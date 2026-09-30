using PersonaliPonto.Modules.SaaS.Domain;
using PersonaliPonto.Modules.SaaS.Services;

namespace PersonaliPonto.Core.Tests;

/// <summary>Etapa E2 — cálculo do uso Owner → revendedor (faixas marginais, mínimo), sem pró-rata e régua de canal.</summary>
public sealed class FaturamentoCanalTests
{
    private static readonly TabelaPrecoCanal Tabela = new();

    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "0.30")]
    [InlineData(2000, "600.00")]
    [InlineData(2001, "600.25")]
    [InlineData(5000, "1350.00")]
    [InlineData(5001, "1350.20")]
    [InlineData(10000, "2350.00")]
    public void Faixas_marginais_por_funcionario(int funcionarios, string esperadoTexto)
    {
        var esperado = decimal.Parse(esperadoTexto, System.Globalization.CultureInfo.InvariantCulture);
        var u = CalculadoraUsoCanal.Calcular(Tabela, 0, funcionarios);
        Assert.Equal(esperado, u.ValorFaixa1 + u.ValorFaixa2 + u.ValorFaixa3);
        Assert.Equal(funcionarios, u.QtdFaixa1 + u.QtdFaixa2 + u.QtdFaixa3);
    }

    [Fact]
    public void Bordas_das_faixas_distribuem_quantidades()
    {
        var a = CalculadoraUsoCanal.Calcular(Tabela, 0, 2000);
        Assert.Equal((2000, 0, 0), (a.QtdFaixa1, a.QtdFaixa2, a.QtdFaixa3));
        var b = CalculadoraUsoCanal.Calcular(Tabela, 0, 2001);
        Assert.Equal((2000, 1, 0), (b.QtdFaixa1, b.QtdFaixa2, b.QtdFaixa3));
        var c = CalculadoraUsoCanal.Calcular(Tabela, 0, 5000);
        Assert.Equal((2000, 3000, 0), (c.QtdFaixa1, c.QtdFaixa2, c.QtdFaixa3));
        var d = CalculadoraUsoCanal.Calcular(Tabela, 0, 5001);
        Assert.Equal((2000, 3000, 1), (d.QtdFaixa1, d.QtdFaixa2, d.QtdFaixa3));
    }

    [Fact]
    public void Minimo_mensal_complementa_o_uso()
    {
        var pequeno = CalculadoraUsoCanal.Calcular(Tabela, 1, 10);
        Assert.Equal(15.00m, pequeno.Subtotal);
        Assert.Equal(275.00m, pequeno.AjusteMinimo);
        Assert.Equal(290m, pequeno.Total);

        var zero = CalculadoraUsoCanal.Calcular(Tabela, 0, 0);
        Assert.Equal(290m, zero.Total);

        // Exatamente no mínimo: sem complemento. 20 empresas (240) + 166 funcionários (49,80) = 289,80 → complemento 0,20.
        var quase = CalculadoraUsoCanal.Calcular(Tabela, 20, 166);
        Assert.Equal(0.20m, quase.AjusteMinimo);
        var exato = CalculadoraUsoCanal.Calcular(Tabela, 15, 367); // 180 + 110,10 = 290,10
        Assert.Equal(0m, exato.AjusteMinimo);
        Assert.Equal(290.10m, exato.Total);

        var grande = CalculadoraUsoCanal.Calcular(Tabela, 100, 5001);
        Assert.Equal(1200m + 1350.20m, grande.Total);
        Assert.Equal(0m, grande.AjusteMinimo);
    }

    [Theory]
    [InlineData("2026-03-15", "2026-03-01", false)]
    [InlineData("2026-03-01", "2026-03-01", false)]
    [InlineData("2026-03-31", "2026-04-01", true)]
    [InlineData("2026-12-20", "2027-01-01", true)]
    [InlineData("2026-03-15", "2026-02-01", false)]
    public void Sem_pro_rata_canal_nao_e_cobrado_no_mes_de_entrada(string entrada, string competencia, bool cobravel) =>
        Assert.Equal(cobravel, CalculadoraUsoCanal.Cobravel(DateOnly.Parse(entrada), DateOnly.Parse(competencia)));

    [Theory]
    [InlineData(0, StatusCanal.Ativo)]
    [InlineData(4, StatusCanal.Ativo)]
    [InlineData(5, StatusCanal.Aviso)]
    [InlineData(14, StatusCanal.Aviso)]
    [InlineData(15, StatusCanal.PainelBloqueado)]
    [InlineData(29, StatusCanal.PainelBloqueado)]
    [InlineData(30, StatusCanal.Suspenso)]
    [InlineData(90, StatusCanal.Suspenso)]
    public void Regua_de_canal_por_dias_de_atraso(int dias, StatusCanal esperado) =>
        Assert.Equal(esperado, FaturamentoCanalService.StatusPorAtraso(dias));

    [Fact]
    public void Parceiro_paga_funcionarios_vezes_preco_com_minimo_opcional()
    {
        Assert.Equal((150m, 0m), CalculadoraUsoCanal.CalcularParceiro(100, 1.50m, null));
        Assert.Equal((15m, 35m), CalculadoraUsoCanal.CalcularParceiro(10, 1.50m, 50m));
        Assert.Equal((0m, 50m), CalculadoraUsoCanal.CalcularParceiro(0, 1.50m, 50m));
    }

    [Fact]
    public void Premium_cobra_mes_cheio_em_que_esteve_ativo()
    {
        var p = new AssinaturaPremium { Inicio = new DateOnly(2026, 5, 20), CanceladaEm = new DateOnly(2026, 7, 3) };
        Assert.False(p.AtivaNaCompetencia(new DateOnly(2026, 4, 1)));
        Assert.True(p.AtivaNaCompetencia(new DateOnly(2026, 5, 1)));
        Assert.True(p.AtivaNaCompetencia(new DateOnly(2026, 7, 1)));
        Assert.False(p.AtivaNaCompetencia(new DateOnly(2026, 8, 1)));
        Assert.True(p.AtivaEm(new DateOnly(2026, 7, 2)));
        Assert.False(p.AtivaEm(new DateOnly(2026, 7, 3)));
    }

    [Fact]
    public void Vencimento_e_dia_10_do_mes_seguinte() =>
        Assert.Equal(new DateOnly(2026, 10, 10), FaturamentoCanalService.VencimentoDe(new DateOnly(2026, 9, 1)));
}
