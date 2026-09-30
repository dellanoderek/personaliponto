using PersonaliPonto.Core.RepP.Abstractions;

namespace PersonaliPonto.Infrastructure.Tenancy;

/// <summary>
/// Contexto de tenant/canal/usuário da unidade de trabalho (scoped). Preenchido pela autenticação.
/// Três modos (não exclusivos): sistema (Owner/rotinas), tenant (usuário de cliente ou modo suporte) e
/// canal (revendedor/parceiro — vê o cadastro/financeiro da própria carteira e dos canais descendentes).
/// </summary>
public sealed class RequestContext : ITenantContext, ICurrentUser
{
    private static readonly IReadOnlyList<Guid> Vazio = [];

    public Guid? TenantId { get; private set; }
    public bool IsSystem { get; private set; }
    public Guid? CanalId { get; private set; }
    public IReadOnlyList<Guid> CanaisVisiveis { get; private set; } = Vazio;
    public IReadOnlyList<Guid> TenantsCanal { get; private set; } = Vazio;
    public Guid? UserId { get; private set; }
    public string? Nome { get; private set; }
    public string? Cpf { get; private set; }
    public string? Ip { get; set; }
    public string? Papel { get; private set; }
    public Guid? FuncionarioId { get; private set; }

    /// <summary>Situação comercial do canal do usuário (régua de canal, E2). Nulo fora do escopo de canal.</summary>
    public Modules.SaaS.Domain.StatusCanal? SituacaoCanal { get; private set; }

    /// <summary>Painel do canal bloqueado (D+15/D+30): o usuário de canal só consulta pendência e faturas, sem gravar.</summary>
    public bool PainelCanalBloqueado => CanalId is not null &&
        SituacaoCanal is Modules.SaaS.Domain.StatusCanal.PainelBloqueado or Modules.SaaS.Domain.StatusCanal.Suspenso;

    public void DefinirSituacaoCanal(Modules.SaaS.Domain.StatusCanal? status) => SituacaoCanal = status;

    /// <summary>Escopo exclusivo para aplicar migrations (DDL): não assume o papel restrito da aplicação.</summary>
    public bool Migracao { get; private set; }

    public void DefinirMigracao()
    {
        DefinirSistema();
        Migracao = true;
    }

    public void DefinirTenant(Guid tenantId)
    {
        TenantId = tenantId;
        IsSystem = false;
    }

    /// <summary>
    /// Escopo de canal: o usuário enxerga o próprio canal e os descendentes (<paramref name="canaisVisiveis"/>)
    /// e o cadastro/financeiro dos clientes da carteira (<paramref name="tenantsVisiveis"/>). Nunca é sistema.
    /// </summary>
    public void DefinirCanal(Guid canalId, IEnumerable<Guid> canaisVisiveis, IEnumerable<Guid> tenantsVisiveis)
    {
        var canais = canaisVisiveis.Distinct().ToList();
        if (!canais.Contains(canalId)) canais.Insert(0, canalId);
        CanalId = canalId;
        CanaisVisiveis = canais.AsReadOnly();
        TenantsCanal = tenantsVisiveis.Distinct().ToList().AsReadOnly();
        IsSystem = false;
    }

    public bool VeTenant(Guid tenantId) => IsSystem || TenantId == tenantId || TenantsCanal.Contains(tenantId);

    public bool VeCanal(Guid canalId) => IsSystem || CanaisVisiveis.Contains(canalId);

    public void DefinirUsuario(Guid? userId, string? nome, string? cpf, string? papel, Guid? funcionarioId)
    {
        UserId = userId;
        Nome = nome;
        Cpf = cpf;
        Papel = papel;
        FuncionarioId = funcionarioId;
    }

    /// <summary>
    /// Escopo de plataforma (Owner: Super Admin/Suporte, login e rotinas). Ignora o isolamento.
    /// Nunca deve ser usado a partir de um usuário de canal ou de tenant.
    /// </summary>
    public void DefinirSistema()
    {
        TenantId = null;
        IsSystem = true;
    }

    public void Limpar()
    {
        TenantId = null;
        IsSystem = false;
        CanalId = null;
        CanaisVisiveis = Vazio;
        TenantsCanal = Vazio;
        SituacaoCanal = null;
    }

    /// <summary>Uso interno da infraestrutura (login, resolução de carteira, rotinas): restaura o contexto ao final.</summary>
    public IDisposable ComoSistema()
    {
        var (t, s) = (TenantId, IsSystem);
        DefinirSistema();
        return new Restaurar(() => { TenantId = t; IsSystem = s; });
    }

    public IDisposable ComoTenant(Guid tenantId)
    {
        if (!VeTenant(tenantId)) throw new UnauthorizedAccessException("Cliente fora da carteira do canal.");
        var (t, s) = (TenantId, IsSystem);
        TenantId = tenantId;
        return new Restaurar(() => { TenantId = t; IsSystem = s; });
    }

    public void IncluirTenantCanal(Guid tenantId, Guid canalDonoId)
    {
        if (IsSystem) return;
        if (!CanaisVisiveis.Contains(canalDonoId)) throw new UnauthorizedAccessException("Canal fora da hierarquia do usuário.");
        if (!TenantsCanal.Contains(tenantId)) TenantsCanal = TenantsCanal.Append(tenantId).ToList().AsReadOnly();
    }

    public void IncluirCanalDescendente(Guid canalId, Guid canalPaiId)
    {
        if (IsSystem) return;
        if (!CanaisVisiveis.Contains(canalPaiId)) throw new UnauthorizedAccessException("Canal fora da hierarquia do usuário.");
        if (!CanaisVisiveis.Contains(canalId)) CanaisVisiveis = CanaisVisiveis.Append(canalId).ToList().AsReadOnly();
    }

    private sealed class Restaurar(Action a) : IDisposable
    {
        public void Dispose() => a();
    }
}
