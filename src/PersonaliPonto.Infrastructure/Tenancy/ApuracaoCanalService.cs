using Microsoft.EntityFrameworkCore;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Infrastructure.Persistence;
using PersonaliPonto.Modules.SaaS.Domain;
using PersonaliPonto.Modules.SaaS.Services;

namespace PersonaliPonto.Infrastructure.Tenancy;

public sealed record UsoApurado(DateOnly Competencia, int EmpresasAtivas, int FuncionariosAtivos, IReadOnlyList<ApuracaoUsoTenant> Detalhes);

public sealed record EstimativaUso(DateOnly Competencia, int EmpresasAtivas, int FuncionariosAtivos, CalculoUso Calculo, bool Cobravel);

/// <summary>
/// Apuração mensal de uso por revendedor (inclui clientes dos parceiros). Empresa ativa = cliente Ativo/Inadimplente
/// com ao menos 1 funcionário ativo; funcionário ativo = CPF com ≥1 marcação REP-P (tipo 7) na competência, contado por
/// cliente. A contagem lê marcações de todos os clientes do canal e por isso roda em modo sistema controlado: o
/// chamador recebe só números agregados (nunca dados de ponto).
/// </summary>
public sealed class ApuracaoCanalService(PersonaliPontoDbContext db, RequestContext ctx, FaturamentoCanalService faturamento, IClock clock)
{
    private static readonly TimeZoneInfo Fuso = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static (DateTimeOffset Inicio, DateTimeOffset Fim) Limites(DateOnly competencia)
    {
        var ini = new DateTime(competencia.Year, competencia.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        return (new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(ini, Fuso), TimeSpan.Zero),
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(ini.AddMonths(1), Fuso), TimeSpan.Zero));
    }

    /// <summary>Calcula o uso do revendedor na competência (sem gravar). Exige modo sistema.</summary>
    public async Task<UsoApurado> CalcularAsync(Guid revendedorId, DateOnly competencia, CancellationToken ct)
    {
        if (!ctx.IsSystem) throw new AcessoNegadoException("Apuração exige modo sistema.");
        competencia = new DateOnly(competencia.Year, competencia.Month, 1);
        var rev = await db.Canais.AsNoTracking().FirstOrDefaultAsync(c => c.Id == revendedorId, ct) ?? throw new NaoEncontradoException("Canal não encontrado.");
        if (rev.Tipo != TipoCanal.Revendedor) throw new RegraNegocioException("A apuração é feita por revendedor.");
        var canais = await db.Canais.AsNoTracking().Where(c => c.Caminho.StartsWith(rev.Caminho)).Select(c => c.Id).ToListAsync(ct);
        var tenants = await db.Tenants.AsNoTracking().Where(t => canais.Contains(t.CanalDonoId))
            .Select(t => new { t.Id, t.Nome, t.Status, t.TipoEntidade, t.CanalDonoId }).ToListAsync(ct);
        var ids = tenants.Select(t => t.Id).ToList();
        var (ini, fim) = Limites(competencia);
        var porTenant = await db.RegistrosRep.AsNoTracking()
            .Where(r => ids.Contains(r.TenantId) && r.Tipo == TipoRegistroRep.MarcacaoRepP && r.Cpf != null
                        && r.DataHoraMarcacao >= ini && r.DataHoraMarcacao < fim)
            .Select(r => new { r.TenantId, r.Cpf }).Distinct()
            .GroupBy(x => x.TenantId).Select(g => new { g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N, ct);

        var detalhes = tenants.OrderBy(t => t.Nome).Select(t =>
        {
            var func = porTenant.GetValueOrDefault(t.Id);
            var ativa = t.Status is TenantStatus.Ativo or TenantStatus.Inadimplente && func > 0;
            return new ApuracaoUsoTenant
            {
                CanalId = rev.Id, CanalDonoId = t.CanalDonoId, ClienteId = t.Id, NomeCliente = t.Nome, StatusCliente = t.Status,
                TipoEntidade = t.TipoEntidade, EmpresaAtiva = ativa, FuncionariosAtivos = ativa ? func : 0
            };
        }).ToList();
        return new UsoApurado(competencia, detalhes.Count(d => d.EmpresaAtiva), detalhes.Sum(d => d.FuncionariosAtivos), detalhes);
    }

    /// <summary>Grava (idempotente) o snapshot imutável da competência para todos os revendedores. Retorna quantos gravou.</summary>
    public async Task<int> ApurarCompetenciaAsync(DateOnly competencia, CancellationToken ct)
    {
        if (!ctx.IsSystem || ctx.CanalId is not null) throw new AcessoNegadoException("Apuração exclusiva da plataforma.");
        competencia = new DateOnly(competencia.Year, competencia.Month, 1);
        var revs = await db.Canais.AsNoTracking().Where(c => c.Tipo == TipoCanal.Revendedor).Select(c => c.Id).ToListAsync(ct);
        var feitos = await db.ApuracoesUsoCanal.AsNoTracking().Where(a => a.Competencia == competencia).Select(a => a.CanalId).ToListAsync(ct);
        var n = 0;
        foreach (var rev in revs.Except(feitos))
        {
            var uso = await CalcularAsync(rev, competencia, ct);
            var ap = new ApuracaoUsoCanal
            {
                CanalId = rev, Competencia = competencia, EmpresasAtivas = uso.EmpresasAtivas, FuncionariosAtivos = uso.FuncionariosAtivos,
                GeradaEm = clock.UtcNow
            };
            db.ApuracoesUsoCanal.Add(ap);
            foreach (var d in uso.Detalhes)
            {
                d.ApuracaoId = ap.Id;
                db.ApuracoesUsoTenant.Add(d);
            }
            try
            {
                await db.SaveChangesAsync(ct);
                n++;
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
            {
                // Outra instância gravou a mesma competência: o snapshot existente prevalece.
                db.ChangeTracker.Clear();
            }
        }
        return n;
    }

    /// <summary>
    /// Uso estimado do mês corrente para o painel do revendedor (ou da plataforma). Devolve apenas números agregados.
    /// </summary>
    public async Task<EstimativaUso> EstimarMesCorrenteAsync(Guid revendedorId, CancellationToken ct)
    {
        if (!ctx.IsSystem && ctx.CanalId != revendedorId) throw new AcessoNegadoException("Estimativa disponível só para o próprio canal.");
        var hoje = faturamento.Hoje;
        var comp = new DateOnly(hoje.Year, hoje.Month, 1);
        UsoApurado uso;
        DateOnly entrada;
        using (ctx.ComoSistema())
        {
            uso = await CalcularAsync(revendedorId, comp, ct);
            entrada = FaturamentoCanalService.DataLocal(await db.Canais.AsNoTracking().Where(c => c.Id == revendedorId).Select(c => c.CriadoEm).FirstAsync(ct));
        }
        var tabela = await faturamento.TabelaVigenteAsync(comp, ct);
        return new EstimativaUso(comp, uso.EmpresasAtivas, uso.FuncionariosAtivos,
            CalculadoraUsoCanal.Calcular(tabela, uso.EmpresasAtivas, uso.FuncionariosAtivos), CalculadoraUsoCanal.Cobravel(entrada, comp));
    }

    /// <summary>Rotina mensal: apura a competência anterior, gera as faturas de canal e aplica a régua de canais.</summary>
    public async Task<(int Apuracoes, int Faturas, int Canais)> ProcessarAsync(CancellationToken ct)
    {
        var hoje = faturamento.Hoje;
        var anterior = new DateOnly(hoje.Year, hoje.Month, 1).AddMonths(-1);
        var a = await ApurarCompetenciaAsync(anterior, ct);
        var f = await faturamento.GerarFaturasAsync(anterior, ct);
        var c = await faturamento.ProcessarReguaAsync(ct);
        return (a, f, c);
    }
}

/// <summary>
/// Situação de canal vista por um cliente: responde apenas se algum canal acima do cliente está suspenso
/// (aviso no painel de RH). Consulta pontual em modo sistema, sem expor dados do canal.
/// </summary>
public sealed class SituacaoCanalCliente(PersonaliPontoDbContext db, RequestContext ctx)
{
    public async Task<bool> CanalSuspensoAsync(Guid tenantId, CancellationToken ct)
    {
        if (!ctx.VeTenant(tenantId)) return false;
        using var _ = ctx.ComoSistema();
        var dono = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => (Guid?)t.CanalDonoId).FirstOrDefaultAsync(ct);
        if (dono is null) return false;
        var caminho = await db.Canais.AsNoTracking().Where(c => c.Id == dono).Select(c => c.Caminho).FirstOrDefaultAsync(ct);
        if (caminho is null) return false;
        var ids = caminho.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList();
        return await db.Canais.AsNoTracking().AnyAsync(c => ids.Contains(c.Id) && c.Status == StatusCanal.Suspenso, ct);
    }
}
