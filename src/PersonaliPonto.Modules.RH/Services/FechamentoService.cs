using Microsoft.EntityFrameworkCore;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Modules.RH.Domain;

namespace PersonaliPonto.Modules.RH.Services;

/// <summary>
/// Fechamento mensal por funcionário: consolida a apuração, lança créditos/débitos no banco de horas
/// e trava novos tratamentos no período. Reabertura estorna os lançamentos e é auditada.
/// </summary>
public sealed class FechamentoService(IRhDbContext db, EspelhoService espelho, AuditService audit, ICurrentUser user, IClock clock)
{
    public async Task GarantirAbertoAsync(Guid funcionarioId, DateOnly data, CancellationToken ct)
    {
        if (await db.Fechamentos.AnyAsync(f => f.FuncionarioId == funcionarioId && f.Ano == data.Year && f.Mes == data.Month
                                               && f.Status == StatusFechamento.Fechado, ct))
            throw new RegraNegocioException($"O período {data:MM/yyyy} está fechado. Reabra o fechamento para alterar.");
    }

    public async Task<FechamentoPeriodo> FecharAsync(Guid funcionarioId, int ano, int mes, CancellationToken ct)
    {
        var inicio = new DateOnly(ano, mes, 1);
        var fim = inicio.AddMonths(1).AddDays(-1);
        var hoje = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        if (fim >= hoje) throw new RegraNegocioException("Só é possível fechar meses encerrados.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var existente = await db.Fechamentos.FirstOrDefaultAsync(f => f.FuncionarioId == funcionarioId && f.Ano == ano && f.Mes == mes, ct);
        if (existente?.Status == StatusFechamento.Fechado) throw new RegraNegocioException("Período já fechado.");

        var a = await espelho.ApurarAsync(funcionarioId, inicio, fim, ct);
        var inconsistentes = a.Dias.Count(d => d.Inconsistente);
        if (inconsistentes > 0)
            throw new RegraNegocioException($"Existem {inconsistentes} dia(s) com marcações ímpares. Trate-os antes de fechar.");

        var f = existente ?? new FechamentoPeriodo { TenantId = a.Funcionario.TenantId, FuncionarioId = funcionarioId, Ano = ano, Mes = mes };
        f.Status = StatusFechamento.Fechado;
        f.MinutosTrabalhados = a.Dias.Sum(d => d.MinutosTrabalhados);
        f.MinutosExtras = a.Dias.Sum(d => d.MinutosExtras);
        f.MinutosAtrasos = a.Dias.Sum(d => d.MinutosAtraso);
        f.MinutosFaltas = a.Dias.Sum(d => d.MinutosFalta);
        f.DiasInconsistentes = 0;
        f.FechadoPor = user.UserId;
        f.FechadoEm = clock.UtcNow;
        f.ReabertoEm = null;
        if (existente is null) db.Fechamentos.Add(f);

        var pol = await db.PoliticasBancoHoras.FirstOrDefaultAsync(ct) ?? new PoliticaBancoHoras();
        if (pol.Ativo)
        {
            if (pol.ExtrasParaBanco && f.MinutosExtras > 0)
                Lancar(f, a.Funcionario.TenantId, fim, f.MinutosExtras, TipoLancamentoBancoHoras.CreditoApuracao, $"Horas extras {mes:00}/{ano}");
            var debito = f.MinutosAtrasos + f.MinutosFaltas;
            if (pol.DescontarAtrasos && debito > 0)
                Lancar(f, a.Funcionario.TenantId, fim, -debito, TipoLancamentoBancoHoras.DebitoApuracao, $"Atrasos/faltas {mes:00}/{ano}");
        }

        audit.Registrar("fechamento.fechado", nameof(FechamentoPeriodo), f.Id, new { funcionarioId, ano, mes, f.MinutosExtras, f.MinutosAtrasos, f.MinutosFaltas });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return f;
    }

    public async Task ReabrirAsync(Guid fechamentoId, string motivo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(motivo)) throw new RegraNegocioException("Informe o motivo da reabertura.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var f = await db.Fechamentos.FirstOrDefaultAsync(x => x.Id == fechamentoId, ct) ?? throw new NaoEncontradoException("Fechamento não encontrado.");
        if (f.Status != StatusFechamento.Fechado) throw new RegraNegocioException("Fechamento não está fechado.");

        var lancamentos = await db.BancoHoras.Where(l => l.FechamentoId == f.Id && l.Tipo != TipoLancamentoBancoHoras.Estorno).ToListAsync(ct);
        var estornados = await db.BancoHoras.Where(l => l.FechamentoId == f.Id && l.Tipo == TipoLancamentoBancoHoras.Estorno)
            .Select(l => l.EstornaLancamentoId).ToListAsync(ct);
        foreach (var l in lancamentos.Where(l => !estornados.Contains(l.Id)))
        {
            db.BancoHoras.Add(new BancoHorasLancamento
            {
                TenantId = l.TenantId, FuncionarioId = l.FuncionarioId, Data = l.Data, Minutos = -l.Minutos,
                Tipo = TipoLancamentoBancoHoras.Estorno, Descricao = $"Estorno (reabertura): {l.Descricao}",
                FechamentoId = f.Id, EstornaLancamentoId = l.Id, CriadoPor = user.UserId, CriadoPorNome = user.Nome, CriadoEm = clock.UtcNow
            });
        }
        f.Status = StatusFechamento.Reaberto;
        f.ReabertoEm = clock.UtcNow;
        audit.Registrar("fechamento.reaberto", nameof(FechamentoPeriodo), f.Id, new { motivo });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private void Lancar(FechamentoPeriodo f, Guid tenantId, DateOnly data, int minutos, TipoLancamentoBancoHoras tipo, string descricao) =>
        db.BancoHoras.Add(new BancoHorasLancamento
        {
            TenantId = tenantId, FuncionarioId = f.FuncionarioId, Data = data, Minutos = minutos, Tipo = tipo,
            Descricao = descricao, FechamentoId = f.Id, CriadoPor = user.UserId, CriadoPorNome = user.Nome, CriadoEm = clock.UtcNow
        });
}
