using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Infrastructure.Persistence;
using PersonaliPonto.Infrastructure.Tenancy;
using PersonaliPonto.Modules.SaaS.Domain;
using PersonaliPonto.Modules.SaaS.Services;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Infrastructure.Pagamentos;

/// <summary>
/// Custódia das chaves Asaas dos revendedores: cifra com Data Protection (propósito dedicado, chaves do servidor
/// persistidas no schema de sistema) e gera/hasheia os authTokens de webhook.
/// </summary>
public sealed class CofreChavesGateway(IDataProtectionProvider dp)
{
    public const string Proposito = "PersonaliPonto.Pagamentos.ChaveAsaasRevendedor.v1";
    private readonly IDataProtector _protetor = dp.CreateProtector(Proposito);

    public string Cifrar(string chave) => _protetor.Protect(chave);
    public string Decifrar(string cifrada) => _protetor.Unprotect(cifrada);

    /// <summary>authToken de webhook: 48 caracteres base64url aleatórios (Asaas exige 32–255, sem espaços).</summary>
    public static string NovoToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(36)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

/// <summary>Resolve a credencial da conta que recebe: a da plataforma (configuração) ou a do revendedor (cofre).</summary>
public sealed class CredenciaisGateway(IOptions<AsaasOptions> opcoes, CofreChavesGateway cofre)
{
    public CredencialGateway? Owner() =>
        opcoes.Value.Owner.Configurada ? new CredencialGateway(opcoes.Value.Owner.ChaveApi!.Trim(), opcoes.Value.Sandbox) : null;

    public CredencialGateway DaConta(ContaGatewayCanal c) => new(cofre.Decifrar(c.ChaveCifrada), c.Sandbox);
}

public sealed record ResumoContaGateway(bool Conectada, string? NomeConta, string? ChaveFinal, bool Sandbox, DateTimeOffset? ConectadaEm);

/// <summary>
/// E9 — conexão da conta Asaas própria do revendedor Premium. Valida a chave (myAccount), cria o webhook na conta dele
/// com authToken próprio e guarda a chave cifrada (nunca exibida de novo). Dinheiro dos clientes do revendedor vai
/// direto para a conta dele: a plataforma não usa split nem subconta.
/// </summary>
public sealed class ContaGatewayService(
    PersonaliPontoDbContext db, RequestContext ctx, IGatewayPagamento gateway, CofreChavesGateway cofre, CredenciaisGateway credenciais,
    FaturamentoCanalService faturamento, AuditService audit, IClock clock, IOptions<AsaasOptions> opcoes, ILogger<ContaGatewayService> log)
{
    public static readonly string[] EventosWebhook =
    [
        "PAYMENT_CREATED", "PAYMENT_UPDATED", "PAYMENT_CONFIRMED", "PAYMENT_RECEIVED", "PAYMENT_OVERDUE", "PAYMENT_DELETED",
        "PAYMENT_RESTORED", "PAYMENT_REFUNDED", "PAYMENT_RECEIVED_IN_CASH_UNDONE", "PAYMENT_CHARGEBACK_REQUESTED"
    ];

    public async Task<ResumoContaGateway> ResumoAsync(Guid canalId, CancellationToken ct)
    {
        var c = await db.ContasGateway.AsNoTracking().FirstOrDefaultAsync(x => x.CanalId == canalId && x.Ativa, ct);
        return c is null ? new(false, null, null, false, null) : new(true, c.NomeConta, c.ChaveFinal, c.Sandbox, c.ConectadaEm);
    }

    private Guid ExigirAdminRevendedor()
    {
        if (ctx.IsSystem || ctx.CanalId is not { } id || ctx.Papel != Roles.AdminRevendedor)
            throw new AcessoNegadoException("Apenas o administrador do revendedor conecta a conta Asaas.");
        return id;
    }

    public async Task<ResumoContaGateway> ConectarAsync(string chaveApi, CancellationToken ct)
    {
        var canalId = ExigirAdminRevendedor();
        if (!await faturamento.PremiumAtivoAsync(canalId, ct))
            throw new RegraNegocioException("A cobrança automática dos clientes pela sua conta Asaas é um recurso Premium.");
        var chave = (chaveApi ?? "").Trim();
        if (chave.Length is < 20 or > 400 || chave.Any(char.IsWhiteSpace))
            throw new RegraNegocioException("Chave API inválida: cole a chave completa gerada em Asaas → Integrações.");
        if (await db.ContasGateway.AnyAsync(x => x.CanalId == canalId && x.Ativa, ct))
            throw new RegraNegocioException("Já existe uma conta Asaas conectada. Desconecte-a antes de conectar outra.");
        var urlPublica = opcoes.Value.UrlPublica?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(urlPublica))
            throw new RegraNegocioException("A URL pública da plataforma não está configurada (Asaas:UrlPublica). Contate o suporte.");

        // Ambiente pela própria chave ($aact_hmlg_ = sandbox, $aact_prod_ = produção); sem prefixo, o da plataforma.
        var sandbox = chave.Contains("_hmlg_", StringComparison.Ordinal) || (!chave.Contains("_prod_", StringComparison.Ordinal) && opcoes.Value.Sandbox);
        var cred = new CredencialGateway(chave, sandbox);
        ContaGatewayInfo info;
        try { info = await gateway.ObterContaAsync(cred, ct); }
        catch (GatewayPagamentoException ex) { throw new RegraNegocioException("Não foi possível validar a chave no Asaas: " + ex.Message); }

        var canal = await db.Canais.AsNoTracking().FirstAsync(c => c.Id == canalId, ct);
        var token = CofreChavesGateway.NovoToken();
        string webhookId;
        try
        {
            webhookId = await gateway.CriarWebhookAsync(cred, new NovoWebhook($"PersonaliPonto — {canal.NomeMarca}", urlPublica + AsaasOptions.CaminhoWebhook,
                canal.Email ?? opcoes.Value.EmailWebhook, token, EventosWebhook), ct);
        }
        catch (GatewayPagamentoException ex) { throw new RegraNegocioException("Não foi possível criar o webhook na sua conta Asaas: " + ex.Message); }

        var conta = new ContaGatewayCanal
        {
            CanalId = canalId, Sandbox = sandbox, ChaveCifrada = cofre.Cifrar(chave), ChaveFinal = CredencialGateway.Final(chave),
            WebhookId = webhookId, WebhookTokenHash = CofreChavesGateway.Hash(token), NomeConta = Limitar(info.Nome, 200),
            DocumentoConta = info.CpfCnpj is { Length: <= 14 } d ? d : null, ConectadaEm = clock.UtcNow, ConectadaPor = ctx.UserId
        };
        db.ContasGateway.Add(conta);
        audit.Registrar("gateway.conta_conectada", nameof(ContaGatewayCanal), conta.Id,
            new { provedor = conta.Provedor, sandbox, chaveFinal = conta.ChaveFinal, conta.NomeConta, conta.WebhookId });
        try { await db.SaveChangesAsync(ct); }
        catch
        {
            try { await gateway.RemoverWebhookAsync(cred, webhookId, CancellationToken.None); } catch (Exception ex) { log.LogWarning("Webhook órfão {Id} não removido: {Erro}", webhookId, ex.Message); }
            throw;
        }
        log.LogInformation("Conta Asaas conectada para o canal {Canal} (final {Final}, sandbox {Sandbox}).", canalId, conta.ChaveFinal, sandbox);
        return new(true, conta.NomeConta, conta.ChaveFinal, sandbox, conta.ConectadaEm);
    }

    public async Task DesconectarAsync(CancellationToken ct)
    {
        var canalId = ExigirAdminRevendedor();
        var conta = await db.ContasGateway.FirstOrDefaultAsync(x => x.CanalId == canalId && x.Ativa, ct)
                    ?? throw new RegraNegocioException("Nenhuma conta Asaas conectada.");
        var webhookRemovido = false;
        if (conta.WebhookId is { } wid)
        {
            try { await gateway.RemoverWebhookAsync(credenciais.DaConta(conta), wid, ct); webhookRemovido = true; }
            catch (Exception ex) when (ex is GatewayPagamentoException or CryptographicException)
            {
                // Chave revogada/conta encerrada: desconecta mesmo assim (o token deixa de ser aceito aqui).
                log.LogWarning("Webhook {Id} do canal {Canal} não removido no Asaas: {Erro}", wid, canalId, ex.Message);
            }
        }
        conta.Ativa = false;
        conta.DesconectadaEm = clock.UtcNow;
        conta.ChaveCifrada = "";
        audit.Registrar("gateway.conta_desconectada", nameof(ContaGatewayCanal), conta.Id, new { chaveFinal = conta.ChaveFinal, webhookRemovido });
        await db.SaveChangesAsync(ct);
    }

    private static string? Limitar(string? s, int n) => s is null ? null : s.Length > n ? s[..n] : s;
}

/// <summary>Compras no painel web com cobrança imediata (Premium). Nunca exposto ao app mobile.</summary>
public sealed class ComprasCanalService(FaturamentoCanalService faturamento, RequestContext ctx)
{
    public async Task AtivarPremiumAsync(CancellationToken ct)
    {
        await faturamento.AtivarPremiumAsync(ct);
        var canalId = ctx.CanalId!.Value;
        using (ctx.ComoSistema()) await faturamento.CobrarPremiumImediatoAsync(canalId, ct);
    }
}
