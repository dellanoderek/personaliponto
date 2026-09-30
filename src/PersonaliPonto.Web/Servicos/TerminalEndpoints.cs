using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Infrastructure.Persistence;
using PersonaliPonto.Infrastructure.Seguranca;
using PersonaliPonto.Infrastructure.Tenancy;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Web.Servicos;

/// <summary>
/// API do Terminal Web de marcação (coletor "02 — browser"). Sem login de usuário: o navegador é
/// identificado por um cookie de terminal (HttpOnly, SameSite=Strict) emitido na ativação pelo RH.
/// Não depende do circuito SignalR do Blazor — funciona com a fila off-line do próprio navegador.
/// </summary>
public static class TerminalEndpoints
{
    public const string Cookie = "tc_terminal";
    private const string CabecalhoCsrf = "X-PersonaliPonto-Terminal";

    public sealed record MarcarTerminalRequest(
        Guid ClientId,
        string Identificacao,
        string? Pin,
        string? PinCifrado,
        bool Offline,
        DateTimeOffset? HorarioOfflineReferenciado,
        Guid? AncoraId);

    public sealed record TerminalInfo(string Nome, string Estabelecimento, string Empresa, string FusoHorario, AncoraHoraDto Ancora, string ChavePublica);

    private static readonly PartitionedRateLimiter<string> Limite = PartitionedRateLimiter.Create<string, string>(chave =>
        RateLimitPartition.GetSlidingWindowLimiter(chave, _ => new SlidingWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6 }));

    public static void MapTerminalEndpoints(this WebApplication app)
    {
        // Ativação: o RH autenticado transforma este navegador em terminal do estabelecimento.
        app.MapPost("/terminal/ativar", async ([FromForm] Guid estabelecimentoId, [FromForm] string nome, HttpContext http, TerminalService svc, CancellationToken ct) =>
        {
            var t = await svc.AtivarAsync(estabelecimentoId, nome, ct);
            http.Response.Cookies.Append(Cookie, t.Token, new CookieOptions
            {
                HttpOnly = true, Secure = http.Request.IsHttps, SameSite = SameSiteMode.Strict, IsEssential = true,
                Expires = DateTimeOffset.UtcNow.AddYears(1), Path = "/terminal"
            });
            return Results.LocalRedirect("/terminal");
        }).RequireAuthorization(Politicas.Rh);

        var api = app.MapGroup("/terminal/api").AllowAnonymous();

        api.MapGet("/info", async (HttpContext http, TerminalService terminais, PersonaliPontoDbContext db, MarcacaoService marcacoes, ChaveTerminal chave, CancellationToken ct) =>
        {
            var t = await terminais.ResolverAsync(http.Request.Cookies[Cookie], ct);
            if (t is null) return Results.Unauthorized();
            var e = await db.Estabelecimentos.AsNoTracking().Include(x => x.Empregador).FirstAsync(x => x.Id == t.EstabelecimentoId, ct);
            var ancora = await marcacoes.EmitirAncoraAsync(t.TenantId, t.EstabelecimentoId, null, t.Id, ct);
            return Results.Ok(new TerminalInfo(t.Nome, e.Nome, e.Empregador!.RazaoSocial, e.FusoHorario, ancora, await chave.PublicaAsync()));
        });

        api.MapPost("/marcacoes", async (MarcarTerminalRequest r, HttpContext http, TerminalService terminais, MarcacaoService marcacoes,
            ChaveTerminal chave, CancellationToken ct) =>
        {
            if (!http.Request.Headers.ContainsKey(CabecalhoCsrf)) return Results.BadRequest();
            var t = await terminais.ResolverAsync(http.Request.Cookies[Cookie], ct);
            if (t is null) return Results.Unauthorized();
            using var lease = Limite.AttemptAcquire(t.Id.ToString());
            if (!lease.IsAcquired) return Results.StatusCode(StatusCodes.Status429TooManyRequests);

            var pin = r.Pin;
            if (pin is null && r.PinCifrado is not null) pin = await chave.DecifrarAsync(r.PinCifrado);
            if (string.IsNullOrEmpty(pin) || string.IsNullOrWhiteSpace(r.Identificacao))
                return Results.Problem("Informe matrícula/CPF e PIN.", statusCode: 422);

            try
            {
                var f = await marcacoes.IdentificarPorPinAsync(t.EstabelecimentoId, r.Identificacao, pin, ct);
                var res = await marcacoes.RegistrarAsync(f,
                    new RegistrarMarcacaoRequest(r.ClientId, r.Offline, r.HorarioOfflineReferenciado, r.AncoraId, null, null, $"terminal:{t.Id}"),
                    Coletor.Browser, t.Id, http.Connection.RemoteIpAddress?.ToString(), ct);
                return Results.Ok(res);
            }
            catch (AcessoNegadoException ex)
            {
                return Results.Problem(ex.Message, statusCode: ex.Bloqueado ? 423 : 401);
            }
            catch (ConflitoMarcacaoException ex)
            {
                return Results.Problem(ex.Message, statusCode: 409);
            }
            catch (RegraNegocioException ex)
            {
                return Results.Problem(ex.Message, statusCode: 422);
            }
        });
    }
}
