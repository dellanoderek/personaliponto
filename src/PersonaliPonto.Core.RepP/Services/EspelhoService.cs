using Microsoft.EntityFrameworkCore;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Aej;
using PersonaliPonto.Core.RepP.Apuracao;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Formatacao;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Core.RepP.Services;

/// <summary>Complementos da apuração fornecidos pelo módulo de RH (feriados, abonos, escalas, banco de horas).</summary>
public interface IApuracaoComplementos
{
    Task<ComplementosApuracao> ObterAsync(Funcionario f, DateOnly inicio, DateOnly fim, CancellationToken ct);
}

public sealed record ComplementosApuracao(
    IReadOnlySet<DateOnly> Feriados,
    IReadOnlyDictionary<DateOnly, string> Abonos,
    Func<DateOnly, Jornada?>? JornadaDoDia,
    IReadOnlyList<AejAusencia> MovimentosBancoHoras,
    int SaldoBancoHorasMinutos)
{
    public static readonly ComplementosApuracao Vazio = new(new HashSet<DateOnly>(), new Dictionary<DateOnly, string>(), null, [], 0);
}

public sealed record ApuracaoFuncionario(Funcionario Funcionario, Estabelecimento Estabelecimento, TimeZoneInfo Fuso,
    IReadOnlyList<DiaApurado> Dias, ComplementosApuracao Complementos);

/// <summary>Espelho de Ponto Eletrônico (art. 84) gerado a partir do ARP + tratamentos.</summary>
public sealed class EspelhoService(IRepPDbContext db, TratamentoService tratamentos, IClock clock, IEnumerable<IApuracaoComplementos> complementos)
{
    public async Task<ApuracaoFuncionario> ApurarAsync(Guid funcionarioId, DateOnly inicio, DateOnly fim, CancellationToken ct)
    {
        if (fim < inicio) throw new RegraNegocioException("Período inválido.");
        if (fim.DayNumber - inicio.DayNumber > 62) throw new RegraNegocioException("Período máximo de 62 dias.");

        var f = await db.Funcionarios.AsNoTracking().Include(x => x.Jornada).Include(x => x.Estabelecimento).ThenInclude(e => e!.Empregador)
                    .FirstOrDefaultAsync(x => x.Id == funcionarioId, ct)
                ?? throw new NaoEncontradoException("Funcionário não encontrado.");
        var fuso = TimeZoneInfo.FindSystemTimeZoneById(f.Estabelecimento!.FusoHorario);

        // Janela ampliada para capturar jornadas que atravessam a meia-noite nas bordas do período.
        var de = new DateTimeOffset(inicio.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var ate = new DateTimeOffset(fim.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var originais = await db.RegistrosRep.AsNoTracking()
            .Where(r => r.FuncionarioId == f.Id && r.Tipo == TipoRegistroRep.MarcacaoRepP && r.DataHoraMarcacao >= de && r.DataHoraMarcacao < ate)
            .Select(r => new { r.Id, r.Nsr, r.DataHoraMarcacao })
            .ToListAsync(ct);

        var trats = await tratamentos.AtivosQuery().Where(t => t.FuncionarioId == f.Id).ToListAsync(ct);
        var desconsideradas = trats.Where(t => t.Tipo == TipoTratamento.Desconsideracao)
            .ToDictionary(t => t.RegistroRepId!.Value, t => t);

        var marcacoes = new List<MarcacaoApurada>();
        foreach (var o in originais)
        {
            var d = desconsideradas.GetValueOrDefault(o.Id);
            marcacoes.Add(new MarcacaoApurada(o.DataHoraMarcacao!.Value, FonteMarcacao.Original, d is not null, d?.Motivo, o.Nsr, o.Id, d?.Id));
        }
        foreach (var t in trats.Where(t => t.Tipo is TipoTratamento.Inclusao or TipoTratamento.PreAssinalada && t.DataHora >= de && t.DataHora < ate))
        {
            marcacoes.Add(new MarcacaoApurada(t.DataHora!.Value,
                t.Tipo == TipoTratamento.Inclusao ? FonteMarcacao.Incluida : FonteMarcacao.PreAssinalada,
                false, t.Motivo, null, null, t.Id));
        }

        var comp = ComplementosApuracao.Vazio;
        foreach (var c in complementos)
        {
            comp = await c.ObterAsync(f, inicio, fim, ct);
            break;
        }

        var dias = ApuracaoJornada.Apurar(inicio, fim, fuso, comp.JornadaDoDia ?? (_ => f.Jornada), marcacoes,
            new ParametrosApuracao
            {
                Feriados = comp.Feriados, Abonos = comp.Abonos,
                Hoje = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, fuso).DateTime)
            }, f.DataAdmissao, f.DataDemissao);

        return new ApuracaoFuncionario(f, f.Estabelecimento, fuso, dias, comp);
    }

    public async Task<EspelhoDto> GerarAsync(Guid funcionarioId, DateOnly inicio, DateOnly fim, CancellationToken ct)
    {
        var a = await ApurarAsync(funcionarioId, inicio, fim, ct);
        var f = a.Funcionario;
        var e = a.Estabelecimento;

        string Fonte(MarcacaoApurada m) => m.Fonte switch
        {
            FonteMarcacao.Original => "Original",
            FonteMarcacao.Incluida => "Incluída",
            _ => "Pré-assinalada"
        };

        var dias = a.Dias.Select(d => new EspelhoDiaDto(
            d.Data,
            DescreverHorario(d.Horario),
            d.MinutosPrevistos, d.MinutosTrabalhados, d.MinutosNoturnos, d.MinutosExtras, d.MinutosAtraso, d.MinutosFalta,
            d.Inconsistente, d.Ocorrencia,
            d.Marcacoes.Select(m => new EspelhoMarcacaoDto(TimeZoneInfo.ConvertTime(m.Instante, a.Fuso), Fonte(m), m.Desconsiderada, m.Motivo, m.Nsr, m.RegistroRepId)).ToList()
        )).ToList();

        var identificador = e.TipoIdentificador == TipoIdentificador.Cnpj ? Documentos.FormatarCnpj(e.Identificador) : Documentos.FormatarCpf(e.Identificador);
        if (!string.IsNullOrEmpty(e.CnoCaepf)) identificador += $" — CNO/CAEPF {e.CnoCaepf}";

        return new EspelhoDto(
            e.Empregador!.RazaoSocial,
            identificador,
            f.Nome,
            Documentos.FormatarCpf(f.Cpf),
            f.DataAdmissao,
            f.Cargo,
            f.Jornada is null ? null : $"{f.Jornada.Codigo} — {f.Jornada.Nome}",
            inicio, fim,
            TimeZoneInfo.ConvertTime(clock.UtcNow, a.Fuso),
            dias,
            dias.Sum(x => x.MinutosTrabalhados),
            dias.Sum(x => x.MinutosExtras),
            dias.Sum(x => x.MinutosAtraso),
            dias.Sum(x => x.MinutosFalta),
            a.Complementos.SaldoBancoHorasMinutos);
    }

    public static string? DescreverHorario(HorarioDia? h) =>
        h is null ? null : string.Join(" / ", h.Periodos.Select(p => $"{p.Entrada:HH\\:mm}–{p.Saida:HH\\:mm}"));

    public static string Hhmm(int minutos)
    {
        var sinal = minutos < 0 ? "-" : "";
        minutos = Math.Abs(minutos);
        return $"{sinal}{minutos / 60:00}:{minutos % 60:00}";
    }
}
