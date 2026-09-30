using Microsoft.Extensions.DependencyInjection;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Modules.RH.Services;

namespace PersonaliPonto.Modules.RH;

public static class RhModule
{
    public static IServiceCollection AddModuloRh(this IServiceCollection services)
    {
        services.AddScoped<SolicitacaoService>();
        services.AddScoped<FechamentoService>();
        services.AddScoped<BancoHorasService>();
        services.AddScoped<AtestadoService>();
        services.AddScoped<FeriadoService>();
        services.AddScoped<EscalaService>();
        services.AddScoped<AssinaturaEspelhoService>();
        services.AddScoped<IApuracaoComplementos, RhApuracaoComplementos>();
        services.AddScoped<IMarcacaoInterceptor, GeofenceInterceptor>();
        services.AddScoped<DispositivoInterceptor>();
        services.AddScoped<IMarcacaoInterceptor>(sp => sp.GetRequiredService<DispositivoInterceptor>());
        services.AddScoped<ConfiguracaoSegurancaService>();
        services.AddScoped<DispositivoService>();
        services.AddScoped<FotoMarcacaoService>();
        services.AddScoped<AnaliseFraudeService>();
        return services;
    }
}
