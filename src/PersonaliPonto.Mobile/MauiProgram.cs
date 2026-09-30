using Microsoft.Extensions.Logging;
using PersonaliPonto.Mobile.Paginas;
using PersonaliPonto.Mobile.Servicos;

namespace PersonaliPonto.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		builder.Services.AddSingleton<ApiClient>();
		builder.Services.AddSingleton<RelogioOficial>();
		builder.Services.AddSingleton<FilaMarcacoes>();
		builder.Services.AddSingleton<Sessao>();
		builder.Services.AddTransient<AppShell>();
		builder.Services.AddTransient<LoginPage>();
		builder.Services.AddTransient<InicioPage>();
		builder.Services.AddTransient<EspelhoPage>();
		builder.Services.AddTransient<MenuPage>();
		builder.Services.AddTransient<AjustePage>();
		builder.Services.AddTransient<AbonoPage>();
		builder.Services.AddTransient<AssinaturaPage>();
		builder.Services.AddTransient<HistoricoPage>();
		builder.Services.AddTransient<BancoHorasPage>();
		builder.Services.AddTransient<ConfiguracoesPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
