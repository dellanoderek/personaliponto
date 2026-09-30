using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PersonaliPonto.Infrastructure.Pagamentos;
using PersonaliPonto.Infrastructure.Persistence;

namespace PersonaliPonto.Core.Tests;

/// <summary>E3/E9 — cliente HTTP do Asaas (sem rede), cofre de chaves e regras puras do webhook.</summary>
public sealed class AsaasTests
{
    private sealed class HandlerFake(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Req, string? Corpo)> Chamadas { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Chamadas.Add((request, request.Content is null ? null : await request.Content.ReadAsStringAsync(ct)));
            return responder(request);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (AsaasGateway Gw, HandlerFake H) Criar(Func<HttpRequestMessage, HttpResponseMessage> r)
    {
        var h = new HandlerFake(r);
        return (new AsaasGateway(new HttpClient(h), Options.Create(new AsaasOptions { UserAgent = "PersonaliPonto-Teste/1.0" }), NullLogger<AsaasGateway>.Instance), h);
    }

    private const string Chave = "$aact_hmlg_000chave-secreta-de-teste-9876";

    [Fact]
    public async Task Cria_cobranca_no_sandbox_com_access_token_user_agent_e_billingType_indefinido()
    {
        var (gw, h) = Criar(_ => Json("""{"id":"pay_123","status":"PENDING","invoiceUrl":"https://sandbox.asaas.com/i/123","bankSlipUrl":"https://sandbox.asaas.com/b/123"}"""));
        var c = await gw.CriarCobrancaAsync(new CredencialGateway(Chave, true),
            new NovaCobranca("cus_1", 149.9m, new DateOnly(2026, 10, 10), "Premium", "canal:abc"), default);

        Assert.Equal("pay_123", c.Id);
        Assert.Equal("https://sandbox.asaas.com/i/123", c.LinkFatura);
        var (req, corpo) = h.Chamadas.Single();
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("https://api-sandbox.asaas.com/v3/payments", req.RequestUri!.ToString());
        Assert.Equal(Chave, req.Headers.GetValues("access_token").Single());
        Assert.Contains("PersonaliPonto-Teste", req.Headers.UserAgent.ToString());
        var j = JsonNode.Parse(corpo!)!;
        Assert.Equal("UNDEFINED", j["billingType"]!.GetValue<string>());
        Assert.Equal("2026-10-10", j["dueDate"]!.GetValue<string>());
        Assert.Equal(149.9m, j["value"]!.GetValue<decimal>());
        Assert.Equal("canal:abc", j["externalReference"]!.GetValue<string>());
    }

    [Fact]
    public async Task Producao_usa_api_asaas_com_e_pix_e_linha_digitavel_sao_lidos()
    {
        var (gw, h) = Criar(r => r.RequestUri!.AbsolutePath.EndsWith("/pixQrCode")
            ? Json("""{"encodedImage":"iVBOR","payload":"00020126PIX","expirationDate":"2026-10-10 23:59:59"}""")
            : Json("""{"identificationField":"34191.79001 01043","nossoNumero":"1","barCode":"341"}"""));
        var cred = new CredencialGateway(Chave, false);
        var pix = await gw.ObterPixAsync(cred, "pay_9", default);
        var linha = await gw.ObterLinhaDigitavelAsync(cred, "pay_9", default);
        Assert.Equal("00020126PIX", pix!.CopiaECola);
        Assert.Equal("iVBOR", pix.QrCodeBase64);
        Assert.Equal("34191.79001 01043", linha);
        Assert.All(h.Chamadas, c => Assert.StartsWith("https://api.asaas.com/v3/payments/pay_9/", c.Req.RequestUri!.ToString()));
    }

    [Fact]
    public async Task Cliente_existente_pela_referencia_externa_e_atualizado_senao_criado()
    {
        var (gw, h) = Criar(r => r.Method == HttpMethod.Get
            ? Json(r.RequestUri!.Query.Contains("externalReference") ? """{"data":[{"id":"cus_77"}]}""" : """{"data":[]}""")
            : Json("""{"id":"cus_77"}"""));
        var id = await gw.GarantirClienteAsync(new CredencialGateway(Chave, true), new ClienteGateway("Rev LTDA", "12345678000195", null, null, "canal:1"), default);
        Assert.Equal("cus_77", id);
        Assert.Equal(HttpMethod.Put, h.Chamadas.Last().Req.Method);
        Assert.EndsWith("/customers/cus_77", h.Chamadas.Last().Req.RequestUri!.AbsolutePath);

        var (gw2, h2) = Criar(r => r.Method == HttpMethod.Get ? Json("""{"data":[]}""") : Json("""{"id":"cus_new"}"""));
        Assert.Equal("cus_new", await gw2.GarantirClienteAsync(new CredencialGateway(Chave, true), new ClienteGateway("X", "1", null, null, "cliente:2"), default));
        Assert.Equal(HttpMethod.Post, h2.Chamadas.Last().Req.Method);
    }

    [Fact]
    public async Task Webhook_criado_com_authToken_eventos_e_envio_sequencial()
    {
        var (gw, h) = Criar(_ => Json("""{"id":"wh_1"}"""));
        var id = await gw.CriarWebhookAsync(new CredencialGateway(Chave, true),
            new NovoWebhook("PersonaliPonto", "https://painel/api/pagamentos/asaas/webhook", "a@b.c", "tok_" + new string('x', 40), ContaGatewayService.EventosWebhook), default);
        Assert.Equal("wh_1", id);
        var j = JsonNode.Parse(h.Chamadas.Single().Corpo!)!;
        Assert.Equal("SEQUENTIALLY", j["sendType"]!.GetValue<string>());
        Assert.Equal(3, j["apiVersion"]!.GetValue<int>());
        Assert.True(j["enabled"]!.GetValue<bool>());
        Assert.Contains("PAYMENT_REFUNDED", j["events"]!.AsArray().Select(e => e!.GetValue<string>()));
    }

    [Fact]
    public async Task Chave_recusada_vira_erro_sem_vazar_a_chave()
    {
        var (gw, _) = Criar(_ => Json("""{"errors":[{"code":"invalid_access_token","description":"A chave de API fornecida é inválida"}]}""", HttpStatusCode.Unauthorized));
        var ex = await Assert.ThrowsAsync<GatewayPagamentoException>(() => gw.ObterContaAsync(new CredencialGateway(Chave, true), default));
        Assert.True(ex.ChaveInvalida);
        Assert.DoesNotContain(Chave, ex.Message);
        Assert.DoesNotContain(Chave, new CredencialGateway(Chave, true).ToString());

        var (gw2, _) = Criar(_ => Json("""{"errors":[{"code":"invalid_value","description":"Valor inválido"}]}""", HttpStatusCode.BadRequest));
        var ex2 = await Assert.ThrowsAsync<GatewayPagamentoException>(() => gw2.CriarCobrancaAsync(new CredencialGateway(Chave, true),
            new NovaCobranca("c", 0, new DateOnly(2026, 1, 1), "x", "r"), default));
        Assert.Contains("Valor inválido", ex2.Message);
        Assert.False(ex2.ChaveInvalida);
    }

    [Fact]
    public void Cofre_cifra_com_proposito_dedicado_e_token_tem_formato_aceito_pelo_Asaas()
    {
        var dp = new EphemeralDataProtectionProvider();
        var cofre = new CofreChavesGateway(dp);
        var cifrada = cofre.Cifrar(Chave);
        Assert.DoesNotContain(Chave, cifrada);
        Assert.Equal(Chave, cofre.Decifrar(cifrada));
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => dp.CreateProtector("outro").Unprotect(cifrada));

        var t1 = CofreChavesGateway.NovoToken();
        Assert.InRange(t1.Length, 32, 255);
        Assert.DoesNotContain(t1, char.IsWhiteSpace);
        Assert.NotEqual(t1, CofreChavesGateway.NovoToken());
        Assert.Equal(64, CofreChavesGateway.Hash(t1).Length);
        Assert.Equal(CofreChavesGateway.Hash(t1), CofreChavesGateway.Hash(t1));
    }

    [Fact]
    public void Datas_do_webhook_sao_horario_de_Brasilia()
    {
        var d = WebhookAsaasService.Data("2026-09-30 10:00:00")!.Value;
        Assert.Equal(TimeSpan.FromHours(-3), d.Offset);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 13, 0, 0, TimeSpan.Zero), d.ToUniversalTime());
        Assert.Null(WebhookAsaasService.Data(null));
        Assert.True(WebhookAsaasService.Data("2026-09-30 10:00:01") > d);
    }

    [Fact]
    public void RLS_v4_inclui_as_tabelas_de_pagamento_e_protege_os_campos_do_gateway()
    {
        var sql = SegurancaBanco.FuncoesPagamentos;
        Assert.NotEqual(SegurancaBanco.FuncoesCanalV3, sql);
        Assert.Contains("t.tabela = 'contas_gateway'", sql);
        Assert.Contains("t.tabela = 'eventos_gateway'", sql);
        Assert.Contains("NEW.gateway_pix_copia_cola IS DISTINCT FROM OLD.gateway_pix_copia_cola", sql);
    }

    [Fact]
    public void Ambiente_padrao_e_sandbox()
    {
        Assert.True(new AsaasOptions().Sandbox);
        Assert.False(new AsaasOptions { Ambiente = "Producao" }.Sandbox);
        Assert.Equal("https://api.asaas.com/v3/", AsaasOptions.UrlBase(false));
        Assert.Equal("https://api-sandbox.asaas.com/v3/", AsaasOptions.UrlBase(true));
    }
}
