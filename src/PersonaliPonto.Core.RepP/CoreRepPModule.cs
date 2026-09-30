using Microsoft.Extensions.DependencyInjection;
using PersonaliPonto.Core.RepP.Services;

namespace PersonaliPonto.Core.RepP;

public static class CoreRepPModule
{
    public static IServiceCollection AddCoreRepP(this IServiceCollection services)
    {
        services.AddScoped<AuditService>();
        services.AddScoped<RegistroRepService>();
        services.AddScoped<MarcacaoService>();
        services.AddScoped<CadastroService>();
        services.AddScoped<TratamentoService>();
        services.AddScoped<EspelhoService>();
        services.AddScoped<ArquivosFiscaisService>();
        services.AddScoped<ComprovanteService>();
        services.AddScoped<ArquivoService>();
        return services;
    }
}
