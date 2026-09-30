using Microsoft.EntityFrameworkCore;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Modules.SaaS.Domain;

namespace PersonaliPonto.Modules.SaaS.Services;

public sealed record IndicadoresPlataforma(
    int Ativos, int EmTeste, int Inadimplentes, int Suspensos, int Cancelados,
    decimal ReceitaRecorrente, decimal CustoRecorrente, decimal Margem,
    decimal FaturadoCompetencia, decimal RecebidoCompetencia, decimal PendenteCompetencia, decimal VencidoTotal,
    int FuncionariosContratados, int FuncionariosCadastrados, int MarcacoesHoje,
    IReadOnlyList<(DateOnly Dia, int Quantidade)> MarcacoesPorDia,
    IReadOnlyList<(DateOnly Competencia, decimal Faturado, decimal Recebido)> ReceitaPorMes);

public enum CelulaFatura
{
    Vazia,
    Paga,
    Aberta,
    Vencida,
    NaoCobrada,
    Cancelada
}

public sealed record LinhaGrade(
    Guid TenantId, string RazaoSocial, string Cnpj, string? Telefone, TenantStatus Status, decimal Valor, int Funcionarios,
    decimal Custo, string? Fornecedor, bool BloqueioAutomatico, IReadOnlyDictionary<DateOnly, (CelulaFatura Estado, Guid? FaturaId)> Meses,
    decimal EmAberto);

/// <summary>Painel do Super Admin: indicadores e grade clientes × competências (substitui a planilha).</summary>
public sealed class PainelPlataformaService(ISaasDbContext db, FaturamentoService faturamento)
{
    public async Task<IndicadoresPlataforma> IndicadoresAsync(CancellationToken ct)
    {
        var hoje = faturamento.Hoje;
        var competencia = new DateOnly(hoje.Year, hoje.Month, 1);
        var status = await db.Tenants.AsNoTracking().GroupBy(t => t.Status).Select(g => new { g.Key, Qt = g.Count() }).ToListAsync(ct);
        int Qt(TenantStatus s) => status.FirstOrDefault(x => x.Key == s)?.Qt ?? 0;

        var vigentes = await db.Assinaturas.AsNoTracking().Where(a => a.CanceladaEm == null).ToListAsync(ct);
        var faturasMes = await db.Faturas.AsNoTracking().Where(f => f.Competencia == competencia).ToListAsync(ct);
        var vencido = await db.Faturas.AsNoTracking().Where(f => f.Status == StatusFatura.Aberta && f.Vencimento < hoje).SumAsync(f => (decimal?)f.Valor, ct) ?? 0;

        var inicioSerie = competencia.AddMonths(-11);
        var serie = await db.Faturas.AsNoTracking().Where(f => f.Competencia >= inicioSerie)
            .GroupBy(f => f.Competencia)
            .Select(g => new
            {
                g.Key,
                Faturado = g.Where(f => f.Status != StatusFatura.NaoCobrada && f.Status != StatusFatura.Cancelada).Sum(f => f.Valor),
                Recebido = g.Where(f => f.Status == StatusFatura.Paga).Sum(f => f.ValorPago ?? f.Valor)
            })
            .ToListAsync(ct);

        var fuso = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        var inicioHoje = new DateTimeOffset(hoje.ToDateTime(TimeOnly.MinValue), fuso.GetUtcOffset(hoje.ToDateTime(TimeOnly.MinValue))).ToUniversalTime();
        var inicio30 = inicioHoje.AddDays(-29);
        var marcacoes = await db.RegistrosRep.AsNoTracking()
            .Where(r => r.Tipo == TipoRegistroRep.MarcacaoRepP && r.DataHoraGravacao >= inicio30)
            .Select(r => r.DataHoraGravacao).ToListAsync(ct);
        var porDia = Enumerable.Range(0, 30).Select(i => hoje.AddDays(-29 + i))
            .Select(d => (d, marcacoes.Count(m => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(m, fuso).DateTime) == d))).ToList();

        var receita = vigentes.Sum(a => a.ValorMensal);
        var custo = vigentes.Sum(a => a.CustoMensal);
        return new IndicadoresPlataforma(
            Qt(TenantStatus.Ativo), Qt(TenantStatus.Teste), Qt(TenantStatus.Inadimplente), Qt(TenantStatus.Suspenso), Qt(TenantStatus.Cancelado),
            receita, custo, receita - custo,
            faturasMes.Where(f => f.Status is StatusFatura.Aberta or StatusFatura.Paga).Sum(f => f.Valor),
            faturasMes.Where(f => f.Status == StatusFatura.Paga).Sum(f => f.ValorPago ?? f.Valor),
            faturasMes.Where(f => f.Status == StatusFatura.Aberta).Sum(f => f.Valor),
            vencido,
            vigentes.Sum(a => a.FuncionariosContratados),
            await db.Funcionarios.AsNoTracking().CountAsync(f => f.Ativo, ct),
            porDia[^1].Item2,
            porDia,
            serie.OrderBy(s => s.Key).Select(s => (s.Key, s.Faturado, s.Recebido)).ToList());
    }

    public async Task<IReadOnlyList<LinhaGrade>> GradeAsync(int ano, string? busca, TenantStatus? status, CancellationToken ct)
    {
        var hoje = faturamento.Hoje;
        var q = from a in db.Assinaturas.AsNoTracking()
                join t in db.Tenants.AsNoTracking() on a.TenantId equals t.Id
                select new { a, t };
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var b = busca.Trim().ToUpper();
            q = q.Where(x => x.a.RazaoSocial.ToUpper().Contains(b) || x.a.Cnpj.Contains(b));
        }
        if (status is not null) q = q.Where(x => x.t.Status == status);
        var lista = await q.OrderBy(x => x.a.RazaoSocial).ToListAsync(ct);

        var ids = lista.Select(x => x.a.TenantId).ToList();
        var inicio = new DateOnly(ano, 1, 1);
        var faturas = await db.Faturas.AsNoTracking().Where(f => ids.Contains(f.TenantId) && f.Competencia >= inicio && f.Competencia < inicio.AddYears(1)).ToListAsync(ct);
        var abertas = await db.Faturas.AsNoTracking().Where(f => ids.Contains(f.TenantId) && f.Status == StatusFatura.Aberta && f.Vencimento < hoje)
            .GroupBy(f => f.TenantId).Select(g => new { g.Key, Total = g.Sum(f => f.Valor) }).ToDictionaryAsync(x => x.Key, x => x.Total, ct);

        return lista.Select(x =>
        {
            var meses = new Dictionary<DateOnly, (CelulaFatura, Guid?)>();
            for (var m = 0; m < 12; m++)
            {
                var comp = inicio.AddMonths(m);
                var f = faturas.FirstOrDefault(y => y.TenantId == x.a.TenantId && y.Competencia == comp);
                meses[comp] = f is null ? (CelulaFatura.Vazia, null) : (f.Status switch
                {
                    StatusFatura.Paga => CelulaFatura.Paga,
                    StatusFatura.NaoCobrada => CelulaFatura.NaoCobrada,
                    StatusFatura.Cancelada => CelulaFatura.Cancelada,
                    _ => f.Vencida(hoje) ? CelulaFatura.Vencida : CelulaFatura.Aberta
                }, f.Id);
            }
            return new LinhaGrade(x.a.TenantId, x.a.RazaoSocial, x.a.Cnpj, x.a.Telefone, x.t.Status, x.a.ValorMensal, x.a.FuncionariosContratados,
                x.a.CustoMensal, x.a.Fornecedor, x.a.BloqueioAutomatico, meses, abertas.GetValueOrDefault(x.a.TenantId));
        }).ToList();
    }
}
