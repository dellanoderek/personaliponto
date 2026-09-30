using PersonaliPonto.Core.RepP.Domain;

namespace PersonaliPonto.Core.RepP.Apuracao;

/// <summary>Fonte da marcação, conforme campo fonteMarc do AEJ.</summary>
public enum FonteMarcacao
{
    Original = 'O',
    Incluida = 'I',
    PreAssinalada = 'P'
}

public sealed record MarcacaoApurada(
    DateTimeOffset Instante,
    FonteMarcacao Fonte,
    bool Desconsiderada,
    string? Motivo,
    long? Nsr,
    Guid? RegistroRepId,
    Guid? TratamentoId);

public sealed record DiaApurado(
    DateOnly Data,
    HorarioDia? Horario,
    int MinutosPrevistos,
    int MinutosTrabalhados,
    int MinutosNoturnos,
    int MinutosExtras,
    int MinutosAtraso,
    int MinutosFalta,
    bool Inconsistente,
    string? Ocorrencia,
    IReadOnlyList<MarcacaoApurada> Marcacoes,
    /// <summary>Marcações válidas pareadas na sequência E1,S1,E2,S2...</summary>
    IReadOnlyList<MarcacaoApurada> Validas,
    Jornada? Jornada = null);

public sealed record ParametrosApuracao
{
    /// <summary>Intervalo mínimo entre marcações para considerar uma nova jornada diária.</summary>
    public TimeSpan QuebraJornada { get; init; } = TimeSpan.FromHours(6);
    public TimeOnly InicioNoturno { get; init; } = new(22, 0);
    public TimeOnly FimNoturno { get; init; } = new(5, 0);
    public bool AplicarHoraNoturnaReduzida { get; init; } = true;
    public IReadOnlySet<DateOnly> Feriados { get; init; } = new HashSet<DateOnly>();
    /// <summary>Dias abonados (atestados/justificativas aprovadas) com descrição.</summary>
    public IReadOnlyDictionary<DateOnly, string> Abonos { get; init; } = new Dictionary<DateOnly, string>();
    /// <summary>Dia corrente: dias futuros e o próprio dia ainda sem marcação não geram falta.</summary>
    public DateOnly? Hoje { get; init; }
}

/// <summary>
/// Motor de apuração do Programa de Tratamento de Registro de Ponto (PTRP). Puro e determinístico:
/// recebe marcações (originais + tratadas), jornada e parâmetros; devolve a apuração diária.
/// </summary>
public static class ApuracaoJornada
{
    private const double FatorNoturno = 60.0 / 52.5;
    private static readonly TimeSpan InterjornadaMinima = TimeSpan.FromHours(11);

    public static IReadOnlyList<DiaApurado> Apurar(
        DateOnly inicio,
        DateOnly fim,
        TimeZoneInfo fuso,
        Jornada? jornada,
        IEnumerable<MarcacaoApurada> marcacoes,
        ParametrosApuracao? parametros = null,
        DateOnly? admissao = null,
        DateOnly? demissao = null) =>
        Apurar(inicio, fim, fuso, _ => jornada, marcacoes, parametros, admissao, demissao);

    /// <summary>Apuração com jornada variável por dia (escalas vigentes por período).</summary>
    public static IReadOnlyList<DiaApurado> Apurar(
        DateOnly inicio,
        DateOnly fim,
        TimeZoneInfo fuso,
        Func<DateOnly, Jornada?> jornadaDoDia,
        IEnumerable<MarcacaoApurada> marcacoes,
        ParametrosApuracao? parametros = null,
        DateOnly? admissao = null,
        DateOnly? demissao = null)
    {
        var p = parametros ?? new ParametrosApuracao();
        var todas = marcacoes.OrderBy(m => m.Instante).ToList();
        var porDia = AgruparPorJornada(todas, fuso, p.QuebraJornada);
        var dias = new List<DiaApurado>();

        for (var d = inicio; d <= fim; d = d.AddDays(1))
        {
            var doDia = porDia.GetValueOrDefault(d) ?? [];
            var validas = doDia.Where(m => !m.Desconsiderada).OrderBy(m => m.Instante).ToList();
            var jornada = jornadaDoDia(d);
            var horario = HorarioDoDia(jornada, d);
            var foraContrato = (admissao.HasValue && d < admissao) || (demissao.HasValue && d > demissao);
            var previsto = foraContrato ? 0 : MinutosPrevistos(horario, d, p);

            var (trabalhado, noturno) = MinutosTrabalhados(validas, fuso, p);
            var inconsistente = validas.Count % 2 != 0;
            string? ocorrencia = null;
            int extras = 0, atraso = 0, falta = 0;

            var feriado = p.Feriados.Contains(d);
            var abono = p.Abonos.GetValueOrDefault(d);

            if (feriado)
            {
                ocorrencia = trabalhado > 0 ? "Feriado trabalhado" : "Feriado";
                extras = trabalhado;
            }
            else if (abono is not null)
            {
                ocorrencia = abono;
                extras = Math.Max(0, trabalhado - previsto);
            }
            else if (previsto == 0)
            {
                ocorrencia = trabalhado > 0 ? "Trabalho em dia de folga" : (foraContrato ? null : "Folga");
                extras = trabalhado;
            }
            else if (validas.Count == 0 && p.Hoje is { } hoje && d >= hoje)
            {
                ocorrencia = d == hoje ? "Jornada não iniciada" : null;
            }
            else if (validas.Count == 0)
            {
                falta = previsto;
                ocorrencia = "Falta";
            }
            else
            {
                var diff = trabalhado - previsto;
                var tolerancia = jornada?.ToleranciaDiariaMinutos ?? 10;
                if (Math.Abs(diff) > tolerancia)
                {
                    if (diff > 0) extras = diff;
                    else atraso = -diff;
                }
            }

            if (inconsistente) ocorrencia = (ocorrencia is null ? "" : ocorrencia + "; ") + "Marcações ímpares";

            dias.Add(new DiaApurado(d, horario, previsto, trabalhado, noturno, extras, atraso, falta,
                inconsistente, ocorrencia, doDia.OrderBy(m => m.Instante).ToList(), validas, jornada));
        }

        return dias;
    }

    /// <summary>
    /// Agrupa marcações em jornadas diárias: uma nova jornada começa quando o intervalo desde a
    /// marcação válida anterior supera <paramref name="quebra"/>. A jornada pertence ao dia local da
    /// sua primeira marcação (FAQ MTE nº 58 — jornadas que atravessam a meia-noite).
    /// </summary>
    public static Dictionary<DateOnly, List<MarcacaoApurada>> AgruparPorJornada(
        IReadOnlyList<MarcacaoApurada> ordenadas, TimeZoneInfo fuso, TimeSpan quebra)
    {
        var resultado = new Dictionary<DateOnly, List<MarcacaoApurada>>();
        DateOnly? diaAtual = null;
        DateTimeOffset? ultima = null;
        var validasNaJornada = 0;

        foreach (var m in ordenadas)
        {
            // Após uma saída (quantidade par) basta o intervalo de quebra. Com quantidade ímpar (entrada sem
            // saída), só há quebra depois do intervalo mínimo entre jornadas (CLT art. 66: 11h): assim um turno
            // contínuo longo (ex.: 22h–05h) não é dividido e um esquecimento não emenda o dia seguinte.
            var intervalo = ultima is null ? TimeSpan.Zero : m.Instante - ultima.Value;
            var novaJornada = diaAtual is null || ultima is null
                              || (validasNaJornada % 2 == 0 && intervalo > quebra)
                              || intervalo > InterjornadaMinima;
            if (novaJornada)
            {
                diaAtual = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(m.Instante, fuso).DateTime);
                validasNaJornada = 0;
            }

            if (!resultado.TryGetValue(diaAtual!.Value, out var lista))
                resultado[diaAtual.Value] = lista = [];
            lista.Add(m);
            if (!m.Desconsiderada)
            {
                ultima = m.Instante;
                validasNaJornada++;
            }
        }

        return resultado;
    }

    public static HorarioDia? HorarioDoDia(Jornada? jornada, DateOnly data)
    {
        if (jornada is null) return null;
        int indice;
        if (jornada.CicloDias is > 0 && jornada.DataReferenciaCiclo is { } refData)
        {
            var dias = data.DayNumber - refData.DayNumber;
            indice = ((dias % jornada.CicloDias.Value) + jornada.CicloDias.Value) % jornada.CicloDias.Value;
        }
        else
        {
            indice = (int)data.DayOfWeek;
        }
        var h = jornada.Horarios.FirstOrDefault(x => x.Indice == indice);
        return h is { Periodos.Count: > 0 } ? h : null;
    }

    public static int MinutosPrevistos(HorarioDia? horario, DateOnly data, ParametrosApuracao p)
    {
        if (horario is null) return 0;
        double total = 0;
        var baseDia = data.ToDateTime(TimeOnly.MinValue);
        var cursor = baseDia;
        foreach (var per in horario.Periodos)
        {
            var ini = baseDia.Add(per.Entrada.ToTimeSpan());
            while (ini < cursor) ini = ini.AddDays(1);
            var fim = baseDia.Add(per.Saida.ToTimeSpan());
            while (fim <= ini) fim = fim.AddDays(1);
            total += MinutosComReducao(ini, fim, p, out _);
            cursor = fim;
        }
        return (int)Math.Round(total, MidpointRounding.AwayFromZero);
    }

    public static (int Trabalhado, int Noturno) MinutosTrabalhados(
        IReadOnlyList<MarcacaoApurada> validas, TimeZoneInfo fuso, ParametrosApuracao p)
    {
        double total = 0, noturno = 0;
        for (var i = 0; i + 1 < validas.Count; i += 2)
        {
            var ini = Truncar(TimeZoneInfo.ConvertTime(validas[i].Instante, fuso).DateTime);
            var fim = Truncar(TimeZoneInfo.ConvertTime(validas[i + 1].Instante, fuso).DateTime);
            if (fim <= ini) continue;
            total += MinutosComReducao(ini, fim, p, out var not);
            noturno += not;
        }
        return ((int)Math.Round(total, MidpointRounding.AwayFromZero), (int)Math.Round(noturno, MidpointRounding.AwayFromZero));
    }

    /// <summary>Minutos entre ini e fim, convertendo o trecho noturno em hora reduzida (52min30s).</summary>
    public static double MinutosComReducao(DateTime ini, DateTime fim, ParametrosApuracao p, out double noturnoReal)
    {
        var real = (fim - ini).TotalMinutes;
        noturnoReal = SobreposicaoNoturna(ini, fim, p);
        return p.AplicarHoraNoturnaReduzida ? real - noturnoReal + noturnoReal * FatorNoturno : real;
    }

    public static double SobreposicaoNoturna(DateTime ini, DateTime fim, ParametrosApuracao p)
    {
        double total = 0;
        for (var dia = ini.Date.AddDays(-1); dia <= fim.Date; dia = dia.AddDays(1))
        {
            var nIni = dia.Add(p.InicioNoturno.ToTimeSpan());
            var nFim = dia.Add(p.FimNoturno.ToTimeSpan());
            if (nFim <= nIni) nFim = nFim.AddDays(1);
            var a = ini > nIni ? ini : nIni;
            var b = fim < nFim ? fim : nFim;
            if (b > a) total += (b - a).TotalMinutes;
        }
        return total;
    }

    /// <summary>Segundos são desconsiderados (o AFD/AEJ registram hh:mm:00).</summary>
    private static DateTime Truncar(DateTime dt) => new(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0, dt.Kind);
}
