using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PersonaliPonto.Infrastructure.Persistence;
using PersonaliPonto.Modules.SaaS.Domain;
using PersonaliPonto.Modules.SaaS.Services;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Infrastructure.Tenancy;

/// <summary>Carteira de um canal: o próprio canal + descendentes e os clientes cujo canal dono está nesse conjunto.</summary>
public sealed record CarteiraCanal(Canal Canal, IReadOnlyList<Guid> Canais, IReadOnlyList<Guid> Tenants);

/// <summary>
/// Resolve, a cada requisição/escopo de um usuário de canal, a hierarquia visível (caminho materializado)
/// e aplica no <see cref="RequestContext"/> (que o interceptor repassa ao PostgreSQL para o RLS).
/// A consulta roda em modo sistema de forma controlada (rotina de infraestrutura), nunca exposta ao chamador.
/// </summary>
public sealed class ContextoCanalService(PersonaliPontoDbContext db, RequestContext ctx)
{
    public async Task<CarteiraCanal?> CarteiraAsync(Guid canalId, CancellationToken ct)
    {
        using var _ = ctx.ComoSistema();
        var canal = await db.Canais.AsNoTracking().FirstOrDefaultAsync(c => c.Id == canalId, ct);
        if (canal is null) return null;
        var canais = await db.Canais.AsNoTracking().Where(c => c.Caminho.StartsWith(canal.Caminho)).Select(c => c.Id).ToListAsync(ct);
        var tenants = await db.Tenants.AsNoTracking().Where(t => canais.Contains(t.CanalDonoId)).Select(t => t.Id).ToListAsync(ct);
        return new CarteiraCanal(canal, canais, tenants);
    }

    /// <summary>
    /// Aplica o escopo de canal do usuário autenticado (claim canal_id). Em modo suporte (claim tenant_id),
    /// revalida que o cliente continua na carteira; caso contrário, o acesso ao tenant é retirado.
    /// </summary>
    public async Task AplicarAsync(ClaimsPrincipal? user, CancellationToken ct = default)
    {
        if (user?.Identity?.IsAuthenticated != true) return;
        if (!Guid.TryParse(user.FindFirst(PersonaliPontoClaims.CanalId)?.Value, out var canalId)) return;
        var tenantSuporte = ctx.TenantId;
        var carteira = await CarteiraAsync(canalId, ct);
        if (carteira is null)
        {
            ctx.Limpar();
            return;
        }
        ctx.DefinirCanal(canalId, carteira.Canais, carteira.Tenants);
        ctx.DefinirSituacaoCanal(carteira.Canal.Status);
        if (tenantSuporte is { } t && !carteira.Tenants.Contains(t))
        {
            ctx.Limpar();
            ctx.DefinirCanal(canalId, carteira.Canais, carteira.Tenants);
            ctx.DefinirSituacaoCanal(carteira.Canal.Status);
        }
    }
}

/// <summary>Unicidade global (sem retornar dados): consultas pontuais em modo sistema.</summary>
public sealed class UnicidadeGlobal(PersonaliPontoDbContext db, RequestContext ctx) : IUnicidadeGlobal
{
    public async Task<bool> EmailEmUsoAsync(string email, CancellationToken ct)
    {
        using var _ = ctx.ComoSistema();
        return await db.Usuarios.AnyAsync(u => u.Email == email, ct);
    }

    public async Task<bool> CnpjClienteAtivoAsync(string cnpj, CancellationToken ct)
    {
        using var _ = ctx.ComoSistema();
        return await db.Assinaturas.AnyAsync(a => a.Cnpj == cnpj && a.CanceladaEm == null, ct);
    }

    public async Task<bool> SlugTenantEmUsoAsync(string slug, CancellationToken ct)
    {
        using var _ = ctx.ComoSistema();
        return await db.Tenants.AnyAsync(t => t.Slug == slug, ct);
    }

    public async Task<bool> SlugCanalEmUsoAsync(string slug, CancellationToken ct)
    {
        using var _ = ctx.ComoSistema();
        return await db.Canais.AnyAsync(c => c.Slug == slug, ct);
    }

    public async Task<bool> CnpjCanalEmUsoAsync(string cnpj, CancellationToken ct)
    {
        using var _ = ctx.ComoSistema();
        return await db.Canais.AnyAsync(c => c.Cnpj == cnpj, ct);
    }
}
