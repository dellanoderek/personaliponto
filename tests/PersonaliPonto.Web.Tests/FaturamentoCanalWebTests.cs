using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Infrastructure.Persistence;
using PersonaliPonto.Infrastructure.Tenancy;
using PersonaliPonto.Modules.SaaS.Domain;
using PersonaliPonto.Modules.SaaS.Services;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Web.Tests;

/// <summary>Etapa E2 — telas de faturamento de canais (plataforma, revendedor, parceiro) e efeitos da régua no painel.</summary>
[Collection("web")]
public sealed class FaturamentoCanalWebTests(WebFixture fx)
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

    [Fact]
    public async Task Owner_ve_faturamento_de_canais()
    {
        var c = await EntrarAsync(WebFixture.EmailAdmin, "/plataforma");
        var html = await PaginaAsync(c, "/plataforma/faturamento-canais");
        Assert.Contains("Faturamento de canais", html);
        Assert.Contains(WebFixture.MarcaRevendedor, html);
        Assert.Contains("Tabela de preço", html);
        Assert.DoesNotContain(WebFixture.MarcaParceiro, html);
    }

    [Fact]
    public async Task Revendedor_ve_minha_conta_com_uso_estimado_premium_e_precos_de_parceiros()
    {
        var c = await EntrarAsync(WebFixture.EmailRevendedor, "/canal");
        var html = await PaginaAsync(c, "/canal/minha-conta");
        Assert.Contains("Minha conta PersonaliPonto", html);
        Assert.Contains("Uso estimado", html);
        Assert.Contains("Ativar Premium", html);
        Assert.Contains("290,00", html); // mínimo mensal aplicado à estimativa
        var parceiros = await PaginaAsync(c, "/canal/parceiros");
        Assert.Contains("Faturas dos parceiros", parceiros);
        Assert.Contains("não cobrado", parceiros);
    }

    [Fact]
    public async Task Parceiro_ve_so_as_proprias_faturas_e_nao_acessa_a_plataforma()
    {
        var c = await EntrarAsync(WebFixture.EmailParceiro, "/canal");
        var html = await PaginaAsync(c, "/canal/minha-conta");
        Assert.Contains("Minhas faturas", html);
        Assert.DoesNotContain("Uso estimado", html);
        Assert.DoesNotContain("Ativar Premium", html);
        var r = await c.GetAsync("/plataforma/faturamento-canais");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.StartsWith("/acesso-negado", r.Headers.Location!.PathAndQuery());
    }

    [Fact]
    public async Task Painel_bloqueado_leva_para_pendencia_e_cliente_de_canal_suspenso_ve_aviso_no_RH()
    {
        const string emailRev = "admin@revenda-bloqueada.local";
        const string emailRh = "rh@cliente-canal-suspenso.local";
        Guid rev;
        using (var scope = fx.Factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var ctx = sp.GetRequiredService<RequestContext>();
            ctx.DefinirSistema();
            ctx.DefinirUsuario(Guid.NewGuid(), "Owner", null, Roles.SuperAdmin, null);
            rev = (await sp.GetRequiredService<CanalService>().CriarAsync(new NovoCanalRequest("REVENDA BLOQUEADA LTDA", "60701190000104", "Revenda Bloqueada",
                null, null, "Admin", emailRev, null), default)).CanalId;
            await sp.GetRequiredService<ClienteService>().CadastrarAsync(new NovoClienteRequest("CLIENTE DE CANAL SUSPENSO LTDA", "07526557000100", null, emailRh,
                "RH", null, 100, 5, 0, null, 10, null, "Rua C, 3", false, CanalDonoId: rev), default);
            var db = sp.GetRequiredService<PersonaliPontoDbContext>();
            foreach (var u in await db.Usuarios.Where(u => u.Email == emailRev || u.Email == emailRh).ToListAsync())
            {
                u.SenhaHash = SecretHasher.Hash(WebFixture.SenhaTeste);
                u.DeveTrocarSenha = false;
            }
            await db.SaveChangesAsync();
            await sp.GetRequiredService<CanalService>().AlterarStatusAsync(rev, StatusCanal.PainelBloqueado, default);
        }

        var c = await EntrarAsync(emailRev, "/canal");
        foreach (var url in new[] { "/canal", "/canal/clientes", "/canal/parceiros" })
        {
            var r = await c.GetAsync(url);
            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
            Assert.Equal("/canal/minha-conta", r.Headers.Location!.PathAndQuery());
        }
        var html = await PaginaAsync(c, "/canal/minha-conta");
        Assert.Contains("Pendência financeira", html);
        Assert.Contains("Painel bloqueado", html);
        Assert.DoesNotContain("Ativar Premium", html);

        // Suspensão: clientes do canal veem o aviso no painel de RH (e seguem operando).
        using (var scope = fx.Factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var ctx = sp.GetRequiredService<RequestContext>();
            ctx.DefinirSistema();
            ctx.DefinirUsuario(Guid.NewGuid(), "Owner", null, Roles.SuperAdmin, null);
            await sp.GetRequiredService<CanalService>().AlterarStatusAsync(rev, StatusCanal.Suspenso, default);
        }
        var rh = await EntrarAsync(emailRh, "/");
        Assert.Contains("Aviso do fornecedor", await PaginaAsync(rh, "/"));
        var rhOutro = await EntrarAsync(WebFixture.EmailRh, "/");
        Assert.DoesNotContain("Aviso do fornecedor", await PaginaAsync(rhOutro, "/"));
    }
}
