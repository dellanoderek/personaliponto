using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using PersonaliPonto.Infrastructure;
using PersonaliPonto.Infrastructure.Hosting;
using PersonaliPonto.Infrastructure.Seguranca;
using PersonaliPonto.Infrastructure.Tenancy;
using PersonaliPonto.Shared.Contracts;
using PersonaliPonto.Web.Components;
using PersonaliPonto.Web.Servicos;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

builder.Services.AddPersonaliPonto(cfg, rotinasEmSegundoPlano: !cfg.GetValue<bool>("Rotinas:Desabilitar"));
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(o =>
    {
        o.DetailedErrors = builder.Environment.IsDevelopment();
        o.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(10);
    });

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/entrar";
        o.LogoutPath = "/conta/sair";
        o.AccessDeniedPath = "/acesso-negado";
        o.ExpireTimeSpan = TimeSpan.FromHours(10);
        o.SlidingExpiration = true;
        o.Cookie.Name = "tc_sessao";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Politicas.Empresa, p => p.RequireRole(Roles.AdminEmpresa, Roles.RH, Roles.Gestor).RequireClaim(PersonaliPontoClaims.TenantId))
    .AddPolicy(Politicas.Rh, p => p.RequireRole(Roles.AdminEmpresa, Roles.RH).RequireClaim(PersonaliPontoClaims.TenantId))
    .AddPolicy(Politicas.Admin, p => p.RequireRole(Roles.AdminEmpresa).RequireClaim(PersonaliPontoClaims.TenantId))
    .AddPolicy(Politicas.Plataforma, p => p.RequireRole(Roles.SuperAdmin, Roles.Suporte))
    .AddPolicy(Politicas.PlataformaAdmin, p => p.RequireRole(Roles.SuperAdmin))
    .AddPolicy(Politicas.Canal, p => p.RequireRole(Roles.PapeisCanal).RequireClaim(PersonaliPontoClaims.CanalId))
    .AddPolicy(Politicas.CanalAdmin, p => p.RequireRole(Roles.AdminRevendedor, Roles.AdminParceiro).RequireClaim(PersonaliPontoClaims.CanalId))
    .AddPolicy(Politicas.Revendedor, p => p.RequireRole(Roles.PapeisRevendedor).RequireClaim(PersonaliPontoClaims.CanalId))
    .AddPolicy(Politicas.RevendedorAdmin, p => p.RequireRole(Roles.AdminRevendedor).RequireClaim(PersonaliPontoClaims.CanalId))
    .AddPolicy(Politicas.Funcionario, p => p.RequireClaim(PersonaliPontoClaims.FuncionarioId).RequireClaim(PersonaliPontoClaims.TenantId));
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<Notificacoes>();
builder.Services.AddHealthChecks();
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddLocalization();
builder.Services.AddLimiteWebhook(cfg);

var app = builder.Build();
app.UseRequestLocalization(new RequestLocalizationOptions().SetDefaultCulture("pt-BR").AddSupportedCultures("pt-BR").AddSupportedUICultures("pt-BR"));

if (args.Contains("--migrar") || cfg.GetValue<bool>("Banco:MigrarNaInicializacao"))
{
    await Inicializacao.MigrarAsync(app.Services);
    await Inicializacao.SemearAsync(app.Services, cfg, app.Logger);
    if (args.Contains("--migrar")) return;
}

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/erro", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseStatusCodePagesWithReExecute("/nao-encontrado", createScopeForStatusCodePages: true);
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    h["X-Frame-Options"] = "DENY";
    h["Permissions-Policy"] = "geolocation=(self), camera=(), microphone=()";
    await next();
});

app.UseAuthentication();
app.Use(async (ctx, next) =>
{
    await ClaimsContexto.PreencherAsync(ctx.RequestServices, ctx.User, ctx.Connection.RemoteIpAddress?.ToString(), ctx.RequestAborted);
    await next();
});
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapHealthChecks("/health");
app.MapContaEndpoints();
app.MapDownloadEndpoints();
app.MapTerminalEndpoints();
app.MapPagamentosEndpoints();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

public partial class Program;
