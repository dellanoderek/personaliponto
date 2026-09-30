using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PersonaliPonto.Infrastructure.Pagamentos;

/// <summary>
/// Cliente da API v3 do Asaas (docs.asaas.com). Autenticação pelo header <c>access_token</c>; ambiente pela
/// credencial (sandbox: api-sandbox.asaas.com, produção: api.asaas.com). A chave nunca vai para log nem exceção.
/// </summary>
public sealed class AsaasGateway(HttpClient http, IOptions<AsaasOptions> opcoes, ILogger<AsaasGateway> log) : IGatewayPagamento
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ContaGatewayInfo> ObterContaAsync(CredencialGateway c, CancellationToken ct)
    {
        var j = await EnviarAsync(c, HttpMethod.Get, "myAccount/commercialInfo/", null, ct);
        var nome = Str(j, "companyName") ?? Str(j, "name") ?? "Conta Asaas";
        return new ContaGatewayInfo(nome, Str(j, "cpfCnpj"), Str(j, "email"), Str(j, "status"));
    }

    public async Task<string> GarantirClienteAsync(CredencialGateway c, ClienteGateway cli, CancellationToken ct)
    {
        var existente = await PrimeiroAsync(c, $"customers?externalReference={Uri.EscapeDataString(cli.ReferenciaExterna)}&limit=1", ct)
                        ?? await PrimeiroAsync(c, $"customers?cpfCnpj={Uri.EscapeDataString(cli.CpfCnpj)}&limit=1", ct);
        var corpo = new JsonObject
        {
            ["name"] = cli.Nome,
            ["cpfCnpj"] = cli.CpfCnpj,
            ["email"] = cli.Email,
            ["mobilePhone"] = cli.Telefone,
            ["externalReference"] = cli.ReferenciaExterna,
            ["notificationDisabled"] = false
        };
        if (existente is not null && Str(existente, "id") is { } id)
        {
            await EnviarAsync(c, HttpMethod.Put, $"customers/{Uri.EscapeDataString(id)}", corpo, ct);
            return id;
        }
        var novo = await EnviarAsync(c, HttpMethod.Post, "customers", corpo, ct);
        return Str(novo, "id") ?? throw new GatewayPagamentoException("Asaas não devolveu o id do cliente.");
    }

    public async Task<CobrancaGateway?> BuscarCobrancaAsync(CredencialGateway c, string referenciaExterna, CancellationToken ct)
    {
        var j = await PrimeiroAsync(c, $"payments?externalReference={Uri.EscapeDataString(referenciaExterna)}&limit=1", ct);
        return j is null || Str(j, "deleted") == "true" ? null : Cobranca(j);
    }

    public async Task<CobrancaGateway> CriarCobrancaAsync(CredencialGateway c, NovaCobranca n, CancellationToken ct)
    {
        var corpo = new JsonObject
        {
            ["customer"] = n.ClienteId,
            ["billingType"] = n.Forma switch
            {
                FormaCobranca.Pix => "PIX",
                FormaCobranca.Boleto => "BOLETO",
                FormaCobranca.Cartao => "CREDIT_CARD",
                _ => "UNDEFINED" // o pagador escolhe Pix, boleto ou cartão no link da fatura
            },
            ["value"] = n.Valor,
            ["dueDate"] = n.Vencimento.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["description"] = n.Descricao.Length > 500 ? n.Descricao[..500] : n.Descricao,
            ["externalReference"] = n.ReferenciaExterna
        };
        return Cobranca(await EnviarAsync(c, HttpMethod.Post, "payments", corpo, ct));
    }

    public async Task<PixGateway?> ObterPixAsync(CredencialGateway c, string cobrancaId, CancellationToken ct)
    {
        var j = await EnviarAsync(c, HttpMethod.Get, $"payments/{Uri.EscapeDataString(cobrancaId)}/pixQrCode", null, ct);
        return Str(j, "payload") is { } payload ? new PixGateway(payload, Str(j, "encodedImage")) : null;
    }

    public async Task<string?> ObterLinhaDigitavelAsync(CredencialGateway c, string cobrancaId, CancellationToken ct)
    {
        var j = await EnviarAsync(c, HttpMethod.Get, $"payments/{Uri.EscapeDataString(cobrancaId)}/identificationField", null, ct);
        return Str(j, "identificationField");
    }

    public Task CancelarCobrancaAsync(CredencialGateway c, string cobrancaId, CancellationToken ct) =>
        EnviarAsync(c, HttpMethod.Delete, $"payments/{Uri.EscapeDataString(cobrancaId)}", null, ct);

    public async Task<string> CriarWebhookAsync(CredencialGateway c, NovoWebhook w, CancellationToken ct)
    {
        var corpo = new JsonObject
        {
            ["name"] = w.Nome,
            ["url"] = w.Url,
            ["email"] = w.Email,
            ["enabled"] = true,
            ["interrupted"] = false,
            ["apiVersion"] = 3,
            ["authToken"] = w.AuthToken,
            ["sendType"] = "SEQUENTIALLY",
            ["events"] = new JsonArray(w.Eventos.Select(e => (JsonNode)JsonValue.Create(e)!).ToArray())
        };
        var j = await EnviarAsync(c, HttpMethod.Post, "webhooks", corpo, ct);
        return Str(j, "id") ?? throw new GatewayPagamentoException("Asaas não devolveu o id do webhook.");
    }

    public Task RemoverWebhookAsync(CredencialGateway c, string webhookId, CancellationToken ct) =>
        EnviarAsync(c, HttpMethod.Delete, $"webhooks/{Uri.EscapeDataString(webhookId)}", null, ct);

    public async Task<string> AgendarNotaFiscalAsync(CredencialGateway c, NotaFiscalGateway n, CancellationToken ct)
    {
        var corpo = new JsonObject
        {
            ["payment"] = n.CobrancaId,
            ["serviceDescription"] = n.DescricaoServico,
            ["observations"] = n.Observacoes ?? "",
            ["value"] = n.Valor,
            ["deductions"] = 0,
            ["effectiveDate"] = n.DataEmissao.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["municipalServiceCode"] = string.IsNullOrWhiteSpace(n.CodigoServicoMunicipal) ? null : n.CodigoServicoMunicipal,
            ["municipalServiceName"] = string.IsNullOrWhiteSpace(n.NomeServicoMunicipal) ? null : n.NomeServicoMunicipal,
            ["taxes"] = new JsonObject
            {
                ["retainIss"] = n.RetemIss, ["iss"] = n.AliquotaIss, ["pis"] = 0, ["cofins"] = 0, ["csll"] = 0, ["inss"] = 0, ["ir"] = 0
            }
        };
        var j = await EnviarAsync(c, HttpMethod.Post, "invoices", corpo, ct);
        return Str(j, "id") ?? throw new GatewayPagamentoException("Asaas não devolveu o id da nota fiscal.");
    }

    // ---------------- HTTP ----------------

    private async Task<JsonObject?> PrimeiroAsync(CredencialGateway c, string caminho, CancellationToken ct)
    {
        var j = await EnviarAsync(c, HttpMethod.Get, caminho, null, ct);
        return j["data"] is JsonArray { Count: > 0 } a ? a[0] as JsonObject : null;
    }

    private async Task<JsonObject> EnviarAsync(CredencialGateway c, HttpMethod metodo, string caminho, JsonObject? corpo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(c.ChaveApi)) throw new GatewayPagamentoException("Chave API do Asaas não configurada.");
        using var req = new HttpRequestMessage(metodo, new Uri(new Uri(AsaasOptions.UrlBase(c.Sandbox)), caminho));
        req.Headers.TryAddWithoutValidation("access_token", c.ChaveApi);
        req.Headers.TryAddWithoutValidation("User-Agent", opcoes.Value.UserAgent);
        req.Headers.Accept.ParseAdd("application/json");
        if (corpo is not null) req.Content = JsonContent.Create(corpo, options: Json);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, opcoes.Value.TimeoutSegundos)));
        HttpResponseMessage resp;
        try { resp = await http.SendAsync(req, cts.Token); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new GatewayPagamentoException($"Asaas indisponível ({metodo} {Rota(caminho)}): {ex.GetType().Name}.");
        }
        using (resp)
        {
            var texto = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                var msg = Erros(texto) ?? resp.ReasonPhrase ?? "erro";
                log.LogWarning("Asaas {Metodo} {Rota} respondeu {Status}: {Mensagem}", metodo, Rota(caminho), (int)resp.StatusCode, msg);
                throw new GatewayPagamentoException((int)resp.StatusCode switch
                {
                    401 => "Chave API do Asaas inválida ou sem permissão.",
                    403 => "Operação não permitida para esta conta Asaas.",
                    _ => $"Asaas recusou a operação ({(int)resp.StatusCode}): {msg}"
                }, (int)resp.StatusCode);
            }
            if (string.IsNullOrWhiteSpace(texto)) return [];
            return JsonNode.Parse(texto) as JsonObject ?? [];
        }
    }

    /// <summary>Rota sem query string (a query pode conter CPF/CNPJ).</summary>
    private static string Rota(string caminho) => caminho.Split('?')[0];

    private static string? Erros(string texto)
    {
        try
        {
            if (JsonNode.Parse(texto)?["errors"] is JsonArray a)
                return string.Join("; ", a.Select(e => e?["description"]?.GetValue<string>()).Where(d => !string.IsNullOrWhiteSpace(d)));
        }
        catch (JsonException) { }
        return null;
    }

    private static CobrancaGateway Cobranca(JsonObject j) =>
        new(Str(j, "id") ?? throw new GatewayPagamentoException("Asaas não devolveu o id da cobrança."),
            Str(j, "status") ?? "PENDING", Str(j, "invoiceUrl"), Str(j, "bankSlipUrl"));

    internal static string? Str(JsonObject? j, string campo) => j?[campo] switch
    {
        null => null,
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonNode n => n.ToJsonString()
    };
}
