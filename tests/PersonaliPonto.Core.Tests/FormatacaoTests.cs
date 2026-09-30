using System.Text;
using PersonaliPonto.Core.RepP.Formatacao;
using PersonaliPonto.Core.RepP.Services;

namespace PersonaliPonto.Core.Tests;

public class FormatacaoTests
{
    [Fact]
    public void Crc16Kermit_exemplo_oficial_MTE()
    {
        // FAQ MTE nº 35: "123456789" gera 0x2189, gravado como "2189".
        Assert.Equal("2189", Crc16Kermit.Hex(Encoding.ASCII.GetBytes("123456789")));
    }

    [Theory]
    [InlineData("529.982.247-25", true)]
    [InlineData("52998224725", true)]
    [InlineData("111.111.111-11", false)]
    [InlineData("529.982.247-24", false)]
    [InlineData("", false)]
    public void Cpf(string cpf, bool valido) => Assert.Equal(valido, Documentos.CpfValido(cpf));

    [Theory]
    [InlineData("11.222.333/0001-81", true)]
    [InlineData("11.222.333/0001-80", false)]
    [InlineData("12.ABC.345/01DE-35", true)] // exemplo oficial da Receita Federal (CNPJ alfanumérico)
    [InlineData("12.ABC.345/01DE-34", false)]
    [InlineData("00.000.000/0000-00", false)]
    public void Cnpj_numerico_e_alfanumerico(string cnpj, bool valido) => Assert.Equal(valido, Documentos.CnpjValido(cnpj));

    [Fact]
    public void DH_formato_oficial_com_fuso()
    {
        var instante = new DateTimeOffset(2021, 4, 27, 19, 44, 37, TimeSpan.Zero);
        Assert.Equal("2021-04-27T16:44:00-0300", AfdCampos.DH(instante, -180));
        Assert.Equal("2021-04-27T15:44:00-0400", AfdCampos.DH(instante, -240));
        Assert.Equal("2021-04-27T19:44:00+0000", AfdCampos.DH(instante, 0));
    }

    [Fact]
    public void Cpf12_zero_a_esquerda() => Assert.Equal("052998224725", AfdCampos.Cpf12("529.982.247-25"));

    [Fact]
    public void Campo_alfanumerico_preenche_com_espaco_e_trunca()
    {
        Assert.Equal("ABC  ", AfdCampos.A("ABC", 5));
        Assert.Equal("ABCDE", AfdCampos.A("ABCDEFG", 5));
        Assert.Equal("Joao?", AfdCampos.A("Joao中", 5));
    }

    [Fact]
    public void Inpi_apenas_numeros_e_espacos_a_direita()
    {
        // FAQ MTE nº 49: "BR 51 2022 000123-4" => "5120220001234" + espaços.
        Assert.Equal("5120220001234    ", AfdCampos.NEsquerda("BR 51 2022 000123-4".Replace("BR", ""), 17));
    }

    [Theory]
    [InlineData("1234", false)]
    [InlineData("0000", false)]
    [InlineData("1234567", false)]
    [InlineData("9876", false)]
    [InlineData("12a4", false)]
    [InlineData("4821", true)]
    public void Pin_aceitavel(string pin, bool ok) => Assert.Equal(ok, SecretHasher.PinAceitavel(pin));

    [Fact]
    public void Hash_de_segredo_verifica_e_nunca_guarda_texto()
    {
        var h = SecretHasher.Hash("4821");
        Assert.DoesNotContain("4821", h);
        Assert.True(SecretHasher.Verificar("4821", h));
        Assert.False(SecretHasher.Verificar("4822", h));
        Assert.NotEqual(h, SecretHasher.Hash("4821"));
    }
}
