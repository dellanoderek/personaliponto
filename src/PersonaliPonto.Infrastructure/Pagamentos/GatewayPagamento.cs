namespace PersonaliPonto.Infrastructure.Pagamentos;

/// <summary>Credencial de uma conta no gateway (chave API em claro só em memória, nunca em log).</summary>
public sealed record CredencialGateway(string ChaveApi, bool Sandbox)
{
    public override string ToString() => $"CredencialGateway {{ Sandbox = {Sandbox}, Chave = ****{Final(ChaveApi)} }}";
    public static string Final(string chave) => chave.Length <= 4 ? chave : chave[^4..];
}

public sealed record ContaGatewayInfo(string Nome, string? CpfCnpj, string? Email, string? Status);

public sealed record ClienteGateway(string Nome, string CpfCnpj, string? Email, string? Telefone, string ReferenciaExterna);

public enum FormaCobranca { Indefinida, Pix, Boleto, Cartao }

public sealed record NovaCobranca(string ClienteId, decimal Valor, DateOnly Vencimento, string Descricao, string ReferenciaExterna,
    FormaCobranca Forma = FormaCobranca.Indefinida);

public sealed record CobrancaGateway(string Id, string Status, string? LinkFatura, string? BoletoUrl);

public sealed record PixGateway(string CopiaECola, string? QrCodeBase64);

public sealed record NovoWebhook(string Nome, string Url, string Email, string AuthToken, IReadOnlyList<string> Eventos);

public sealed record NotaFiscalGateway(string CobrancaId, decimal Valor, DateOnly DataEmissao, string DescricaoServico,
    string? CodigoServicoMunicipal, string? NomeServicoMunicipal, decimal AliquotaIss, bool RetemIss, string? Observacoes);

/// <summary>Erro devolvido pelo gateway (mensagem já sem segredos).</summary>
public sealed class GatewayPagamentoException(string mensagem, int? statusHttp = null) : Exception(mensagem)
{
    public int? StatusHttp { get; } = statusHttp;
    /// <summary>Chave recusada (401/403): não adianta tentar de novo sem nova chave.</summary>
    public bool ChaveInvalida => StatusHttp is 401 or 403;
}

/// <summary>
/// Gateway de pagamento (implementação real: <see cref="AsaasGateway"/>; testes usam um fake). Todas as operações
/// recebem a credencial da conta — a da plataforma (Owner) ou a do revendedor —, nunca há conta implícita.
/// </summary>
public interface IGatewayPagamento
{
    Task<ContaGatewayInfo> ObterContaAsync(CredencialGateway c, CancellationToken ct);
    /// <summary>Procura o cliente pela referência externa (depois pelo CPF/CNPJ); atualiza ou cria. Devolve o id.</summary>
    Task<string> GarantirClienteAsync(CredencialGateway c, ClienteGateway cliente, CancellationToken ct);
    /// <summary>Cobrança já criada com esta referência externa (idempotência em retentativas), ou nulo.</summary>
    Task<CobrancaGateway?> BuscarCobrancaAsync(CredencialGateway c, string referenciaExterna, CancellationToken ct);
    Task<CobrancaGateway> CriarCobrancaAsync(CredencialGateway c, NovaCobranca cobranca, CancellationToken ct);
    Task<PixGateway?> ObterPixAsync(CredencialGateway c, string cobrancaId, CancellationToken ct);
    Task<string?> ObterLinhaDigitavelAsync(CredencialGateway c, string cobrancaId, CancellationToken ct);
    Task CancelarCobrancaAsync(CredencialGateway c, string cobrancaId, CancellationToken ct);
    Task<string> CriarWebhookAsync(CredencialGateway c, NovoWebhook webhook, CancellationToken ct);
    Task RemoverWebhookAsync(CredencialGateway c, string webhookId, CancellationToken ct);
    /// <summary>Agenda a NFS-e da cobrança (emitida pelo Asaas na data informada). Devolve o id da nota.</summary>
    Task<string> AgendarNotaFiscalAsync(CredencialGateway c, NotaFiscalGateway nota, CancellationToken ct);
}

/// <summary>Configuração do Asaas (seção "Asaas"; segredos por variável de ambiente, ex.: Asaas__Owner__ChaveApi).</summary>
public sealed class AsaasOptions
{
    public const string UrlSandbox = "https://api-sandbox.asaas.com/v3/";
    public const string UrlProducao = "https://api.asaas.com/v3/";

    /// <summary>"Sandbox" (padrão) ou "Producao". Vale para a conta do Owner e é o padrão das contas de revendedor.</summary>
    public string Ambiente { get; set; } = "Sandbox";
    public bool Sandbox => !string.Equals(Ambiente, "Producao", StringComparison.OrdinalIgnoreCase)
                           && !string.Equals(Ambiente, "Produção", StringComparison.OrdinalIgnoreCase);
    /// <summary>URL pública do painel (ex.: https://app.personaliponto.com.br), usada no webhook criado nas contas dos revendedores.</summary>
    public string? UrlPublica { get; set; }
    public string UserAgent { get; set; } = "PersonaliPonto/1.0";
    public int TimeoutSegundos { get; set; } = 30;
    public string EmailWebhook { get; set; } = "financeiro@personaliponto.com.br";
    public OwnerAsaasOptions Owner { get; set; } = new();
    public NotaFiscalAsaasOptions NotaFiscal { get; set; } = new();

    public static string UrlBase(bool sandbox) => sandbox ? UrlSandbox : UrlProducao;
    public const string CaminhoWebhook = "/api/pagamentos/asaas/webhook";
}

public sealed class OwnerAsaasOptions
{
    /// <summary>Chave API da conta Asaas da plataforma (segredo). Vazia = cobrança automática Owner → revendedor desligada.</summary>
    public string? ChaveApi { get; set; }
    /// <summary>authToken cadastrado no webhook da conta da plataforma (32–255 caracteres, segredo).</summary>
    public string? WebhookToken { get; set; }
    public bool Configurada => !string.IsNullOrWhiteSpace(ChaveApi);
}

/// <summary>NFS-e via Asaas para as cobranças da plataforma (desligada por padrão).</summary>
public sealed class NotaFiscalAsaasOptions
{
    public bool Habilitada { get; set; }
    public string DescricaoServico { get; set; } = "Licenciamento de uso de software (PersonaliPonto)";
    public string? CodigoServicoMunicipal { get; set; }
    public string? NomeServicoMunicipal { get; set; }
    public decimal AliquotaIss { get; set; }
    public bool RetemIss { get; set; }
}
