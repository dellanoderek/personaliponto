using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using PersonaliPonto.Infrastructure.Tenancy;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Infrastructure.Seguranca;

public static class ClaimsContexto
{
    /// <summary>Preenche o contexto da unidade de trabalho a partir do usuário autenticado.</summary>
    public static void Preencher(RequestContext ctx, ClaimsPrincipal? user, string? ip)
    {
        ctx.Ip = ip;
        if (user?.Identity?.IsAuthenticated != true) return;
        Guid? G(string tipo) => Guid.TryParse(user.FindFirst(tipo)?.Value, out var g) ? g : null;

        var papel = user.FindFirst(ClaimTypes.Role)?.Value;
        ctx.DefinirUsuario(G(PersonaliPontoClaims.UserId) ?? G(ClaimTypes.NameIdentifier), user.FindFirst(ClaimTypes.Name)?.Value,
            user.FindFirst("cpf")?.Value, papel, G(PersonaliPontoClaims.FuncionarioId));

        // Papéis da plataforma (Owner) operam em modo sistema, exceto quando "entram" num tenant ou no painel de um
        // canal (acesso de suporte auditado). Usuários de canal nunca são sistema: a carteira é aplicada em seguida
        // por ContextoCanalService (consulta assíncrona ao banco).
        var tenant = G(PersonaliPontoClaims.TenantId);
        var canal = G(PersonaliPontoClaims.CanalId);
        if (tenant is { } t) ctx.DefinirTenant(t);
        else if (canal is null && Roles.EhPlataforma(papel)) ctx.DefinirSistema();
    }

    /// <summary>Preenche o contexto e, para usuários de canal, aplica a carteira visível.</summary>
    public static async Task PreencherAsync(IServiceProvider sp, ClaimsPrincipal? user, string? ip, CancellationToken ct = default)
    {
        var ctx = sp.GetRequiredService<RequestContext>();
        Preencher(ctx, user, ip);
        await sp.GetRequiredService<ContextoCanalService>().AplicarAsync(user, ct);
    }

    public static Guid? CanalId(this ClaimsPrincipal u) =>
        Guid.TryParse(u.FindFirst(PersonaliPontoClaims.CanalId)?.Value, out var g) ? g : null;

    /// <summary>Usuário da plataforma (Owner), considerando o papel original mesmo em modo suporte.</summary>
    public static bool EhPlataforma(this ClaimsPrincipal u) => u.IsInRole(Roles.SuperAdmin) || u.IsInRole(Roles.Suporte);

    public static Guid? FuncionarioId(this ClaimsPrincipal u) =>
        Guid.TryParse(u.FindFirst(PersonaliPontoClaims.FuncionarioId)?.Value, out var g) ? g : null;

    public static Guid? UsuarioId(this ClaimsPrincipal u) =>
        Guid.TryParse(u.FindFirst(PersonaliPontoClaims.UserId)?.Value ?? u.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : null;

    public static Guid? TenantId(this ClaimsPrincipal u) =>
        Guid.TryParse(u.FindFirst(PersonaliPontoClaims.TenantId)?.Value, out var g) ? g : null;
}
