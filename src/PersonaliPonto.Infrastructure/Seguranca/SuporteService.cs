using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Infrastructure.Persistence;
using PersonaliPonto.Infrastructure.Tenancy;
using PersonaliPonto.Modules.SaaS.Domain;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Infrastructure.Seguranca;

/// <summary>
/// Modo suporte auditado, em qualquer nível: plataforma → cliente/canal, revendedor → cliente da carteira
/// (inclusive de parceiros), parceiro → cliente próprio. Sempre com motivo, registrado com horário e IP,
/// e sempre com checagem de descendência (o alvo precisa estar na hierarquia de quem inicia).
/// </summary>
public sealed class SuporteService(PersonaliPontoDbContext db, RequestContext ctx, IClock clock, AuditService audit)
{
    public const string ClaimCliente = "suporte_cliente";
    public const string ClaimCanal = "suporte_canal";

    private static void ValidarMotivo(string? motivo)
    {
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 10)
            throw new RegraNegocioException("Informe o motivo do acesso (mínimo 10 caracteres).");
    }

    /// <summary>Nível de quem inicia o acesso, conforme o papel.</summary>
    public static NivelSuporte Nivel(string? papel) => papel switch
    {
        Roles.SuperAdmin or Roles.Suporte => NivelSuporte.Plataforma,
        Roles.AdminRevendedor or Roles.SuporteRevendedor => NivelSuporte.Revendedor,
        Roles.AdminParceiro or Roles.SuporteParceiro => NivelSuporte.Parceiro,
        _ => throw new AcessoNegadoException("Perfil sem permissão de suporte.")
    };

    /// <summary>Inicia o acesso ao ambiente de um cliente e devolve as claims da sessão de suporte.</summary>
    public async Task<List<Claim>> IniciarClienteAsync(ClaimsPrincipal user, Guid tenantId, string motivo, CancellationToken ct)
    {
        ValidarMotivo(motivo);
        var papel = PapelOriginal(user);
        var nivel = Nivel(papel);
        if (nivel == NivelSuporte.Plataforma) ctx.DefinirSistema();
        else if (ctx.CanalId is null || ctx.IsSystem) throw new AcessoNegadoException("Contexto de canal não resolvido.");

        // Checagem de descendência: fora do modo sistema, o filtro/RLS só encontra clientes da carteira.
        if (!ctx.VeTenant(tenantId)) throw new NaoEncontradoException("Cliente não encontrado.");
        var t = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId, ct) ?? throw new NaoEncontradoException("Cliente não encontrado.");

        var acesso = new AcessoSuporte
        {
            // Visibilidade segue a origem: acessos da plataforma ficam com o Owner como canal de origem (mesmo quando o Owner
            // está no painel de um canal em modo suporte), para que revendedores/parceiros não os vejam.
            TenantId = tenantId, CanalId = nivel == NivelSuporte.Plataforma ? Canal.OwnerRaizId : ctx.CanalId, NivelOrigem = nivel, UsuarioId = user.UsuarioId()!.Value,
            UsuarioNome = user.Identity?.Name ?? "", Motivo = motivo.Trim(), Inicio = clock.UtcNow, Ip = ctx.Ip
        };
        db.AcessosSuporte.Add(acesso);
        audit.Registrar("suporte.acesso_iniciado", nameof(Tenant), tenantId, new { motivo, nivel = nivel.ToString() }, tenantId);
        await db.SaveChangesAsync(ct);

        var claims = user.Claims.Where(c => c.Type is not (PersonaliPontoClaims.TenantId or ClaimTypes.Role or PersonaliPontoClaims.SuporteAcesso
            or ClaimCliente or ClaimCanal)).ToList();
        // Decisão: no cliente o suporte opera como AdminEmpresa (N1/N2 precisa corrigir cadastro/jornada do cliente);
        // tudo fica auditado com o acesso de suporte e o motivo.
        claims.Add(new Claim(ClaimTypes.Role, Roles.AdminEmpresa));
        claims.Add(new Claim(ClaimTypes.Role, papel!));
        claims.Add(new Claim(PersonaliPontoClaims.TenantId, tenantId.ToString()));
        claims.Add(new Claim(PersonaliPontoClaims.SuporteAcesso, acesso.Id.ToString()));
        claims.Add(new Claim(ClaimCliente, t.Nome));
        return claims;
    }

    /// <summary>Owner entra no painel de um revendedor/parceiro (visão do canal, sem dados de ponto).</summary>
    public async Task<List<Claim>> IniciarCanalAsync(ClaimsPrincipal user, Guid canalId, string motivo, CancellationToken ct)
    {
        ValidarMotivo(motivo);
        var papel = PapelOriginal(user);
        if (Nivel(papel) != NivelSuporte.Plataforma) throw new AcessoNegadoException("Somente a plataforma acessa o painel de um canal.");
        ctx.DefinirSistema();
        var canal = await db.Canais.AsNoTracking().FirstOrDefaultAsync(c => c.Id == canalId, ct) ?? throw new NaoEncontradoException("Canal não encontrado.");
        if (canal.Tipo == TipoCanal.Owner) throw new RegraNegocioException("Selecione um revendedor ou parceiro.");

        var acesso = new AcessoSuporte
        {
            CanalAlvoId = canalId, CanalId = Canal.OwnerRaizId, NivelOrigem = NivelSuporte.Plataforma, UsuarioId = user.UsuarioId()!.Value,
            UsuarioNome = user.Identity?.Name ?? "", Motivo = motivo.Trim(), Inicio = clock.UtcNow, Ip = ctx.Ip
        };
        db.AcessosSuporte.Add(acesso);
        audit.Registrar("suporte.acesso_canal_iniciado", nameof(Canal), canalId, new { motivo });
        await db.SaveChangesAsync(ct);

        var claims = user.Claims.Where(c => c.Type is not (PersonaliPontoClaims.TenantId or PersonaliPontoClaims.CanalId or ClaimTypes.Role
            or PersonaliPontoClaims.SuporteAcesso or ClaimCliente or ClaimCanal)).ToList();
        // Super Admin entra como administrador do canal; o Suporte da plataforma, como suporte do canal (sem gestão).
        claims.Add(new Claim(ClaimTypes.Role, PapelNoCanal(papel!, canal.Tipo)));
        claims.Add(new Claim(ClaimTypes.Role, papel!));
        claims.Add(new Claim(PersonaliPontoClaims.CanalId, canalId.ToString()));
        claims.Add(new Claim(PersonaliPontoClaims.SuporteAcesso, acesso.Id.ToString()));
        claims.Add(new Claim(ClaimCanal, canal.NomeMarca));
        return claims;
    }

    /// <summary>Encerra o acesso de suporte em andamento e devolve as claims originais do usuário.</summary>
    public async Task<(List<Claim> Claims, string Papel)> EncerrarAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var id = user.UsuarioId() ?? throw new AcessoNegadoException("Sessão inválida.");
        // O registro pode ter sido criado pela plataforma (visível só em modo sistema) ou pelo canal do usuário.
        // Fora do modo sistema ninguém altera acessos_suporte (RLS só SELECT/INSERT): o encerramento é uma rotina
        // controlada, restrita ao registro do próprio usuário.
        using var _ = ctx.ComoSistema();
        if (Guid.TryParse(user.FindFirst(PersonaliPontoClaims.SuporteAcesso)?.Value, out var acessoId))
        {
            var a = await db.AcessosSuporte.FirstOrDefaultAsync(x => x.Id == acessoId && x.UsuarioId == id, ct);
            if (a is not null && a.Fim is null)
            {
                a.Fim = clock.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }
        Usuario u;
        using (ctx.ComoSistema()) u = await db.Usuarios.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        return (AuthService.Claims(u), u.Papel);
    }

    /// <summary>Papel concedido no painel do canal ao usuário da plataforma em modo suporte.</summary>
    public static string PapelNoCanal(string papelPlataforma, TipoCanal tipo) => (papelPlataforma, tipo) switch
    {
        (Roles.SuperAdmin, TipoCanal.Revendedor) => Roles.AdminRevendedor,
        (Roles.SuperAdmin, _) => Roles.AdminParceiro,
        (_, TipoCanal.Revendedor) => Roles.SuporteRevendedor,
        _ => Roles.SuporteParceiro
    };

    /// <summary>Papel real do usuário (em modo suporte, o último papel adicionado é o original).</summary>
    public static string? PapelOriginal(ClaimsPrincipal user) => user.FindAll(ClaimTypes.Role).LastOrDefault()?.Value;
}
