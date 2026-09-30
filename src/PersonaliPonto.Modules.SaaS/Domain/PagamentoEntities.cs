namespace PersonaliPonto.Modules.SaaS.Domain;

/// <summary>Situação da cobrança da fatura no gateway de pagamento (Asaas).</summary>
public enum StatusCobrancaGateway
{
    /// <summary>Sem cobrança automática (gateway não configurado, cliente público, valor zero, fatura manual).</summary>
    Nenhuma = 0,
    /// <summary>Aguardando envio ao gateway (outbox, com retentativa).</summary>
    Pendente = 1,
    Emitida = 2,
    /// <summary>Última tentativa de envio falhou; o outbox tenta de novo.</summary>
    Falha = 3,
    /// <summary>Cobrança removida no gateway (PAYMENT_DELETED).</summary>
    Cancelada = 4,
    /// <summary>Pagamento estornado no gateway (PAYMENT_REFUNDED/chargeback).</summary>
    Estornada = 5
}

/// <summary>Dados da cobrança externa gravados na própria fatura (de canal ou de cliente).</summary>
public interface ICobrancaGateway
{
    Guid Id { get; }
    decimal Valor { get; }
    DateOnly Vencimento { get; }
    StatusFatura Status { get; }
    StatusCobrancaGateway GatewayStatus { get; set; }
    /// <summary>Conta do gateway que emitiu (nulo = conta da plataforma/Owner).</summary>
    Guid? GatewayContaId { get; set; }
    /// <summary>Id do pagamento no Asaas (pay_...).</summary>
    string? GatewayCobrancaId { get; set; }
    /// <summary>Link da fatura no Asaas (permite Pix, boleto e cartão).</summary>
    string? GatewayLink { get; set; }
    string? GatewayBoletoUrl { get; set; }
    string? GatewayLinhaDigitavel { get; set; }
    string? GatewayPixCopiaCola { get; set; }
    /// <summary>Imagem PNG do QR Code Pix em base64.</summary>
    string? GatewayPixQrCode { get; set; }
    /// <summary>Data do último evento de webhook aplicado (eventos mais antigos que este são ignorados).</summary>
    DateTimeOffset? GatewayUltimoEventoEm { get; set; }
    string? GatewayErro { get; set; }
}

/// <summary>
/// Conta Asaas própria de um revendedor Premium (E9). A chave API fica cifrada (Data Protection, propósito
/// dedicado) e nunca é exibida de novo — só os 4 últimos caracteres. O webhook criado na conta do revendedor usa
/// um authToken próprio; guardamos apenas o hash SHA-256 dele para rotear os eventos.
/// </summary>
public class ContaGatewayCanal
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CanalId { get; set; }
    public string Provedor { get; set; } = "asaas";
    public bool Sandbox { get; set; }
    public string ChaveCifrada { get; set; } = "";
    public string ChaveFinal { get; set; } = "";
    public string? WebhookId { get; set; }
    public string WebhookTokenHash { get; set; } = "";
    public string? NomeConta { get; set; }
    public string? DocumentoConta { get; set; }
    public bool Ativa { get; set; } = true;
    public DateTimeOffset ConectadaEm { get; set; }
    public Guid? ConectadaPor { get; set; }
    public DateTimeOffset? DesconectadaEm { get; set; }
}

/// <summary>Evento de webhook recebido (idempotência por origem + id do evento). Visível só ao sistema.</summary>
public class EventoGateway
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>"owner" ou o id da <see cref="ContaGatewayCanal"/>.</summary>
    public string Origem { get; set; } = "";
    public string EventoId { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string? CobrancaId { get; set; }
    public DateTimeOffset? OcorridoEm { get; set; }
    public DateTimeOffset RecebidoEm { get; set; }
    public DateTimeOffset? ProcessadoEm { get; set; }
    /// <summary>Resultado: aplicado, ignorado (fora de ordem/sem fatura), erro.</summary>
    public string? Resultado { get; set; }
    public string Payload { get; set; } = "";
}
