using Microsoft.Maui.Controls.Shapes;
using PersonaliPonto.Mobile.Servicos;

namespace PersonaliPonto.Mobile.Paginas;

/// <summary>Menu do funcionário (referência: ajuste de ponto, abono, assinatura de espelho, histórico, configurações).</summary>
public sealed class MenuPage : ContentPage
{
    private readonly Sessao _sessao;
    private readonly Label _nome = Ui.L("", 18, Colors.White, true);
    private readonly Label _empresa = Ui.L("", 13, Color.FromArgb("#C9D6EA"));
    private readonly Label _iniciais = Ui.L("", 26, Ui.Azul, true, TextAlignment.Center);

    public MenuPage(Sessao sessao)
    {
        _sessao = sessao;
        BackgroundColor = Ui.Fundo;
        _iniciais.VerticalTextAlignment = TextAlignment.Center;

        var cab = new Grid { Background = Ui.Degrade, Padding = new Thickness(20, 52, 20, 24), ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star)], ColumnSpacing = 16 };
        cab.Add(new Border { WidthRequest = 72, HeightRequest = 72, StrokeShape = new Ellipse(), StrokeThickness = 0, BackgroundColor = Colors.White, Content = _iniciais }, 0);
        cab.Add(new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Children = { _nome, _empresa } }, 1);

        var itens = new VerticalStackLayout { Padding = new Thickness(16, 16), Spacing = 10 };
        void Item(string icone, string titulo, string sub, Func<Task> acao)
        {
            var g = new Grid { ColumnDefinitions = [new(44), new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 10 };
            g.Add(new Border
            {
                WidthRequest = 40, HeightRequest = 40, StrokeShape = new Ellipse(), StrokeThickness = 0, BackgroundColor = Ui.FundoAzul,
                Content = Ui.L(icone, 18, Ui.Azul, true, TextAlignment.Center).Also(l => l.VerticalTextAlignment = TextAlignment.Center)
            }, 0);
            g.Add(new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Children = { Ui.L(titulo, 16, Ui.Titulo, true), Ui.L(sub, 12) } }, 1);
            g.Add(Ui.L("›", 24, Ui.Texto).Also(l => l.VerticalOptions = LayoutOptions.Center), 2);
            var cartao = Ui.Cartao(g, new Thickness(12));
            var toque = new TapGestureRecognizer();
            toque.Tapped += async (_, _) => await acao();
            cartao.GestureRecognizers.Add(toque);
            itens.Children.Add(cartao);
        }

        Item("✎", "Ajuste de ponto", "Pedir inclusão de marcação esquecida", () => Shell.Current.GoToAsync("ajuste"));
        Item("✚", "Abono de falta", "Enviar atestado ou justificativa", () => Shell.Current.GoToAsync("abono"));
        Item("✍", "Assinatura de espelho", "Confirmar o espelho do mês", () => Shell.Current.GoToAsync("assinatura"));
        Item("◷", "Histórico de pontos", "Marcações e comprovantes", () => Shell.Current.GoToAsync("historico"));
        Item("$", "Banco de horas", "Saldo e lançamentos", () => Shell.Current.GoToAsync("banco-horas"));
        Item("⚙", "Configurações", "Senha e servidor", () => Shell.Current.GoToAsync("configuracoes"));
        Item("⎋", "Sair", "Encerrar a sessão neste aparelho", SairAsync);

        itens.Children.Add(Ui.L($"PersonaliPonto · versão {AppInfo.Current.VersionString}", 11, h: TextAlignment.Center).Also(l => l.Margin = new Thickness(0, 12)));
        Content = new ScrollView { Content = new VerticalStackLayout { Children = { cab, itens } } };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        var p = _sessao.Perfil;
        _nome.Text = p?.Nome ?? "";
        _empresa.Text = p is null ? "" : $"{p.Empresa}\nMatrícula {p.Matricula}";
        var partes = (p?.Nome ?? "?").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _iniciais.Text = partes.Length > 1 ? $"{partes[0][0]}{partes[^1][0]}" : partes[0][..1];
    }

    private async Task SairAsync()
    {
        if (!await DisplayAlertAsync("Sair", "Encerrar a sessão neste aparelho? Marcações ainda não enviadas continuam guardadas.", "Sair", "Cancelar")) return;
        await _sessao.SairAsync();
        ((App)Application.Current!).IrParaLogin();
    }
}
