using PersonaliPonto.Mobile.Paginas;
using PersonaliPonto.Mobile.Servicos;

namespace PersonaliPonto.Mobile;

public partial class App : Application
{
	private readonly IServiceProvider _sp;

	public App(IServiceProvider sp)
	{
		InitializeComponent();
		UserAppTheme = AppTheme.Light;
		_sp = sp;
		sp.GetRequiredService<ApiClient>().SessaoEncerrada += () => MainThread.BeginInvokeOnMainThread(IrParaLogin);
		Connectivity.Current.ConnectivityChanged += async (_, e) =>
		{
			if (e.NetworkAccess == NetworkAccess.Internet) await sp.GetRequiredService<Sessao>().SincronizarHoraAsync();
		};
	}

	protected override Window CreateWindow(IActivationState? activationState) =>
		new(new ContentPage { BackgroundColor = Colors.White, Content = new ActivityIndicator { IsRunning = true, Color = Color.FromArgb("#1166D6"), VerticalOptions = LayoutOptions.Center } })
		{
			Title = "PersonaliPonto"
		};

	protected override async void OnStart()
	{
		var api = _sp.GetRequiredService<ApiClient>();
		if (await api.RestaurarAsync() && api.Autenticado)
		{
			await _sp.GetRequiredService<Sessao>().CarregarAsync();
			IrParaShell();
		}
		else IrParaLogin();
	}

	public void IrParaShell() => Windows[0].Page = _sp.GetRequiredService<AppShell>();

	public void IrParaLogin() => Windows[0].Page = _sp.GetRequiredService<LoginPage>();
}
