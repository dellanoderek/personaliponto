using Microsoft.Extensions.DependencyInjection;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Modules.SaaS.Services;

namespace PersonaliPonto.Modules.SaaS;

public static class SaasModule
{
    public static IServiceCollection AddModuloSaas(this IServiceCollection services)
    {
        services.AddScoped<ClienteService>();
        services.AddScoped<FaturamentoService>();
        services.AddScoped<TenantUsoPolicy>();
        services.AddScoped<IMarcacaoInterceptor, TenantUsoInterceptor>();
        services.AddScoped<ImportacaoPlanilhaService>();
        services.AddScoped<PainelPlataformaService>();
        return services;
    }
}
