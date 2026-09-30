using Microsoft.EntityFrameworkCore;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Modules.SaaS.Domain;

namespace PersonaliPonto.Modules.SaaS.Services;

/// <summary>
/// Faturamento mensal por competência e régua de inadimplência. Suspensão bloqueia apenas funcionalidades
/// de uso (novas marcações, cadastros, tratamentos); espelhos, comprovantes, AFD e AEJ continuam acessíveis.
/// </summary>
public sealed class FaturamentoService(ISaasDbContext db, AuditService audit, IClock clock)
{
    public DateOnly Hoje => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, Fuso).DateTime);
    private static readonly TimeZoneInfo Fuso = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    /// <summary>Gera (idempotente) as faturas da competência para as assinaturas vigentes.</summary>
    public async Task<int> GerarCompetenciaAsync(DateOnly competencia, CancellationToken ct)
    {
        competencia = new DateOnly(competencia.Year, competencia.Month, 1);
        var fimMes = competencia.AddMonths(1).AddDays(-1);
        // Clientes em período de teste não são faturados.
        var emTeste = await db.Tenants.AsNoTracking().Where(t => t.Status == TenantStatus.Teste).Select(t => t.Id).ToListAsync(ct);
        var assinaturas = await db.Assinaturas.AsNoTracking()
            .Where(a => a.Inicio <= fimMes && (a.CanceladaEm == null || a.CanceladaEm >= competencia) && !emTeste.Contains(a.TenantId))
            .ToListAsync(ct);
        var existentes = await db.Faturas.Where(f => f.Competencia == competencia).Select(f => f.TenantId).ToListAsync(ct);
        var novas = 0;
        foreach (var a in assinaturas.Where(a => !existentes.Contains(a.TenantId)))
        {
            db.Faturas.Add(new Fatura
            {
                TenantId = a.TenantId,
                Competencia = competencia,
                Valor = a.ValorMensal,
                Vencimento = PrimeiroVencimento(a, competencia),
                Status = a.ValorMensal <= 0 ? StatusFatura.NaoCobrada : StatusFatura.Aberta,
                CriadaEm = clock.UtcNow
            });
            novas++;
        }
        if (novas > 0) audit.Registrar("faturamento.competencia_gerada", nameof(Fatura), competencia.ToString("yyyy-MM"), new { novas });
        await db.SaveChangesAsync(ct);
        return novas;
    }

    /// <summary>
    /// Vencimento da competência; na competência da contratação nunca vence antes de 7 dias após o início
    /// (evita fatura já vencida para quem contrata depois do dia de vencimento).
    /// </summary>
    public static DateOnly PrimeiroVencimento(Assinatura a, DateOnly competencia)
    {
        var normal = new DateOnly(competencia.Year, competencia.Month, Math.Min(a.DiaVencimento, 28));
        var minimo = a.Inicio.AddDays(7);
        return normal < minimo ? minimo : normal;
    }

    public async Task RegistrarPagamentoAsync(Guid faturaId, DateOnly pagaEm, decimal valorPago, string? forma, string? observacao, CancellationToken ct)
    {
        var f = await db.Faturas.FirstOrDefaultAsync(x => x.Id == faturaId, ct) ?? throw new NaoEncontradoException("Fatura não encontrada.");
        if (f.Status is StatusFatura.Paga) throw new RegraNegocioException("Fatura já paga.");
        if (valorPago <= 0) throw new RegraNegocioException("Valor pago inválido.");
        f.Status = StatusFatura.Paga;
        f.PagaEm = pagaEm;
        f.ValorPago = valorPago;
        f.FormaPagamento = forma;
        f.Observacao = observacao;
        f.AtualizadaEm = clock.UtcNow;
        audit.Registrar("fatura.paga", nameof(Fatura), f.Id, new { f.Competencia, valorPago, pagaEm, forma }, f.TenantId);
        await db.SaveChangesAsync(ct);
        await AvaliarTenantAsync(f.TenantId, ct);
    }

    public async Task MarcarNaoCobradaAsync(Guid faturaId, string motivo, CancellationToken ct)
    {
        var f = await db.Faturas.FirstOrDefaultAsync(x => x.Id == faturaId, ct) ?? throw new NaoEncontradoException("Fatura não encontrada.");
        if (f.Status == StatusFatura.Paga) throw new RegraNegocioException("Fatura já paga.");
        f.Status = StatusFatura.NaoCobrada;
        f.Observacao = motivo;
        f.AtualizadaEm = clock.UtcNow;
        audit.Registrar("fatura.nao_cobrada", nameof(Fatura), f.Id, new { f.Competencia, motivo }, f.TenantId);
        await db.SaveChangesAsync(ct);
        await AvaliarTenantAsync(f.TenantId, ct);
    }

    public async Task EstornarPagamentoAsync(Guid faturaId, string motivo, CancellationToken ct)
    {
        var f = await db.Faturas.FirstOrDefaultAsync(x => x.Id == faturaId, ct) ?? throw new NaoEncontradoException("Fatura não encontrada.");
        if (f.Status != StatusFatura.Paga && f.Status != StatusFatura.NaoCobrada) throw new RegraNegocioException("Fatura não está paga.");
        f.Status = StatusFatura.Aberta;
        f.PagaEm = null;
        f.ValorPago = null;
        f.Observacao = motivo;
        f.AtualizadaEm = clock.UtcNow;
        audit.Registrar("fatura.reaberta", nameof(Fatura), f.Id, new { f.Competencia, motivo }, f.TenantId);
        await db.SaveChangesAsync(ct);
        await AvaliarTenantAsync(f.TenantId, ct);
    }

    /// <summary>Régua diária: aplica inadimplência/suspensão automática e libera quem regularizou.</summary>
    public async Task<int> ProcessarInadimplenciaAsync(CancellationToken ct)
    {
        var ids = await db.Assinaturas.AsNoTracking().Where(a => a.CanceladaEm == null).Select(a => a.TenantId).ToListAsync(ct);
        var alterados = 0;
        foreach (var id in ids) if (await AvaliarTenantAsync(id, ct)) alterados++;
        return alterados;
    }

    /// <summary>Reavalia o status de um cliente com base nas faturas vencidas. Retorna true se mudou.</summary>
    public async Task<bool> AvaliarTenantAsync(Guid tenantId, CancellationToken ct)
    {
        var t = await db.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId, ct);
        var a = await db.Assinaturas.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId, ct);
        if (t is null || a is null || t.Status == TenantStatus.Cancelado) return false;

        var hoje = Hoje;
        var maisAntiga = await db.Faturas.AsNoTracking()
            .Where(f => f.TenantId == tenantId && f.Status == StatusFatura.Aberta && f.Vencimento < hoje)
            .OrderBy(f => f.Vencimento).Select(f => (DateOnly?)f.Vencimento).FirstOrDefaultAsync(ct);

        var novo = t.Status;
        if (maisAntiga is null)
        {
            if (t.Status is TenantStatus.Inadimplente || (t.Status == TenantStatus.Suspenso && SuspensaoAutomatica(t)))
                novo = TenantStatus.Ativo;
        }
        else
        {
            var dias = hoje.DayNumber - maisAntiga.Value.DayNumber;
            if (a.BloqueioAutomatico && dias > a.DiasParaBloqueio) novo = TenantStatus.Suspenso;
            else if (dias > a.DiasTolerancia && t.Status is TenantStatus.Ativo or TenantStatus.Teste) novo = TenantStatus.Inadimplente;
        }

        if (novo == t.Status) return false;
        audit.Registrar("cliente.status_automatico", nameof(Tenant), t.Id, new { anterior = t.Status, novo, faturaVencidaDesde = maisAntiga }, tenantId);
        t.Status = novo;
        await db.SaveChangesAsync(ct);
        return true;
    }

    // Suspensões manuais ficam registradas com a ação "cliente.suspenso"; a régua só desfaz as automáticas.
    private bool SuspensaoAutomatica(Tenant t) =>
        !db.AuditLogs.Where(l => l.TenantId == t.Id && (l.Acao == "cliente.suspenso" || l.Acao == "cliente.status_automatico"))
            .OrderByDescending(l => l.Em).Select(l => l.Acao).Take(1).Contains("cliente.suspenso");
}

/// <summary>Política de uso por status do cliente (usada pela API, painel e terminal).</summary>
public sealed class TenantUsoPolicy(ISaasDbContext db)
{
    public async Task<(bool Pode, string? Mensagem)> PodeUsarAsync(Guid tenantId, CancellationToken ct)
    {
        var t = await db.Tenants.AsNoTracking().Where(x => x.Id == tenantId).Select(x => new { x.Status, x.TesteAte }).FirstOrDefaultAsync(ct);
        if (t is null) return (false, "Cliente não encontrado.");
        return t.Status switch
        {
            TenantStatus.Suspenso => (false, "Acesso suspenso por pendência financeira. Os registros e relatórios continuam disponíveis para consulta."),
            TenantStatus.Cancelado => (false, "Contrato cancelado. Os registros continuam disponíveis para consulta e exportação."),
            TenantStatus.Teste when t.TesteAte < DateTimeOffset.UtcNow => (false, "Período de teste encerrado. Entre em contato para contratar."),
            _ => (true, null)
        };
    }
}

/// <summary>Bloqueia novas marcações de clientes suspensos/cancelados (interceptor do núcleo).</summary>
public sealed class TenantUsoInterceptor(TenantUsoPolicy policy) : IMarcacaoInterceptor
{
    public async Task<MarcacaoAvaliacao> AvaliarAsync(MarcacaoContextoEntrada e, CancellationToken ct)
    {
        var (pode, msg) = await policy.PodeUsarAsync(e.TenantId, ct);
        return pode ? MarcacaoAvaliacao.Livre : new MarcacaoAvaliacao(true, null, msg);
    }
}
