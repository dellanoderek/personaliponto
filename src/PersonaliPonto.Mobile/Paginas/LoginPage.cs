using Microsoft.Maui.Controls.Shapes;
using PersonaliPonto.Mobile.Servicos;

namespace PersonaliPonto.Mobile.Paginas;

public sealed class LoginPage : ContentPage
{
    private readonly ApiClient _api;
    private readonly Sessao _sessao;
    private readonly Entry _login = Ui.Campo("CPF ou e-mail", Keyboard.Email);
    private readonly Entry _senha = Ui.Campo("Senha", senha: true);
    private readonly Entry _codigo = Ui.Campo("Código do autenticador", Keyboard.Numeric);
    private readonly View _campoCodigo;
    private readonly Button _entrar = Ui.Botao("Entrar");
    private readonly ActivityIndicator _carregando = new() { Color = Ui.Azul };

    public LoginPage(ApiClient api, Sessao sessao)
    {
        _api = api;
        _sessao = sessao;
        BackgroundColor = Ui.Fundo;
        _campoCodigo = Ui.CampoComRotulo("Verificação em duas etapas", _codigo);
        _campoCodigo.IsVisible = false;
        _entrar.Clicked += async (_, _) => await EntrarAsync();
        _senha.Completed += async (_, _) => await EntrarAsync();

        var config = new Button { Text = "Configurar servidor", TextColor = Ui.Texto, BackgroundColor = Colors.Transparent, FontSize = 12 };
        config.Clicked += async (_, _) =>
        {
            var url = await DisplayPromptAsync("Servidor", "Endereço da API PersonaliPonto", initialValue: ApiClient.Servidor, keyboard: Keyboard.Url);
            if (!string.IsNullOrWhiteSpace(url)) ApiClient.Servidor = url.Trim();
        };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Children =
                {
                    new Grid
                    {
                        Background = Ui.Degrade, Padding = new Thickness(24, 64, 24, 48),
                        Children =
                        {
                            new VerticalStackLayout
                            {
                                Spacing = 16,
                                Children =
                                {
                                    new Border
                                    {
                                        BackgroundColor = Colors.White, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 18 },
                                        Padding = new Thickness(18, 12), HorizontalOptions = LayoutOptions.Start,
                                        Content = new Image { Source = "logo.png", HeightRequest = 64 }
                                    },
                                    Ui.L("PLATAFORMA INTELIGENTE", 12, Ui.Ciano, true),
                                    Ui.L("DE GESTÃO DE JORNADA", 24, Colors.White, true),
                                    Ui.L("Registre seu ponto, acompanhe sua jornada e seus comprovantes.", 14, Color.FromArgb("#D6D1C4"))
                                }
                            }
                        }
                    },
                    new VerticalStackLayout
                    {
                        Padding = new Thickness(24, 28), Spacing = 14,
                        Children =
                        {
                            Ui.L("Acesse sua conta", 22, Ui.Titulo, true),
                            Ui.CampoComRotulo("CPF ou e-mail", _login),
                            Ui.CampoComRotulo("Senha", _senha),
                            _campoCodigo,
                            _entrar,
                            _carregando,
                            Ui.L("Esqueceu a senha? Peça a redefinição ao RH da sua empresa.", 12, h: TextAlignment.Center),
                            config
                        }
                    }
                }
            }
        };
    }

    private async Task EntrarAsync()
    {
        if (string.IsNullOrWhiteSpace(_login.Text) || string.IsNullOrWhiteSpace(_senha.Text)) return;
        _entrar.IsEnabled = false;
        _carregando.IsRunning = true;
        try
        {
            var t = await _api.EntrarAsync(_login.Text.Trim(), _senha.Text, _campoCodigo.IsVisible ? _codigo.Text : null);
            if (t.Papel != "Funcionario")
            {
                await _api.SairAsync();
                await DisplayAlertAsync("PersonaliPonto", "O aplicativo é de uso do funcionário. Gestores e RH devem usar o painel web.", "OK");
                return;
            }
            await _sessao.CarregarAsync();
            _senha.Text = "";
            ((App)Application.Current!).IrParaShell();
        }
        catch (ApiException e) when (e.Message.Contains("código de verificação", StringComparison.OrdinalIgnoreCase))
        {
            _campoCodigo.IsVisible = true;
            _codigo.Focus();
        }
        catch (Exception e)
        {
            await DisplayAlertAsync("PersonaliPonto", Ui.Mensagem(e), "OK");
        }
        finally
        {
            _entrar.IsEnabled = true;
            _carregando.IsRunning = false;
        }
    }
}
