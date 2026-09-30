using PersonaliPonto.Core.RepP.Abstractions;

namespace PersonaliPonto.Infrastructure.Tenancy;

/// <summary>Contexto de tenant/usuário da unidade de trabalho (scoped). Preenchido pela autenticação.</summary>
public sealed class RequestContext : ITenantContext, ICurrentUser
{
    public Guid? TenantId { get; private set; }
    public bool IsSystem { get; private set; }
    public Guid? UserId { get; private set; }
    public string? Nome { get; private set; }
    public string? Cpf { get; private set; }
    public string? Ip { get; set; }
    public string? Papel { get; private set; }
    public Guid? FuncionarioId { get; private set; }

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

    public void DefinirUsuario(Guid? userId, string? nome, string? cpf, string? papel, Guid? funcionarioId)
    {
        UserId = userId;
        Nome = nome;
        Cpf = cpf;
        Papel = papel;
        FuncionarioId = funcionarioId;
    }

    /// <summary>Escopo de plataforma (Super Admin, login, rotinas). Ignora o isolamento por tenant.</summary>
    public void DefinirSistema()
    {
        TenantId = null;
        IsSystem = true;
    }

    public void Limpar()
    {
        TenantId = null;
        IsSystem = false;
    }

    public IDisposable ComoSistema()
    {
        var (t, s) = (TenantId, IsSystem);
        DefinirSistema();
        return new Restaurar(() => { TenantId = t; IsSystem = s; });
    }

    private sealed class Restaurar(Action a) : IDisposable
    {
        public void Dispose() => a();
    }
}
