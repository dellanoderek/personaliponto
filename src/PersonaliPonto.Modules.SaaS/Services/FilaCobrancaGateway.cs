using System.Text.Json;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Modules.SaaS.Domain;

namespace PersonaliPonto.Modules.SaaS.Services;

/// <summary>Mensagem do outbox que pede a emissão da cobrança de uma fatura no gateway.</summary>
public sealed record PedidoCobrancaGateway(string Origem, Guid FaturaId)
{
    public const string TipoMensagem = "gateway.cobranca";
    public const string FaturaCanal = "canal";
    public const string FaturaCliente = "cliente";
}

/// <summary>
/// Enfileira a emissão da cobrança no gateway (outbox, mesma transação da fatura). A decisão de cobrar (conta do
/// recebedor configurada/conectada, cliente público, Premium) é do handler de infraestrutura; aqui só se filtra o óbvio.
/// </summary>
public static class FilaCobrancaGateway
{
    public static void Enfileirar(ISaasDbContext db, FaturaCanal f, IClock clock) =>
        Enfileirar(db, f, new PedidoCobrancaGateway(PedidoCobrancaGateway.FaturaCanal, f.Id), null, clock);

    /// <summary>Mensagem de outbox para uma fatura já marcada como pendente (varredura da rotina de plataforma).</summary>
    public static OutboxMessage Mensagem(PedidoCobrancaGateway pedido, Guid? tenantId, IClock clock) => new()
    {
        TenantId = tenantId, Tipo = PedidoCobrancaGateway.TipoMensagem, Payload = JsonSerializer.Serialize(pedido), CriadoEm = clock.UtcNow
    };

    public static void Enfileirar(ISaasDbContext db, Fatura f, IClock clock, bool comMensagem = true) =>
        Enfileirar(db, f, new PedidoCobrancaGateway(PedidoCobrancaGateway.FaturaCliente, f.Id), f.TenantId, clock, comMensagem);

    private static void Enfileirar(ISaasDbContext db, ICobrancaGateway f, PedidoCobrancaGateway pedido, Guid? tenantId, IClock clock, bool comMensagem = true)
    {
        if (f.Status != StatusFatura.Aberta || f.Valor <= 0) return;
        f.GatewayStatus = StatusCobrancaGateway.Pendente;
        if (!comMensagem) return;
        db.Outbox.Add(Mensagem(pedido, tenantId, clock));
    }
}
