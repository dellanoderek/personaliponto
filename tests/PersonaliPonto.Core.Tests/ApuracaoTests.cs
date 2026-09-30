using PersonaliPonto.Core.RepP.Apuracao;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Services;

namespace PersonaliPonto.Core.Tests;

public class ApuracaoTests
{
    private static readonly TimeZoneInfo Sp = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private static MarcacaoApurada M(int ano, int mes, int dia, int h, int min, bool desc = false, FonteMarcacao f = FonteMarcacao.Original) =>
        new(new DateTimeOffset(ano, mes, dia, h, min, 0, TimeSpan.FromHours(-3)), f, desc, desc ? "teste" : null, 1, Guid.NewGuid(), null);

    private static readonly Jornada Comercial = CadastroService.Modelo(TipoJornada.Semanal5x2, "COM", "Comercial");

    [Fact]
    public void Dia_normal_sem_extras_nem_atraso()
    {
        // Terça 01/09/2026: 08:00-12:00 / 13:00-17:48 = 528 min previstos.
        var dias = ApuracaoJornada.Apurar(new(2026, 9, 1), new(2026, 9, 1), Sp, Comercial,
            [M(2026, 9, 1, 8, 0), M(2026, 9, 1, 12, 0), M(2026, 9, 1, 13, 0), M(2026, 9, 1, 17, 48)]);
        var d = Assert.Single(dias);
        Assert.Equal(528, d.MinutosPrevistos);
        Assert.Equal(528, d.MinutosTrabalhados);
        Assert.Equal(0, d.MinutosExtras);
        Assert.Equal(0, d.MinutosAtraso);
        Assert.False(d.Inconsistente);
    }

    [Fact]
    public void Tolerancia_diaria_de_10_minutos()
    {
        var dias = ApuracaoJornada.Apurar(new(2026, 9, 1), new(2026, 9, 1), Sp, Comercial,
            [M(2026, 9, 1, 8, 5), M(2026, 9, 1, 12, 0), M(2026, 9, 1, 13, 0), M(2026, 9, 1, 17, 50)]);
        Assert.Equal(0, dias[0].MinutosAtraso);
        Assert.Equal(0, dias[0].MinutosExtras);
    }

    [Fact]
    public void Extras_e_atraso_acima_da_tolerancia()
    {
        var extras = ApuracaoJornada.Apurar(new(2026, 9, 1), new(2026, 9, 1), Sp, Comercial,
            [M(2026, 9, 1, 8, 0), M(2026, 9, 1, 12, 0), M(2026, 9, 1, 13, 0), M(2026, 9, 1, 19, 0)]);
        Assert.Equal(72, extras[0].MinutosExtras);

        var atraso = ApuracaoJornada.Apurar(new(2026, 9, 1), new(2026, 9, 1), Sp, Comercial,
            [M(2026, 9, 1, 9, 0), M(2026, 9, 1, 12, 0), M(2026, 9, 1, 13, 0), M(2026, 9, 1, 17, 48)]);
        Assert.Equal(60, atraso[0].MinutosAtraso);
    }

    [Fact]
    public void Falta_folga_e_domingo()
    {
        var dias = ApuracaoJornada.Apurar(new(2026, 9, 5), new(2026, 9, 7), Sp, Comercial, []);
        Assert.Equal("Folga", dias[0].Ocorrencia); // sábado
        Assert.Equal("Folga", dias[1].Ocorrencia); // domingo
        Assert.Equal(528, dias[2].MinutosFalta);   // segunda
    }

    [Fact]
    public void Marcacoes_impares_sao_inconsistentes()
    {
        var dias = ApuracaoJornada.Apurar(new(2026, 9, 1), new(2026, 9, 1), Sp, Comercial,
            [M(2026, 9, 1, 8, 0), M(2026, 9, 1, 12, 0), M(2026, 9, 1, 13, 0)]);
        Assert.True(dias[0].Inconsistente);
    }

    [Fact]
    public void Desconsiderada_nao_conta_e_incluida_conta()
    {
        var dias = ApuracaoJornada.Apurar(new(2026, 9, 1), new(2026, 9, 1), Sp, Comercial,
        [
            M(2026, 9, 1, 8, 0), M(2026, 9, 1, 8, 1, desc: true), M(2026, 9, 1, 12, 0),
            M(2026, 9, 1, 13, 0, f: FonteMarcacao.Incluida), M(2026, 9, 1, 17, 48)
        ]);
        Assert.Equal(4, dias[0].Validas.Count);
        Assert.Equal(5, dias[0].Marcacoes.Count);
        Assert.Equal(528, dias[0].MinutosTrabalhados);
    }

    [Fact]
    public void Jornada_que_atravessa_meia_noite_pertence_ao_dia_de_inicio()
    {
        // FAQ MTE nº 58: 19h do dia X até 02h do dia X+1, intervalo 23:30-00:30.
        var dias = ApuracaoJornada.Apurar(new(2026, 9, 1), new(2026, 9, 2), Sp, (Jornada?)null,
            [M(2026, 9, 1, 19, 0), M(2026, 9, 1, 23, 30), M(2026, 9, 2, 0, 30), M(2026, 9, 2, 2, 0)]);
        Assert.Equal(4, dias[0].Validas.Count);
        Assert.Empty(dias[1].Validas);
    }

    [Fact]
    public void Hora_noturna_reduzida()
    {
        // 22:00 às 05:00 = 420 min reais = 480 min fictos (8 horas).
        var dias = ApuracaoJornada.Apurar(new(2026, 9, 1), new(2026, 9, 1), Sp, (Jornada?)null,
            [M(2026, 9, 1, 22, 0), M(2026, 9, 2, 5, 0)]);
        Assert.Equal(480, dias[0].MinutosTrabalhados);
        Assert.Equal(420, dias[0].MinutosNoturnos);
    }

    [Fact]
    public void Escala_12x36_alterna_dias()
    {
        var j = CadastroService.Modelo(TipoJornada.Escala12x36, "E1236", "12x36");
        Assert.NotNull(ApuracaoJornada.HorarioDoDia(j, new DateOnly(2026, 1, 1)));
        Assert.Null(ApuracaoJornada.HorarioDoDia(j, new DateOnly(2026, 1, 2)));
        Assert.NotNull(ApuracaoJornada.HorarioDoDia(j, new DateOnly(2026, 1, 3)));
        Assert.NotNull(ApuracaoJornada.HorarioDoDia(j, new DateOnly(2025, 12, 30)));
    }

    [Fact]
    public void Feriado_trabalhado_vira_extra()
    {
        var p = new ParametrosApuracao { Feriados = new HashSet<DateOnly> { new(2026, 9, 7) } };
        var dias = ApuracaoJornada.Apurar(new(2026, 9, 7), new(2026, 9, 7), Sp, Comercial,
            [M(2026, 9, 7, 8, 0), M(2026, 9, 7, 12, 0)], p);
        Assert.Equal("Feriado trabalhado", dias[0].Ocorrencia);
        Assert.Equal(240, dias[0].MinutosExtras);
        Assert.Equal(0, dias[0].MinutosFalta);
    }
}

public class ApuracaoRegressaoTests
{
    private static readonly TimeZoneInfo Sp = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private static MarcacaoApurada M(int dia, int h, int min) =>
        new(new DateTimeOffset(2026, 9, dia, h, min, 0, TimeSpan.FromHours(-3)), FonteMarcacao.Original, false, null, 1, Guid.NewGuid(), null);

    [Fact]
    public void Esquecimento_no_meio_do_dia_nao_emenda_o_dia_seguinte()
    {
        var j = CadastroService.Modelo(TipoJornada.Semanal5x2, "COM", "Comercial");
        // Terça esqueceu a volta do almoço (3 marcações); quarta normal.
        var dias = ApuracaoJornada.Apurar(new(2026, 9, 1), new(2026, 9, 2), Sp, j,
            [M(1, 8, 0), M(1, 12, 0), M(1, 17, 48), M(2, 8, 0), M(2, 12, 0), M(2, 13, 0), M(2, 17, 48)]);
        Assert.Equal(3, dias[0].Validas.Count);
        Assert.True(dias[0].Inconsistente);
        Assert.Equal(4, dias[1].Validas.Count);
        Assert.Equal(528, dias[1].MinutosTrabalhados);
        Assert.Equal(0, dias[1].MinutosExtras);
    }

    [Fact]
    public void Dia_corrente_sem_marcacao_nao_e_falta()
    {
        var j = CadastroService.Modelo(TipoJornada.Semanal5x2, "COM", "Comercial");
        var dias = ApuracaoJornada.Apurar(new(2026, 9, 1), new(2026, 9, 3), Sp, j, [], new ParametrosApuracao { Hoje = new DateOnly(2026, 9, 2) });
        Assert.Equal(528, dias[0].MinutosFalta);
        Assert.Equal(0, dias[1].MinutosFalta);
        Assert.Equal("Jornada não iniciada", dias[1].Ocorrencia);
        Assert.Equal(0, dias[2].MinutosFalta);
    }
}
