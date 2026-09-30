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
        ClaimsContexto.Preencher(Svc<RequestContext>(), Usuario, null);
        if (ContextoPlataforma) Svc<RequestContext>().DefinirSistema();
        _contextoPronto = true;
    }

    /// <summary>Páginas do Super Admin operam sobre todos os clientes, mesmo durante um acesso de suporte.</summary>
    protected virtual bool ContextoPlataforma => false;

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
