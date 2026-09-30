using PersonaliPonto.Mobile.Paginas;

namespace PersonaliPonto.Mobile;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute("ajuste", typeof(AjustePage));
		Routing.RegisterRoute("abono", typeof(AbonoPage));
		Routing.RegisterRoute("assinatura", typeof(AssinaturaPage));
		Routing.RegisterRoute("historico", typeof(HistoricoPage));
		Routing.RegisterRoute("banco-horas", typeof(BancoHorasPage));
		Routing.RegisterRoute("configuracoes", typeof(ConfiguracoesPage));
		CurrentItem = Items[0].Items[1]; // abre em "Início"
	}
}
