using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using PersonaliPonto.Infrastructure.Persistence;
using PersonaliPonto.Infrastructure.Seguranca;
using PersonaliPonto.Infrastructure.Tenancy;
using PersonaliPonto.Web.Servicos;

namespace PersonaliPonto.Web.Components;

/// <summary>
/// Base das páginas interativas: cada página tem seu próprio escopo de DI (DbContext isolado, sem
/// concorrência entre componentes do mesmo circuito) com o contexto de tenant/usuário do login.
/// </summary>
public abstract class PaginaBase : OwningComponentBase
{
    [CascadingParameter] protected Task<AuthenticationState> AuthState { get; set; } = default!;
    [Inject] protected Notificacoes Avisos { get; set; } = default!;
    [Inject] protected NavigationManager Nav { get; set; } = default!;

    protected ClaimsPrincipal Usuario { get; private set; } = new();
    protected bool Ocupado { get; set; }
    private bool _contextoPronto;

    protected T Svc<T>() where T : notnull => ScopedServices.GetRequiredService<T>();
    protected PersonaliPontoDbContext Db => Svc<PersonaliPontoDbContext>();

    protected override async Task OnInitializedAsync()
    {
        await GarantirContextoAsync();
        await CarregarAsync();
    }

    protected async Task GarantirContextoAsync()
    {
        if (_contextoPronto) return;
        Usuario = (await AuthState).User;
        await ClaimsContexto.PreencherAsync(ScopedServices, Usuario, null);
        if (ContextoPlataforma)
        {
            // Modo sistema é exclusivo do Owner: nunca concedido a usuário de canal ou de cliente.
            if (!Usuario.EhPlataforma()) throw new UnauthorizedAccessException("Página exclusiva da plataforma.");
            Svc<RequestContext>().DefinirSistema();
        }
        _contextoPronto = true;
    }

    /// <summary>Páginas do Super Admin operam sobre todos os clientes, mesmo durante um acesso de suporte.</summary>
    protected virtual bool ContextoPlataforma => false;

    protected RequestContext Contexto => Svc<RequestContext>();
    protected Guid? CanalId => Usuario.CanalId();

    /// <summary>Carga inicial da página (substitui OnInitializedAsync nas páginas).</summary>
    protected virtual Task CarregarAsync() => Task.CompletedTask;

    /// <summary>Executa uma ação com indicador de ocupado e tradução de erros para aviso.</summary>
    protected async Task<bool> Executar(Func<Task> acao, string? sucesso = null)
    {
        if (Ocupado) return false;
        Ocupado = true;
        try
        {
            await GarantirContextoAsync();
            await acao();
            if (sucesso is not null) Avisos.Sucesso(sucesso);
            return true;
        }
        catch (Exception ex)
        {
            Db.ChangeTracker.Clear();
            Avisos.Erro(Notificacoes.Mensagem(ex));
            if (ex is not (Core.RepP.Services.RegraNegocioException or Core.RepP.Services.AcessoNegadoException or Core.RepP.Services.NaoEncontradoException))
                Svc<ILogger<PaginaBase>>().LogError(ex, "Erro na página {Pagina}", GetType().Name);
            return false;
        }
        finally
        {
            Ocupado = false;
            StateHasChanged();
        }
    }

    protected bool TemPapel(params string[] papeis) => papeis.Any(Usuario.IsInRole);
    protected Guid? FuncionarioId => Usuario.FuncionarioId();
    protected Guid? TenantId => Usuario.TenantId();

    protected static DateOnly Hoje => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo")).DateTime);
    protected static DateOnly InicioMes(DateOnly d) => new(d.Year, d.Month, 1);
}

/// <summary>Base das páginas do Super Admin (contexto de plataforma).</summary>
public abstract class PaginaPlataforma : PaginaBase
{
    protected override bool ContextoPlataforma => true;
}

/// <summary>
/// Base das páginas do painel de canal (revendedor/parceiro): contexto de canal com a carteira visível
/// (cadastro e financeiro). Dados de ponto/RH dos clientes só via modo suporte auditado.
/// </summary>
public abstract class PaginaCanal : PaginaBase
{
    protected Modules.SaaS.Domain.Canal? Canal { get; private set; }
    protected bool EhRevendedor => Canal?.Tipo == Modules.SaaS.Domain.TipoCanal.Revendedor;
    protected bool EhAdminCanal => TemPapel(Shared.Contracts.Roles.AdminRevendedor, Shared.Contracts.Roles.AdminParceiro);

    /// <summary>Painel bloqueado/suspenso pela régua de canal (D+15/D+30): só pendência e faturas.</summary>
    protected bool PainelBloqueado => Canal?.Status is Modules.SaaS.Domain.StatusCanal.PainelBloqueado or Modules.SaaS.Domain.StatusCanal.Suspenso;

    /// <summary>Páginas que continuam acessíveis com o painel bloqueado (pendência e faturas).</summary>
    protected virtual bool PermitidaComPainelBloqueado => false;

    public const string PaginaPendencia = "/canal/minha-conta";

    protected override async Task OnInitializedAsync()
    {
        await GarantirContextoAsync();
        if (CanalId is { } id) Canal = await Svc<Modules.SaaS.Services.CanalService>().ObterAsync(id, default);
        if (PainelBloqueado && !PermitidaComPainelBloqueado)
        {
            Nav.NavigateTo(PaginaPendencia);
            return;
        }
        await CarregarAsync();
    }
}
