using System.Text;
using System.Threading.RateLimiting;
using PersonaliPonto.Infrastructure.Pagamentos;

namespace PersonaliPonto.Web.Servicos;

/// <summary>
/// Webhook público do Asaas (E3/E9). Fica no painel Web — o mesmo host cuja URL pública (Asaas:UrlPublica) é cadastrada
/// nas contas: POST {UrlPublica}/api/pagamentos/asaas/webhook. Autenticação pelo authToken no header
/// <c>asaas-access-token</c>; limite de taxa por IP; corpo limitado a 256 KB.
/// </summary>
public static class PagamentosEndpoints
{
    public const string PoliticaWebhook = "webhook-pagamentos";
    private const int TamanhoMaximo = 256 * 1024;

    public static IServiceCollection AddLimiteWebhook(this IServiceCollection services, IConfiguration cfg) =>
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // Sem reexecução da página de status (o POST reexecutado cairia no antiforgery do Blazor).
            o.OnRejected = (ctx, _) =>
            {
                if (ctx.HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodePagesFeature>() is { } scp) scp.Enabled = false;
                return ValueTask.CompletedTask;
            };
            o.AddPolicy(PoliticaWebhook, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = cfg.GetValue("Asaas:WebhookLimitePorMinuto", 300), Window = TimeSpan.FromMinutes(1) }));
        });

    public static void MapPagamentosEndpoints(this WebApplication app)
    {
        app.MapPost(AsaasOptions.CaminhoWebhook, async (HttpContext http, WebhookAsaasService webhook, CancellationToken ct) =>
        {
            if (http.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodePagesFeature>() is { } scp) scp.Enabled = false; // API: sem página HTML de erro
            if (http.Request.ContentLength > TamanhoMaximo) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            using var leitor = new StreamReader(http.Request.Body, Encoding.UTF8);
            var buffer = new char[TamanhoMaximo + 1];
            var lidos = await leitor.ReadBlockAsync(buffer, ct);
            if (lidos > TamanhoMaximo) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            var token = http.Request.Headers["asaas-access-token"].ToString();
            return await webhook.ReceberAsync(token, new string(buffer, 0, lidos), ct) switch
            {
                ResultadoWebhook.NaoAutorizado => Results.Unauthorized(),
                ResultadoWebhook.Invalido => Results.BadRequest(),
                _ => Results.Ok(new { recebido = true })
            };
        }).AllowAnonymous().DisableAntiforgery().RequireRateLimiting(PoliticaWebhook);
    }
}
