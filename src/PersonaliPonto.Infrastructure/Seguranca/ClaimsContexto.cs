using System.Security.Claims;
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

        // Papéis da plataforma operam em modo sistema, exceto quando "entram" num tenant (acesso de suporte auditado).
        var tenant = G(PersonaliPontoClaims.TenantId);
        if (tenant is { } t) ctx.DefinirTenant(t);
        else if (papel is Roles.SuperAdmin or Roles.Suporte) ctx.DefinirSistema();
    }

    public static Guid? FuncionarioId(this ClaimsPrincipal u) =>
        Guid.TryParse(u.FindFirst(PersonaliPontoClaims.FuncionarioId)?.Value, out var g) ? g : null;

    public static Guid? UsuarioId(this ClaimsPrincipal u) =>
        Guid.TryParse(u.FindFirst(PersonaliPontoClaims.UserId)?.Value ?? u.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : null;

    public static Guid? TenantId(this ClaimsPrincipal u) =>
        Guid.TryParse(u.FindFirst(PersonaliPontoClaims.TenantId)?.Value, out var g) ? g : null;
}
