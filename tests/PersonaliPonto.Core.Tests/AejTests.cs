using PersonaliPonto.Core.RepP.Aej;
using PersonaliPonto.Core.RepP.Apuracao;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Formatacao;

namespace PersonaliPonto.Core.Tests;

public class AejTests
{
    private static readonly TimeZoneInfo Sp = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    [Fact]
    public void Exemplo_do_FAQ_MTE_51_registros_tipo_05()
    {
        var j = new Jornada
        {
            Codigo = "CH001", Nome = "Comercial",
            Horarios = Enumerable.Range(0, 7).Select(i => new HorarioDia
            {
                Indice = i,
                Periodos = [new() { Entrada = new(7, 0), Saida = new(11, 30) }, new() { Entrada = new(13, 30), Saida = new(17, 30) }]
            }).ToList()
        };
        MarcacaoApurada M(int d, int h, int m) => new(new DateTimeOffset(2022, 1, d, h, m, 0, TimeSpan.FromHours(-3)), FonteMarcacao.Original, false, null, 1, Guid.NewGuid(), null);
        var dias = ApuracaoJornada.Apurar(new(2022, 1, 1), new(2022, 1, 2), Sp, j,
            [M(1, 7, 0), M(1, 11, 30), M(1, 13, 32), M(1, 17, 25), M(2, 7, 2), M(2, 11, 28), M(2, 13, 35), M(2, 17, 45)]);

        var arq = AejGenerator.Gerar(
            new AejCabecalho(TipoIdentificador.Cnpj, "11222333000181", null, null, "EMPRESA", new(2022, 1, 1), new(2022, 1, 2),
                new DateTimeOffset(2022, 1, 3, 12, 0, 0, TimeSpan.Zero), -180, "BR 51 2026 000123-4"),
            new AejPtrp("PersonaliPonto", "1.0.0", TipoIdentificador.Cnpj, "12ABC34501DE35", "DESENVOLVEDOR", "dev@exemplo.com"),
            [
                new AejVinculo("00000000191", "OUTRO", null, j, [], [], Sp),
                new AejVinculo("52998224725", "FULANO", null, j, dias, [], Sp)
            ]);

        var linhas = AfdCampos.Latin1.GetString(arq.Conteudo).Split("\r\n");
        var tipo05 = linhas.Where(l => l.StartsWith("05|")).ToList();
        var codigo = linhas.First(l => l.StartsWith("04|")).Split('|')[1];
        Assert.Equal(
        [
            $"05|2|2022-01-01T07:00:00-0300|1|E|1|O|{codigo}|",
            "05|2|2022-01-01T11:30:00-0300|1|S|1|O||",
            "05|2|2022-01-01T13:32:00-0300|1|E|2|O||",
            "05|2|2022-01-01T17:25:00-0300|1|S|2|O||",
            $"05|2|2022-01-02T07:02:00-0300|1|E|1|O|{codigo}|",
            "05|2|2022-01-02T11:28:00-0300|1|S|1|O||",
            "05|2|2022-01-02T13:35:00-0300|1|E|2|O||",
            "05|2|2022-01-02T17:45:00-0300|1|S|2|O||"
        ], tipo05);

        Assert.StartsWith("01|1|11222333000181|||EMPRESA|2022-01-01|2022-01-02|2022-01-03T09:00:00-0300|002", linhas[0]);
        Assert.Equal("02|1|3|5120260001234", linhas[1]);
        Assert.Contains("04|" + codigo + "|510|0700|1130|1330|1730", linhas);
        Assert.Equal("99|1|1|2|1|8|0|0|1", linhas[^3]);
        Assert.Equal("ASSINATURA_DIGITAL_EM_ARQUIVO_P7S".PadRight(100), linhas[^2]);
        Assert.Equal("", linhas[^1]);
    }

    [Fact]
    public void Desconsiderada_e_incluida_exigem_motivo()
    {
        MarcacaoApurada M(int h, bool desc = false, FonteMarcacao f = FonteMarcacao.Original) =>
            new(new DateTimeOffset(2026, 9, 1, h, 0, 0, TimeSpan.FromHours(-3)), f, desc, desc || f == FonteMarcacao.Incluida ? "Esqueceu | de marcar" : null, 1, Guid.NewGuid(), null);
        var dias = ApuracaoJornada.Apurar(new(2026, 9, 1), new(2026, 9, 1), Sp, (Jornada?)null,
            [M(8), M(9, desc: true), M(12, f: FonteMarcacao.Incluida)]);
        var arq = AejGenerator.Gerar(
            new AejCabecalho(TipoIdentificador.Cnpj, "11222333000181", null, null, "EMPRESA", new(2026, 9, 1), new(2026, 9, 1), DateTimeOffset.UtcNow, -180, "1"),
            new AejPtrp("PersonaliPonto", "1.0.0", TipoIdentificador.Cnpj, "12ABC34501DE35", "DEV", "dev@exemplo.com"),
            [new AejVinculo("52998224725", "FULANO", null, null, dias, [], Sp)]);
        var linhas = AfdCampos.Latin1.GetString(arq.Conteudo).Split("\r\n");
        Assert.Contains("05|1|2026-09-01T12:00:00-0300||S|1|I||Esqueceu / de marcar", linhas);
        Assert.Contains("05|1|2026-09-01T09:00:00-0300|1|D|0|O||Esqueceu / de marcar", linhas);
    }
}
