using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;
using PersonaliPonto.Core.RepP;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Infrastructure.Arquivos;
using PersonaliPonto.Infrastructure.Documentos;
using PersonaliPonto.Infrastructure.Persistence;
using PersonaliPonto.Infrastructure.Rotinas;
using PersonaliPonto.Infrastructure.Seguranca;
using PersonaliPonto.Infrastructure.Tempo;
using PersonaliPonto.Infrastructure.Tenancy;
using PersonaliPonto.Modules.Analytics;
using PersonaliPonto.Modules.RH;
using PersonaliPonto.Modules.SaaS;
using PersonaliPonto.Modules.SaaS.Services;

namespace PersonaliPonto.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Connection string: "ConnectionStrings:PersonaliPonto" ou variável de ambiente PERSONALIPONTO_DB.
    /// </summary>
    public static string ConnectionString(IConfiguration cfg) =>
        Environment.GetEnvironmentVariable("PERSONALIPONTO_DB")
        ?? cfg.GetConnectionString("PersonaliPonto")
        ?? throw new InvalidOperationException("Connection string não configurada (PERSONALIPONTO_DB ou ConnectionStrings:PersonaliPonto).");

    public static IServiceCollection AddPersonaliPonto(this IServiceCollection services, IConfiguration cfg, bool rotinasEmSegundoPlano = true)
    {
        var cs = ConnectionString(cfg);
        var papel = cfg["Banco:PapelAplicacao"] ?? TenantSessionInterceptor.PapelPadrao;
        var interceptor = new TenantSessionInterceptor(papel);

        services.AddScoped<RequestContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<RequestContext>());
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<RequestContext>());

        var dataSource = new NpgsqlDataSourceBuilder(cs).Build();
        services.AddSingleton(dataSource);
        services.AddDbContext<PersonaliPontoDbContext>(o => o
            .UseNpgsql(dataSource, n => n.MigrationsHistoryTable("__EFMigrationsHistory", PersonaliPontoDbContext.Schema))
            .AddInterceptors(interceptor));
        services.AddScoped<IRepPDbContext>(sp => sp.GetRequiredService<PersonaliPontoDbContext>());
        services.AddScoped<IRhDbContext>(sp => sp.GetRequiredService<PersonaliPontoDbContext>());
        services.AddScoped<ISaasDbContext>(sp => sp.GetRequiredService<PersonaliPontoDbContext>());
        services.AddScoped<INsrAllocator, NsrAllocator>();

        // Dados internos (Data Protection, chaves do servidor): schema próprio, sem o papel multi-tenant.
        services.AddDbContextFactory<SistemaDbContext>(o => o.UseNpgsql(dataSource,
            n => n.MigrationsHistoryTable("__EFMigrationsHistory", SistemaDbContext.Schema)));
        services.AddDataProtection().SetApplicationName("PersonaliPonto").PersistKeysToDbContext<SistemaDbContext>();
        services.AddSingleton<ChaveTerminal>();

        services.Configure<RepPOptions>(cfg.GetSection("RepP"));
        services.Configure<NtpOptions>(cfg.GetSection("Ntp"));
        services.Configure<AssinaturaOptions>(cfg.GetSection("Assinatura"));
        services.Configure<StorageOptions>(cfg.GetSection("Storage"));
        services.Configure<JwtOptions>(cfg.GetSection("Jwt"));

        services.AddSingleton<NtpClock>();
        services.AddSingleton<IClock>(sp => sp.GetRequiredService<NtpClock>());
        services.AddSingleton<AssinaturaDigital>();
        services.AddSingleton<IAssinaturaDigital>(sp => sp.GetRequiredService<AssinaturaDigital>());
        services.AddSingleton<IComprovanteRenderer, ComprovanteRenderer>();
        services.AddSingleton<EspelhoPdfRenderer>();
        services.AddSingleton<IImageProcessor, SkiaImageProcessor>();

        if (string.Equals(cfg["Storage:Provedor"], "supabase", StringComparison.OrdinalIgnoreCase))
            services.AddHttpClient<IFileStorage, SupabaseStorage>();
        else
            services.AddSingleton<IFileStorage, LocalFileStorage>();

        // Ordem importa: a política comercial (SaaS) é avaliada antes da geofence (RH).
        services.AddCoreRepP();
        services.AddModuloSaas();
        services.AddModuloRh();
        services.AddModuloAnalytics();

        services.AddScoped<ContextoCanalService>();
        services.AddScoped<ApuracaoCanalService>();
        services.AddScoped<SituacaoCanalCliente>();
        services.AddScoped<IUnicidadeGlobal, UnicidadeGlobal>();
        services.AddScoped<SuporteService>();
        services.AddScoped<AuthService>();
        services.AddScoped<UsuarioService>();
        services.AddScoped<TerminalService>();
        services.AddSingleton<OutboxProcessor>();

        // Pagamentos (E3/E9): Asaas da plataforma e contas próprias dos revendedores Premium.
        services.Configure<Pagamentos.AsaasOptions>(cfg.GetSection("Asaas"));
        services.AddHttpClient<Pagamentos.IGatewayPagamento, Pagamentos.AsaasGateway>();
        services.AddSingleton<Pagamentos.CofreChavesGateway>();
        services.AddSingleton<Pagamentos.CredenciaisGateway>();
        services.AddScoped<Pagamentos.ContaGatewayService>();
        services.AddScoped<Pagamentos.ComprasCanalService>();
        services.AddScoped<Pagamentos.WebhookAsaasService>();
        services.AddScoped<Pagamentos.VarreduraCobrancasGateway>();
        services.AddScoped<Pagamentos.CobrancaGatewayHandler>();
        services.AddScoped<IOutboxHandler>(sp => sp.GetRequiredService<Pagamentos.CobrancaGatewayHandler>());
        services.AddScoped<IOutboxHandler, Pagamentos.EventoGatewayHandler>();

        if (rotinasEmSegundoPlano)
        {
            services.AddHostedService<NtpSyncService>();
            services.AddHostedService(sp => sp.GetRequiredService<OutboxProcessor>());
            services.AddHostedService<RotinasPlataforma>();
        }
        return services;
    }
}

/// <summary>Usado pelo "dotnet ef" para gerar migrations.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<PersonaliPontoDbContext>
{
    public PersonaliPontoDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("PERSONALIPONTO_DB") ?? "Host=localhost;Port=54329;Database=personaliponto;Username=postgres;Password=devpass";
        var o = new DbContextOptionsBuilder<PersonaliPontoDbContext>()
            .UseNpgsql(cs, n => n.MigrationsHistoryTable("__EFMigrationsHistory", PersonaliPontoDbContext.Schema))
            .Options;
        var ctx = new RequestContext();
        ctx.DefinirSistema();
        return new PersonaliPontoDbContext(o, ctx);
    }
}
