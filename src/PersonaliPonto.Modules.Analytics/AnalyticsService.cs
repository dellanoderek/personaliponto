using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Formatacao;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Modules.RH;
using PersonaliPonto.Modules.RH.Domain;

namespace PersonaliPonto.Modules.Analytics;

public sealed record ResumoFuncionario(
    Guid FuncionarioId, string Nome, string Matricula, string Estabelecimento, string? Cargo,
    int Trabalhado, int Previsto, int Extras, int Atrasos, int Faltas, int DiasComFalta, int DiasInconsistentes,
    int SaldoBancoHoras, bool ForaDaCerca);

public sealed record IndicadoresEmpresa(
    DateOnly Inicio, DateOnly Fim,
    int FuncionariosAtivos, int PresentesAgora, int MarcacoesHoje,
    int TotalExtras, int TotalAtrasos, int TotalFaltas, double Absenteismo, int SaldoBancoHoras,
    int SolicitacoesPendentes, int AtestadosPendentes, int DiasInconsistentes, int MarcacoesForaCerca,
    IReadOnlyList<(DateOnly Dia, int Extras, int Atrasos, int Faltas)> Serie,
    IReadOnlyList<ResumoFuncionario> Funcionarios);

/// <summary>Indicadores gerenciais e relatórios exportáveis do painel RH (Fase 3).</summary>
public sealed class AnalyticsService(IRhDbContext db, EspelhoService espelho, IClock clock, IMemoryCache cache, ITenantContext tenant)
{
    public async Task<IndicadoresEmpresa> IndicadoresAsync(DateOnly inicio, DateOnly fim, Guid? estabelecimentoId, bool usarCache, CancellationToken ct)
    {
        var chave = $"ind:{tenant.TenantId}:{inicio}:{fim}:{estabelecimentoId}";
        if (usarCache && cache.TryGetValue(chave, out IndicadoresEmpresa? c) && c is not null) return c;

        var funcionarios = await db.Funcionarios.AsNoTracking().Include(f => f.Estabelecimento)
            .Where(f => f.Ativo && (estabelecimentoId == null || f.EstabelecimentoId == estabelecimentoId))
            .OrderBy(f => f.Nome).ToListAsync(ct);

        var resumos = new List<ResumoFuncionario>();
        var serie = new Dictionary<DateOnly, (int E, int A, int F)>();
        int previstoTotal = 0, faltasTotal = 0;
        var foraCercaIds = await ForaDaCercaAsync(inicio, fim, ct);

        foreach (var f in funcionarios)
        {
            var a = await espelho.ApurarAsync(f.Id, inicio, fim, ct);
            foreach (var d in a.Dias)
            {
                var s = serie.GetValueOrDefault(d.Data);
                serie[d.Data] = (s.E + d.MinutosExtras, s.A + d.MinutosAtraso, s.F + d.MinutosFalta);
            }
            var r = new ResumoFuncionario(f.Id, f.Nome, f.Matricula, f.Estabelecimento!.Nome, f.Cargo,
                a.Dias.Sum(d => d.MinutosTrabalhados), a.Dias.Sum(d => d.MinutosPrevistos), a.Dias.Sum(d => d.MinutosExtras),
                a.Dias.Sum(d => d.MinutosAtraso), a.Dias.Sum(d => d.MinutosFalta), a.Dias.Count(d => d.MinutosFalta > 0),
                a.Dias.Count(d => d.Inconsistente), a.Complementos.SaldoBancoHorasMinutos, foraCercaIds.Contains(f.Id));
            resumos.Add(r);
            previstoTotal += r.Previsto;
            faltasTotal += r.Faltas;
        }

        var (presentes, marcacoesHoje) = await PresencaAgoraAsync(estabelecimentoId, ct);
        var ind = new IndicadoresEmpresa(inicio, fim, funcionarios.Count, presentes, marcacoesHoje,
            resumos.Sum(r => r.Extras), resumos.Sum(r => r.Atrasos), faltasTotal,
            previstoTotal == 0 ? 0 : Math.Round(100.0 * (faltasTotal + resumos.Sum(r => r.Atrasos)) / previstoTotal, 1),
            resumos.Sum(r => r.SaldoBancoHoras),
            await db.Solicitacoes.CountAsync(s => s.Status == StatusSolicitacao.Pendente, ct),
            await db.Atestados.CountAsync(s => s.Status == StatusSolicitacao.Pendente, ct),
            resumos.Sum(r => r.DiasInconsistentes),
            foraCercaIds.Count,
            serie.OrderBy(x => x.Key).Select(x => (x.Key, x.Value.E, x.Value.A, x.Value.F)).ToList(),
            resumos);
        cache.Set(chave, ind, TimeSpan.FromMinutes(5));
        return ind;
    }

    /// <summary>Quem está trabalhando agora (última marcação válida de hoje é entrada) e total de marcações do dia.</summary>
    public async Task<(int Presentes, int Marcacoes)> PresencaAgoraAsync(Guid? estabelecimentoId, CancellationToken ct)
    {
        var agora = clock.UtcNow;
        var desde = agora.AddHours(-16);
        var marcas = await db.RegistrosRep.AsNoTracking()
            .Where(r => r.Tipo == TipoRegistroRep.MarcacaoRepP && r.DataHoraMarcacao >= desde
                        && (estabelecimentoId == null || r.EstabelecimentoId == estabelecimentoId))
            .Select(r => new { r.FuncionarioId, r.DataHoraMarcacao }).ToListAsync(ct);
        var presentes = marcas.GroupBy(m => m.FuncionarioId).Count(g => g.Count() % 2 == 1);
        var inicioDia = agora.AddHours(-(agora.ToOffset(TimeSpan.FromHours(-3)).TimeOfDay.TotalHours));
        return (presentes, marcas.Count(m => m.DataHoraMarcacao >= inicioDia));
    }

    private async Task<HashSet<Guid>> ForaDaCercaAsync(DateOnly inicio, DateOnly fim, CancellationToken ct)
    {
        var de = new DateTimeOffset(inicio.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var ate = new DateTimeOffset(fim.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var ids = await (from c in db.MarcacoesContexto.AsNoTracking()
                         join r in db.RegistrosRep.AsNoTracking() on c.RegistroRepId equals r.Id
                         where c.DentroCerca == false && r.DataHoraMarcacao >= de && r.DataHoraMarcacao < ate
                         select r.FuncionarioId!.Value).Distinct().ToListAsync(ct);
        return ids.ToHashSet();
    }

    // ---------------- Relatórios exportáveis ----------------

    public async Task<byte[]> RelatorioResumoXlsxAsync(DateOnly inicio, DateOnly fim, Guid? estabelecimentoId, CancellationToken ct)
    {
        var ind = await IndicadoresAsync(inicio, fim, estabelecimentoId, false, ct);
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Resumo");
        ws.Cell(1, 1).Value = $"Resumo de jornada — {inicio:dd/MM/yyyy} a {fim:dd/MM/yyyy}";
        ws.Range(1, 1, 1, 12).Merge().Style.Font.SetBold().Font.SetFontSize(14);
        string[] cab = ["Funcionário", "Matrícula", "Estabelecimento", "Cargo", "Previsto", "Trabalhado", "Extras", "Atrasos", "Faltas", "Dias c/ falta", "Inconsistências", "Banco de horas"];
        for (var i = 0; i < cab.Length; i++) ws.Cell(3, i + 1).Value = cab[i];
        Cabecalho(ws.Range(3, 1, 3, cab.Length));
        var l = 4;
        foreach (var r in ind.Funcionarios)
        {
            ws.Cell(l, 1).Value = r.Nome;
            ws.Cell(l, 2).Value = r.Matricula;
            ws.Cell(l, 3).Value = r.Estabelecimento;
            ws.Cell(l, 4).Value = r.Cargo;
            ws.Cell(l, 5).Value = EspelhoService.Hhmm(r.Previsto);
            ws.Cell(l, 6).Value = EspelhoService.Hhmm(r.Trabalhado);
            ws.Cell(l, 7).Value = EspelhoService.Hhmm(r.Extras);
            ws.Cell(l, 8).Value = EspelhoService.Hhmm(r.Atrasos);
            ws.Cell(l, 9).Value = EspelhoService.Hhmm(r.Faltas);
            ws.Cell(l, 10).Value = r.DiasComFalta;
            ws.Cell(l, 11).Value = r.DiasInconsistentes;
            ws.Cell(l, 12).Value = EspelhoService.Hhmm(r.SaldoBancoHoras);
            l++;
        }
        ws.Columns().AdjustToContents();
        return Salvar(wb);
    }

    public async Task<byte[]> RelatorioMarcacoesXlsxAsync(DateOnly inicio, DateOnly fim, Guid? estabelecimentoId, CancellationToken ct)
    {
        var de = new DateTimeOffset(inicio.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(-3));
        var ate = new DateTimeOffset(fim.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(-3));
        var dados = await (from r in db.RegistrosRep.AsNoTracking()
                           join f in db.Funcionarios.AsNoTracking() on r.FuncionarioId equals f.Id
                           join e in db.Estabelecimentos.AsNoTracking() on r.EstabelecimentoId equals e.Id
                           join c in db.MarcacoesContexto.AsNoTracking() on r.Id equals c.RegistroRepId into cs
                           from c in cs.DefaultIfEmpty()
                           where r.Tipo == TipoRegistroRep.MarcacaoRepP && r.DataHoraMarcacao >= de && r.DataHoraMarcacao < ate
                                 && (estabelecimentoId == null || r.EstabelecimentoId == estabelecimentoId)
                           orderby r.DataHoraMarcacao
                           select new { r.Nsr, r.DataHoraMarcacao, r.OffsetMarcacaoMinutos, r.Coletor, r.Offline, r.Hash, f.Nome, f.Matricula, f.Cpf, Est = e.Nome, Dentro = (bool?)c.DentroCerca })
            .ToListAsync(ct);

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Marcações");
        string[] cab = ["NSR", "Data", "Hora", "Funcionário", "Matrícula", "CPF", "Estabelecimento", "Coletor", "Off-line", "Cerca", "Hash"];
        for (var i = 0; i < cab.Length; i++) ws.Cell(1, i + 1).Value = cab[i];
        Cabecalho(ws.Range(1, 1, 1, cab.Length));
        var l = 2;
        foreach (var d in dados)
        {
            var local = d.DataHoraMarcacao!.Value.ToOffset(TimeSpan.FromMinutes(d.OffsetMarcacaoMinutos ?? -180));
            ws.Cell(l, 1).Value = d.Nsr;
            ws.Cell(l, 2).Value = local.ToString("dd/MM/yyyy");
            ws.Cell(l, 3).Value = local.ToString("HH:mm");
            ws.Cell(l, 4).Value = d.Nome;
            ws.Cell(l, 5).Value = d.Matricula;
            ws.Cell(l, 6).Value = Documentos.FormatarCpf(d.Cpf);
            ws.Cell(l, 7).Value = d.Est;
            ws.Cell(l, 8).Value = d.Coletor?.ToString();
            ws.Cell(l, 9).Value = d.Offline == true ? "Sim" : "Não";
            ws.Cell(l, 10).Value = d.Dentro is null ? "-" : d.Dentro.Value ? "Dentro" : "Fora";
            ws.Cell(l, 11).Value = d.Hash;
            l++;
        }
        ws.Columns().AdjustToContents();
        return Salvar(wb);
    }

    public async Task<byte[]> RelatorioBancoHorasXlsxAsync(CancellationToken ct)
    {
        var saldos = await (from f in db.Funcionarios.AsNoTracking()
                            where f.Ativo
                            select new { f.Nome, f.Matricula, Saldo = db.BancoHoras.Where(b => b.FuncionarioId == f.Id).Sum(b => (int?)b.Minutos) ?? 0 })
            .OrderBy(x => x.Nome).ToListAsync(ct);
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Banco de horas");
        ws.Cell(1, 1).Value = "Funcionário";
        ws.Cell(1, 2).Value = "Matrícula";
        ws.Cell(1, 3).Value = "Saldo (hh:mm)";
        ws.Cell(1, 4).Value = "Saldo (min)";
        Cabecalho(ws.Range(1, 1, 1, 4));
        var l = 2;
        foreach (var s in saldos)
        {
            ws.Cell(l, 1).Value = s.Nome;
            ws.Cell(l, 2).Value = s.Matricula;
            ws.Cell(l, 3).Value = EspelhoService.Hhmm(s.Saldo);
            ws.Cell(l, 4).Value = s.Saldo;
            l++;
        }
        ws.Columns().AdjustToContents();
        return Salvar(wb);
    }

    private static void Cabecalho(IXLRange r)
    {
        r.Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(XLColor.FromHtml("#082352"));
    }

    private static byte[] Salvar(XLWorkbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}

public static class AnalyticsModule
{
    public static IServiceCollection AddModuloAnalytics(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddScoped<AnalyticsService>();
        return services;
    }
}
