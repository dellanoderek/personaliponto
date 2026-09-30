using System.Net;

namespace PersonaliPonto.Web.Tests;

/// <summary>Etapa E1 — painéis de revendedor/parceiro, gestão de revendedores pelo Owner e modo suporte por nível.</summary>
[Collection("web")]
public sealed class CanalWebTests(WebFixture fx)
{
    private async Task<HttpClient> EntrarAsync(string email, string destinoEsperado)
    {
        var c = fx.Cliente();
        Assert.Equal(destinoEsperado, await WebFixture.EntrarAsync(c, email, WebFixture.SenhaTeste));
        return c;
    }

    private static async Task<string> PaginaAsync(HttpClient c, string url)
    {
        var r = await c.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var html = WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());
        Assert.DoesNotContain("Algo deu errado", html);
        return html;
    }

    private static async Task NegadoAsync(HttpClient c, string url)
    {
        var r = await c.GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.StartsWith("/acesso-negado", r.Headers.Location!.PathAndQuery());
    }

    public static readonly TheoryData<string, string> PaginasRevendedor = new()
    {
        { "/canal", "Painel Tempo Certo Web" },
        { "/canal/clientes", WebFixture.ClienteParceiro },
        { "/canal/parceiros", WebFixture.MarcaParceiro },
        { "/canal/usuarios", WebFixture.EmailRevendedor }
    };

    [Theory]
    [MemberData(nameof(PaginasRevendedor))]
    public async Task Paginas_do_revendedor_renderizam(string url, string texto)
    {
        var c = await EntrarAsync(WebFixture.EmailRevendedor, "/canal");
        Assert.Contains(texto, await PaginaAsync(c, url));
    }

    [Fact]
    public async Task Revendedor_ve_carteira_propria_e_dos_parceiros_mas_nao_clientes_do_Owner()
    {
        var c = await EntrarAsync(WebFixture.EmailRevendedor, "/canal");
        var html = await PaginaAsync(c, "/canal/clientes");
        Assert.Contains(WebFixture.ClienteRevendedor, html);
        Assert.Contains(WebFixture.ClienteParceiro, html);
        Assert.DoesNotContain("EMPRESA TESTE WEB LTDA", html);
        // Fornecedor padrão = marca do canal dono.
        Assert.Contains("TEMPO CERTO WEB", html);
        Assert.Contains("INFO WEB", html);
    }

    [Fact]
    public async Task Parceiro_ve_so_a_propria_carteira_e_nao_acessa_outras_areas()
    {
        var c = await EntrarAsync(WebFixture.EmailParceiro, "/canal");
        Assert.Contains("Painel Info Web", await PaginaAsync(c, "/canal"));
        var html = await PaginaAsync(c, "/canal/clientes");
        Assert.Contains(WebFixture.ClienteParceiro, html);
        Assert.DoesNotContain(WebFixture.ClienteRevendedor, html);
        Assert.DoesNotContain("EMPRESA TESTE WEB LTDA", html);
        Assert.Contains(WebFixture.EmailParceiro, await PaginaAsync(c, "/canal/usuarios"));
        await NegadoAsync(c, "/canal/parceiros");
        await NegadoAsync(c, "/plataforma");
        await NegadoAsync(c, "/plataforma/revendedores");
        await NegadoAsync(c, "/funcionarios");
    }

    [Fact]
    public async Task Usuarios_de_cliente_e_plataforma_nao_acessam_paginas_de_canal()
    {
        var rh = await EntrarAsync(WebFixture.EmailRh, "/");
        await NegadoAsync(rh, "/canal");
        await NegadoAsync(rh, "/canal/clientes");
        var owner = await EntrarAsync(WebFixture.EmailAdmin, "/plataforma");
        await NegadoAsync(owner, "/canal");
    }

    [Fact]
    public async Task Owner_gerencia_revendedores()
    {
        var c = await EntrarAsync(WebFixture.EmailAdmin, "/plataforma");
        var html = await PaginaAsync(c, "/plataforma/revendedores");
        Assert.Contains("Revendedores", html);
        Assert.Contains(WebFixture.MarcaRevendedor, html);
        Assert.DoesNotContain(WebFixture.MarcaParceiro, html);
    }

    [Fact]
    public async Task Revendedor_entra_em_modo_suporte_so_em_cliente_da_carteira()
    {
        var c = await EntrarAsync(WebFixture.EmailRevendedor, "/canal");
        var token = await WebFixture.TokenAsync(c, "/canal/clientes");

        var fora = await c.PostAsync("/canal/suporte/entrar", Form(token, ("tenantId", fx.TenantId.ToString()), ("motivo", "Tentativa fora da carteira")));
        Assert.Equal(HttpStatusCode.NotFound, fora.StatusCode);

        var ok = await c.PostAsync("/canal/suporte/entrar", Form(token, ("tenantId", fx.TenantParceiro.ToString()), ("motivo", "Chamado 77 - dúvida de jornada")));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        Assert.Equal("/", ok.Headers.Location!.ToString());
        var html = await PaginaAsync(c, "/funcionarios");
        Assert.Contains("Modo suporte", html);
        Assert.Contains(WebFixture.ClienteParceiro, html);

        var token2 = await WebFixture.TokenAsync(c, "/funcionarios");
        var sair = await c.PostAsync("/suporte/sair", Form(token2));
        Assert.Equal("/canal", sair.Headers.Location!.ToString());
        await NegadoAsync(c, "/funcionarios");

        // O acesso fica registrado e visível para a plataforma.
        var owner = await EntrarAsync(WebFixture.EmailAdmin, "/plataforma");
        Assert.Contains("Chamado 77 - dúvida de jornada", await PaginaAsync(owner, "/plataforma/acessos"));
    }

    [Fact]
    public async Task Owner_entra_no_painel_do_revendedor_em_modo_suporte()
    {
        var c = await EntrarAsync(WebFixture.EmailAdmin, "/plataforma");
        var token = await WebFixture.TokenAsync(c, "/plataforma/revendedores");
        var r = await c.PostAsync("/plataforma/suporte/canal/entrar", Form(token, ("canalId", fx.RevendedorId.ToString()), ("motivo", "Chamado 90 - configuração de parceiro")));
        Assert.Equal("/canal", r.Headers.Location!.ToString());
        var html = await PaginaAsync(c, "/canal/clientes");
        Assert.Contains(WebFixture.ClienteParceiro, html);
        Assert.DoesNotContain("EMPRESA TESTE WEB LTDA", html);
        Assert.Contains("Modo suporte", await PaginaAsync(c, "/canal"));
        var sair = await c.PostAsync("/suporte/sair", Form(await WebFixture.TokenAsync(c, "/canal")));
        Assert.Equal("/plataforma", sair.Headers.Location!.ToString());
    }

    private static FormUrlEncodedContent Form(string token, params (string Chave, string Valor)[] campos)
    {
        var d = new Dictionary<string, string> { ["__RequestVerificationToken"] = token };
        foreach (var (k, v) in campos) d[k] = v;
        return new FormUrlEncodedContent(d);
    }
}
