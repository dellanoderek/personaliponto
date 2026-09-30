using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Infrastructure.Persistence;
using PersonaliPonto.Infrastructure.Rotinas;
using PersonaliPonto.Infrastructure.Tenancy;
using PersonaliPonto.Modules.SaaS.Domain;
using PersonaliPonto.Modules.SaaS.Services;

namespace PersonaliPonto.Infrastructure.Pagamentos;

public enum ResultadoWebhook { NaoAutorizado, Invalido, Duplicado, Recebido }

/// <summary>
/// Webhook do Asaas (conta da plataforma e contas dos revendedores). A conta de origem é identificada pelo
/// authToken (header <c>asaas-access-token</c>): o da plataforma vem da configuração; o de cada revendedor é
/// comparado pelo hash SHA-256. Idempotente por (origem, id do evento) com índice único; tolerante a eventos fora
/// de ordem (evento mais antigo que o último aplicado na fatura é registrado e ignorado). Falha ao aplicar vai
/// para o outbox (retentativa) e o webhook responde 200 — o Asaas não precisa reenviar.
/// </summary>
public sealed class WebhookAsaasService(
    PersonaliPontoDbContext db, RequestContext ctx, FaturamentoCanalService faturamentoCanal, FaturamentoService faturamento,
    IClock clock, IOptions<AsaasOptions> opcoes, ILogger<WebhookAsaasService> log)
{
    public const string Owner = "owner";
    public const string TipoReprocessar = "gateway.evento";
    private static readonly TimeZoneInfo Fuso = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public async Task<ResultadoWebhook> ReceberAsync(string? token, string corpo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 255) return ResultadoWebhook.NaoAutorizado;
        using var _ = ctx.ComoSistema();

        var origem = await OrigemAsync(token, ct);
        if (origem is null)
        {
            log.LogWarning("Webhook Asaas recusado: authToken desconhecido.");
            return ResultadoWebhook.NaoAutorizado;
        }

        JsonObject? json;
        try { json = JsonNode.Parse(corpo) as JsonObject; } catch (JsonException) { json = null; }
        var eventoId = AsaasGateway.Str(json, "id");
        var tipo = AsaasGateway.Str(json, "event");
        if (json is null || string.IsNullOrWhiteSpace(eventoId) || string.IsNullOrWhiteSpace(tipo) || eventoId.Length > 100 || tipo.Length > 60)
            return ResultadoWebhook.Invalido;

        if (await db.EventosGateway.AnyAsync(e => e.Origem == origem && e.EventoId == eventoId, ct)) return ResultadoWebhook.Duplicado;
        var ev = new EventoGateway
        {
            Origem = origem, EventoId = eventoId, Tipo = tipo, CobrancaId = Limitar(AsaasGateway.Str(json["payment"] as JsonObject, "id"), 60),
            OcorridoEm = Data(AsaasGateway.Str(json, "dateCreated")), RecebidoEm = clock.UtcNow, Payload = corpo
        };
        db.EventosGateway.Add(ev);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return ResultadoWebhook.Duplicado; // entrega concorrente do mesmo evento
        }

        try
        {
            ev.Resultado = await AplicarAsync(ev, json, ct);
            ev.ProcessadoEm = clock.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogError("Falha ao aplicar evento Asaas {Evento} ({Tipo}): {Erro}. Reprocessamento agendado.", eventoId, tipo, ex.Message);
            db.ChangeTracker.Clear();
            var salvo = await db.EventosGateway.FirstAsync(e => e.Id == ev.Id, ct);
            salvo.Resultado = Limitar("erro: " + ex.Message, 300);
            db.Outbox.Add(new OutboxMessage { Tipo = TipoReprocessar, Payload = JsonSerializer.Serialize(new { EventoId = ev.Id }), CriadoEm = clock.UtcNow });
            await db.SaveChangesAsync(ct);
        }
        return ResultadoWebhook.Recebido;
    }

    /// <summary>Reprocessa um evento gravado cuja aplicação falhou (handler do outbox, modo sistema).</summary>
    public async Task ReprocessarAsync(Guid eventoId, CancellationToken ct)
    {
        var ev = await db.EventosGateway.FirstOrDefaultAsync(e => e.Id == eventoId, ct);
        if (ev is null || ev.ProcessadoEm is not null) return;
        ev.Resultado = await AplicarAsync(ev, (JsonObject)JsonNode.Parse(ev.Payload)!, ct);
        ev.ProcessadoEm = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task<string?> OrigemAsync(string token, CancellationToken ct)
    {
        var tokenOwner = opcoes.Value.Owner.WebhookToken;
        if (!string.IsNullOrEmpty(tokenOwner)
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(tokenOwner)))
            return Owner;
        var hash = CofreChavesGateway.Hash(token);
        var conta = await db.ContasGateway.AsNoTracking().Where(c => c.WebhookTokenHash == hash && c.Ativa).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        return conta?.ToString();
    }

    private async Task<string> AplicarAsync(EventoGateway ev, JsonObject json, CancellationToken ct)
    {
        if (json["payment"] is not JsonObject pay || AsaasGateway.Str(pay, "id") is not { } cobrancaId) return "ignorado: evento sem cobrança";
        var contaId = ev.Origem == Owner ? (Guid?)null : Guid.Parse(ev.Origem);

        // Localiza a fatura pela cobrança (ou pela referência externa, se a gravação do id não chegou a ocorrer).
        ICobrancaGateway? f = await db.FaturasCanal.FirstOrDefaultAsync(x => x.GatewayCobrancaId == cobrancaId, ct);
        f ??= await db.Faturas.FirstOrDefaultAsync(x => x.GatewayCobrancaId == cobrancaId, ct);
        if (f is null && AsaasGateway.Str(pay, "externalReference") is { } refExt && refExt.Split(':') is [var origemRef, var idRef] && Guid.TryParse(idRef, out var fid))
            f = origemRef == PedidoCobrancaGateway.FaturaCanal ? await db.FaturasCanal.FirstOrDefaultAsync(x => x.Id == fid && x.GatewayCobrancaId == null, ct)
                : origemRef == PedidoCobrancaGateway.FaturaCliente ? await db.Faturas.FirstOrDefaultAsync(x => x.Id == fid && x.GatewayCobrancaId == null, ct) : null;
        if (f is null) return "ignorado: cobrança desconhecida";

        // Roteamento: a conta que enviou o evento tem de ser a conta que recebe a fatura.
        if (!await ContaConfereAsync(f, contaId, ct))
        {
            log.LogWarning("Evento Asaas {Evento} recusado: conta de origem não corresponde ao recebedor da fatura {Fatura}.", ev.EventoId, f.Id);
            return "rejeitado: conta de origem não corresponde ao recebedor";
        }

        // Fora de ordem: o estado de um evento mais antigo que o último aplicado não prevalece.
        var quando = ev.OcorridoEm ?? ev.RecebidoEm;
        if (f.GatewayUltimoEventoEm is { } ultimo && quando < ultimo) return "ignorado: fora de ordem";
        f.GatewayUltimoEventoEm = quando;
        f.GatewayCobrancaId ??= cobrancaId;
        f.GatewayContaId ??= contaId;

        var origem = ev.Origem == Owner ? "asaas:plataforma" : "asaas:revendedor";
        switch (ev.Tipo)
        {
            case "PAYMENT_RECEIVED" or "PAYMENT_CONFIRMED":
            {
                if (f.GatewayStatus is StatusCobrancaGateway.Cancelada or StatusCobrancaGateway.Estornada or StatusCobrancaGateway.Nenhuma or StatusCobrancaGateway.Pendente or StatusCobrancaGateway.Falha)
                    f.GatewayStatus = StatusCobrancaGateway.Emitida;
                var valor = Decimal(pay, "value") ?? f.Valor;
                var pagaEm = DataDia(pay, "paymentDate") ?? DataDia(pay, "clientPaymentDate") ?? DataDia(pay, "confirmedDate") ?? faturamentoCanal.Hoje;
                var forma = "Asaas · " + (AsaasGateway.Str(pay, "billingType") switch { "PIX" => "Pix", "BOLETO" => "Boleto", "CREDIT_CARD" => "Cartão", var x => x ?? "?" });
                var baixou = f is FaturaCanal
                    ? await faturamentoCanal.BaixarViaGatewayAsync(f.Id, pagaEm, valor, forma, origem, ct)
                    : await faturamento.BaixarViaGatewayAsync(f.Id, pagaEm, valor, forma, origem, ct);
                return baixou ? "aplicado: baixa" : "ignorado: fatura não estava em aberto";
            }
            case "PAYMENT_REFUNDED" or "PAYMENT_RECEIVED_IN_CASH_UNDONE" or "PAYMENT_CHARGEBACK_REQUESTED":
            {
                f.GatewayStatus = StatusCobrancaGateway.Estornada;
                var motivo = ev.Tipo == "PAYMENT_CHARGEBACK_REQUESTED" ? "Chargeback solicitado no Asaas." : "Pagamento estornado no Asaas.";
                var estornou = f is FaturaCanal
                    ? await faturamentoCanal.EstornarViaGatewayAsync(f.Id, motivo, origem, ct)
                    : await faturamento.EstornarViaGatewayAsync(f.Id, motivo, origem, ct);
                if (!estornou) await db.SaveChangesAsync(ct);
                return estornou ? "aplicado: estorno" : "registrado: estorno sem baixa";
            }
            case "PAYMENT_DELETED":
                if (f.Status == StatusFatura.Aberta)
                {
                    f.GatewayStatus = StatusCobrancaGateway.Cancelada;
                    f.GatewayPixCopiaCola = f.GatewayPixQrCode = f.GatewayLinhaDigitavel = f.GatewayBoletoUrl = f.GatewayLink = null;
                }
                await db.SaveChangesAsync(ct);
                return "aplicado: cobrança removida";
            case "PAYMENT_RESTORED":
                if (f.GatewayStatus == StatusCobrancaGateway.Cancelada) f.GatewayStatus = StatusCobrancaGateway.Emitida;
                await db.SaveChangesAsync(ct);
                return "aplicado: cobrança restaurada";
            default:
                await db.SaveChangesAsync(ct);
                return "registrado";
        }
    }

    private async Task<bool> ContaConfereAsync(ICobrancaGateway f, Guid? contaId, CancellationToken ct)
    {
        if (f.GatewayContaId is { } emissora && emissora != contaId) return false;
        Guid? canalConta = contaId is { } cid ? await db.ContasGateway.AsNoTracking().Where(c => c.Id == cid).Select(c => (Guid?)c.CanalId).FirstOrDefaultAsync(ct) : null;
        return f switch
        {
            FaturaCanal fc => contaId is null ? fc.CanalRecebedorId == Canal.OwnerRaizId : fc.CanalRecebedorId == canalConta,
            Fatura fa => contaId is not null && await db.Tenants.AsNoTracking().AnyAsync(t => t.Id == fa.TenantId && t.CanalDonoId == canalConta
                                                                                             && t.TipoEntidade != TipoEntidade.Publica, ct),
            _ => false
        };
    }

    /// <summary>Datas do Asaas ("yyyy-MM-dd HH:mm:ss") estão no horário de Brasília.</summary>
    public static DateTimeOffset? Data(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (DateTime.TryParseExact(s, ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return new DateTimeOffset(d, Fuso.GetUtcOffset(d));
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var o) ? o : null;
    }

    private static DateOnly? DataDia(JsonObject j, string campo) =>
        DateOnly.TryParseExact(AsaasGateway.Str(j, campo) ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static decimal? Decimal(JsonObject j, string campo) =>
        j[campo] is JsonValue v && v.TryGetValue<decimal>(out var d) ? d
        : decimal.TryParse(AsaasGateway.Str(j, campo), NumberStyles.Number, CultureInfo.InvariantCulture, out var p) ? p : null;

    private static string? Limitar(string? s, int n) => s is null ? null : s.Length > n ? s[..n] : s;
}

/// <summary>Reprocessamento (outbox) de eventos de webhook cuja aplicação falhou.</summary>
public sealed class EventoGatewayHandler(WebhookAsaasService webhook) : IOutboxHandler
{
    public string Tipo => WebhookAsaasService.TipoReprocessar;

    public async Task ProcessarAsync(OutboxMessage mensagem, CancellationToken ct)
    {
        var id = JsonNode.Parse(mensagem.Payload)?["EventoId"]?.GetValue<Guid>() ?? throw new InvalidOperationException("Evento inválido.");
        await webhook.ReprocessarAsync(id, ct);
    }
}
