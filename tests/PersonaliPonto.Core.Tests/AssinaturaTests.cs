using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PersonaliPonto.Infrastructure.Seguranca;

namespace PersonaliPonto.Core.Tests;

public class AssinaturaTests
{
    private static AssinaturaDigital Dev() => new(Options.Create(new AssinaturaOptions()), NullLogger<AssinaturaDigital>.Instance);

    [Fact]
    public void Certificado_de_desenvolvimento_nao_e_reconhecido_como_ICP_Brasil()
    {
        using var a = Dev();
        Assert.False(a.CertificadoIcpBrasil);
    }

    [Fact]
    public void Cades_destacado_verifica_e_detecta_alteracao()
    {
        using var a = Dev();
        var conteudo = System.Text.Encoding.Latin1.GetBytes("000000000100000000000000000\r\n");
        var p7s = a.AssinarCadesDestacado(conteudo);
        Assert.True(AssinaturaDigital.VerificarCades(conteudo, p7s));
        conteudo[5] = (byte)'9';
        Assert.False(AssinaturaDigital.VerificarCades(conteudo, p7s));
    }
}
