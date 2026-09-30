using System.Collections.Concurrent;
using PersonaliPonto.Infrastructure.Pagamentos;

namespace PersonaliPonto.IntegrationTests;

/// <summary>Gateway de pagamento em memória (substitui o Asaas nos testes). Registra a chave usada em cada chamada.</summary>
public sealed class FakeGateway : IGatewayPagamento
{
    private int _seq;
    public ConcurrentDictionary<string, (NovaCobranca Cobranca, string Chave)> Cobrancas { get; } = new();
    public ConcurrentDictionary<string, ClienteGateway> Clientes { get; } = new();
    public ConcurrentDictionary<string, (NovoWebhook Webhook, string Chave)> Webhooks { get; } = new();
    public ConcurrentBag<string> NotasFiscais { get; } = [];
    /// <summary>Chaves que falham em qualquer operação (simula Asaas fora do ar ou chave revogada).</summary>
    public ConcurrentDictionary<string, int> ChavesComFalha { get; } = new();
    public ConcurrentDictionary<string, bool> ChavesInvalidas { get; } = new();

    private void Checar(CredencialGateway c)
    {
        if (ChavesInvalidas.ContainsKey(c.ChaveApi)) throw new GatewayPagamentoException("Chave API do Asaas inválida ou sem permissão.", 401);
        if (ChavesComFalha.ContainsKey(c.ChaveApi)) throw new GatewayPagamentoException("Asaas indisponível (fake).", 503);
    }

    private string Id(string prefixo) => $"{prefixo}_{Interlocked.Increment(ref _seq):000000}";

    public Task<ContaGatewayInfo> ObterContaAsync(CredencialGateway c, CancellationToken ct)
    {
        Checar(c);
        return Task.FromResult(new ContaGatewayInfo("Conta Fake " + CredencialGateway.Final(c.ChaveApi), "12345678000195", "fake@asaas.test", "APPROVED"));
    }

    public Task<string> GarantirClienteAsync(CredencialGateway c, ClienteGateway cliente, CancellationToken ct)
    {
        Checar(c);
        var chave = c.ChaveApi + "|" + cliente.ReferenciaExterna;
        Clientes[chave] = cliente;
        return Task.FromResult("cus_" + Math.Abs(chave.GetHashCode()).ToString());
    }

    public Task<CobrancaGateway?> BuscarCobrancaAsync(CredencialGateway c, string referenciaExterna, CancellationToken ct)
    {
        Checar(c);
        var e = Cobrancas.FirstOrDefault(x => x.Value.Cobranca.ReferenciaExterna == referenciaExterna && x.Value.Chave == c.ChaveApi);
        return Task.FromResult(e.Key is null ? null : Cobranca(e.Key));
    }

    public Task<CobrancaGateway> CriarCobrancaAsync(CredencialGateway c, NovaCobranca n, CancellationToken ct)
    {
        Checar(c);
        var id = Id("pay");
        Cobrancas[id] = (n, c.ChaveApi);
        return Task.FromResult(Cobranca(id));
    }

    private static CobrancaGateway Cobranca(string id) => new(id, "PENDING", $"https://sandbox.asaas.com/i/{id}", $"https://sandbox.asaas.com/b/pdf/{id}");

    public Task<PixGateway?> ObterPixAsync(CredencialGateway c, string cobrancaId, CancellationToken ct)
    {
        Checar(c);
        return Task.FromResult<PixGateway?>(new PixGateway($"00020101021226PIX{cobrancaId}", "iVBORw0KGgo="));
    }

    public Task<string?> ObterLinhaDigitavelAsync(CredencialGateway c, string cobrancaId, CancellationToken ct)
    {
        Checar(c);
        return Task.FromResult<string?>("34191.79001 01043.510047 91020.150008 1 00000000014900");
    }

    public Task CancelarCobrancaAsync(CredencialGateway c, string cobrancaId, CancellationToken ct)
    {
        Checar(c);
        Cobrancas.TryRemove(cobrancaId, out _);
        return Task.CompletedTask;
    }

    public Task<string> CriarWebhookAsync(CredencialGateway c, NovoWebhook webhook, CancellationToken ct)
    {
        Checar(c);
        var id = Id("wh");
        Webhooks[id] = (webhook, c.ChaveApi);
        return Task.FromResult(id);
    }

    public Task RemoverWebhookAsync(CredencialGateway c, string webhookId, CancellationToken ct)
    {
        Checar(c);
        Webhooks.TryRemove(webhookId, out _);
        return Task.CompletedTask;
    }

    public Task<string> AgendarNotaFiscalAsync(CredencialGateway c, NotaFiscalGateway nota, CancellationToken ct)
    {
        Checar(c);
        NotasFiscais.Add(nota.CobrancaId);
        return Task.FromResult(Id("inv"));
    }
}
