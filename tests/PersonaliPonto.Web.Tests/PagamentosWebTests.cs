using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonaliPonto.Infrastructure.Pagamentos;
using PersonaliPonto.Infrastructure.Persistence;
using PersonaliPonto.Infrastructure.Tenancy;
using PersonaliPonto.Modules.SaaS.Domain;

namespace PersonaliPonto.Web.Tests;

/// <summary>E3/E9 — webhook público do Asaas (autenticação, idempotência, limite de taxa) e telas de pagamento.</summary>
[Collection("web")]
public sealed class PagamentosWebTests(WebFixture fx)
{
    private async Task<HttpClient> EntrarAsync(string email, string destino)
    {
        var c = fx.Cliente();
        Assert.Equal(destino, await WebFixture.EntrarAsync(c, email, WebFixture.SenhaTeste));
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

    private static HttpRequestMessage Evento(string? token, string corpo)
    {
        var m = new HttpRequestMessage(HttpMethod.Post, AsaasOptions.CaminhoWebhook) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };
        if (token is not null) m.Headers.Add("asaas-access-token", token);
        return m;
    }

    [Fact]
    public async Task Webhook_exige_authToken_e_idempotente_e_limita_taxa()
    {
        var c = fx.Cliente();
        var evt = "evt_web_" + Guid.NewGuid().ToString("N");
        var corpo = $$$"""{"id":"{{{evt}}}","event":"PAYMENT_CREATED","dateCreated":"2026-09-30 10:00:00","payment":{"id":"pay_desconhecido","value":10}}""";

        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Evento(null, corpo))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Evento("token-invalido-0000000000000000000000000", corpo))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(Evento(WebFixture.TokenWebhookOwner, "não é json"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Evento(WebFixture.TokenWebhookOwner, corpo))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Evento(WebFixture.TokenWebhookOwner, corpo))).StatusCode); // repetido

        using (var scope = fx.Factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<RequestContext>().DefinirSistema();
            var db = scope.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
            var ev = await db.EventosGateway.SingleAsync(e => e.EventoId == evt);
            Assert.Equal("ignorado: cobrança desconhecida", ev.Resultado);
        }

        // Limite de taxa por IP (30/min no teste).
        var codigos = new List<HttpStatusCode>();
        for (var i = 0; i < 35; i++) codigos.Add((await c.SendAsync(Evento(null, "{}"))).StatusCode);
        Assert.True(codigos.Contains(HttpStatusCode.TooManyRequests), string.Join(",", codigos));
    }

    [Fact]
    public async Task Revendedor_ve_pagamento_pix_boleto_cartao_e_cartao_da_conta_Asaas()
    {
        using (var scope = fx.Factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<RequestContext>().DefinirSistema();
            var db = scope.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
            if (!await db.FaturasCanal.AnyAsync(f => f.CanalPagadorId == fx.RevendedorId && f.GatewayCobrancaId == "pay_web_1"))
            {
                var f = new FaturaCanal
                {
                    Tipo = TipoFaturaCanal.Avulsa, CanalPagadorId = fx.RevendedorId, CanalRecebedorId = Canal.OwnerRaizId,
                    Competencia = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1), Valor = 149m, Vencimento = DateOnly.FromDateTime(DateTime.Today.AddDays(3)),
                    Status = StatusFatura.Aberta, CriadaEm = DateTimeOffset.UtcNow, GatewayStatus = StatusCobrancaGateway.Emitida, GatewayCobrancaId = "pay_web_1",
                    GatewayLink = "https://sandbox.asaas.com/i/pay_web_1", GatewayBoletoUrl = "https://sandbox.asaas.com/b/pay_web_1",
                    GatewayLinhaDigitavel = "34191.79001 01043.510047", GatewayPixCopiaCola = "00020101021226PIXWEB", GatewayPixQrCode = "iVBORw0KGgo="
                };
                f.Itens.Add(new ItemFaturaCanal { FaturaCanalId = f.Id, Tipo = TipoItemFaturaCanal.Premium, Descricao = "Assinatura Premium", Quantidade = 1, ValorUnitario = 149m, Valor = 149m, Ordem = 1 });
                db.FaturasCanal.Add(f);
                await db.SaveChangesAsync();
            }
        }

        var c = await EntrarAsync(WebFixture.EmailRevendedor, "/canal");
        var html = await PaginaAsync(c, "/canal/minha-conta");
        Assert.Contains("Pix copia e cola", html);
        Assert.Contains("00020101021226PIXWEB", html);
        Assert.Contains("data:image/png;base64,iVBORw0KGgo=", html);
        Assert.Contains("34191.79001 01043.510047", html);
        Assert.Contains("https://sandbox.asaas.com/i/pay_web_1", html);
        Assert.Contains("Compra no painel", html);
        Assert.Contains("Cobrança automática dos seus clientes (Asaas)", html);
        Assert.Contains("Recurso Premium", html); // sem Premium não conecta

        var parceiro = await EntrarAsync(WebFixture.EmailParceiro, "/canal");
        var hp = await PaginaAsync(parceiro, "/canal/minha-conta");
        Assert.DoesNotContain("PIXWEB", hp);
        Assert.DoesNotContain("Cobrança automática dos seus clientes", hp);
    }

    [Fact]
    public async Task Plataforma_mostra_situacao_do_Asaas_da_plataforma()
    {
        var c = await EntrarAsync(WebFixture.EmailAdmin, "/plataforma");
        var html = await PaginaAsync(c, "/plataforma/faturamento-canais");
        Assert.Contains("Asaas da plataforma não configurado", html);
    }
}
