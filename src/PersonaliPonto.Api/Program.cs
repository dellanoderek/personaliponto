using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using PersonaliPonto.Api;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Infrastructure;
using PersonaliPonto.Infrastructure.Hosting;
using PersonaliPonto.Infrastructure.Seguranca;
using PersonaliPonto.Infrastructure.Tenancy;
using PersonaliPonto.Shared.Contracts;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

var chave = cfg["Jwt:Chave"];
if (string.IsNullOrWhiteSpace(chave) || Encoding.UTF8.GetByteCount(chave) < 32)
    throw new InvalidOperationException("Configure Jwt:Chave com pelo menos 32 bytes (variável de ambiente Jwt__Chave).");

builder.Services.AddPersonaliPonto(cfg, rotinasEmSegundoPlano: !cfg.GetValue<bool>("Rotinas:Desabilitar"));
builder.Services.AddProblemDetails();
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = cfg["Jwt:Emissor"] ?? "personaliponto",
        ValidAudience = cfg["Jwt:Audiencia"] ?? "personaliponto",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(chave)),
        ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        RoleClaimType = System.Security.Claims.ClaimTypes.Role,
        NameClaimType = System.Security.Claims.ClaimTypes.Name
    };
});
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("Funcionario", p => p.RequireClaim(PersonaliPontoClaims.FuncionarioId).RequireClaim(PersonaliPontoClaims.TenantId));
    o.AddPolicy("Gestao", p => p.RequireRole(Roles.AdminEmpresa, Roles.RH, Roles.Gestor));
});

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("marcacao", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.FindFirst(PersonaliPontoClaims.UserId)?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));
});
builder.Services.AddHealthChecks();

var app = builder.Build();

if (args.Contains("--migrar") || cfg.GetValue<bool>("Banco:MigrarNaInicializacao"))
{
    await Inicializacao.MigrarAsync(app.Services);
    await Inicializacao.SemearAsync(app.Services, cfg, app.Logger);
    if (args.Contains("--migrar")) return;
}

app.UseForwardedHeaders();
app.UseExceptionHandler(e => e.Run(ErrosApi.TratarAsync));
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});
app.UseAuthentication();
app.Use(async (ctx, next) =>
{
    ClaimsContexto.Preencher(ctx.RequestServices.GetRequiredService<RequestContext>(), ctx.User, ctx.Connection.RemoteIpAddress?.ToString());
    await next();
});
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Ok(new { servico = "PersonaliPonto API", versao = typeof(Program).Assembly.GetName().Version?.ToString() }));
app.MapAuthEndpoints();
app.MapFuncionarioEndpoints();

app.Run();

public partial class Program;
