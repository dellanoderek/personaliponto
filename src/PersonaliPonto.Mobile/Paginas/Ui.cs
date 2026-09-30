using Microsoft.Maui.Controls.Shapes;
using PersonaliPonto.Mobile.Servicos;

namespace PersonaliPonto.Mobile.Paginas;

/// <summary>Kit de componentes visuais do app (paleta e formas da apresentação PersonaliPonto).</summary>
public static class Ui
{
    public static readonly Color Marinho = Color.FromArgb("#082352");
    public static readonly Color Azul = Color.FromArgb("#1166D6");
    public static readonly Color Ciano = Color.FromArgb("#21B8EF");
    public static readonly Color Texto = Color.FromArgb("#59708E");
    public static readonly Color Titulo = Color.FromArgb("#082452");
    public static readonly Color Fundo = Color.FromArgb("#F4F8FB");
    public static readonly Color FundoAzul = Color.FromArgb("#E6F4FD");
    public static readonly Color Borda = Color.FromArgb("#DBE6F0");
    public static readonly Color Verde = Color.FromArgb("#16A34A");
    public static readonly Color Vermelho = Color.FromArgb("#C0392B");
    public static readonly Color Amarelo = Color.FromArgb("#B7791F");

    public static Brush Degrade => new LinearGradientBrush(
        [new GradientStop(Azul, 0f), new GradientStop(Color.FromArgb("#0D3A86"), 0.55f), new GradientStop(Marinho, 1f)],
        new Point(0, 0), new Point(1, 1));

    public static Label L(string texto = "", double tamanho = 14, Color? cor = null, bool negrito = false, TextAlignment h = TextAlignment.Start) => new()
    {
        Text = texto, FontSize = tamanho, TextColor = cor ?? Texto, FontFamily = negrito ? "OpenSansSemibold" : "OpenSansRegular",
        HorizontalTextAlignment = h, LineBreakMode = LineBreakMode.WordWrap
    };

    public static Border Cartao(View conteudo, Thickness? padding = null) => new()
    {
        Style = (Style)Application.Current!.Resources["Cartao"],
        Padding = padding ?? new Thickness(16),
        Content = conteudo
    };

    public static Border Chip(string texto, Color? fundo = null, Color? cor = null, bool riscado = false) => new()
    {
        BackgroundColor = fundo ?? FundoAzul,
        StrokeThickness = 0,
        StrokeShape = new RoundRectangle { CornerRadius = 14 },
        Padding = new Thickness(12, 5),
        Content = new Label
        {
            Text = texto, FontSize = 14, FontFamily = "OpenSansSemibold", TextColor = cor ?? Marinho,
            TextDecorations = riscado ? TextDecorations.Strikethrough : TextDecorations.None
        }
    };

    public static Button Botao(string texto, bool primario = true) => new()
    {
        Text = texto, Style = (Style)Application.Current!.Resources[primario ? "BotaoPrimario" : "BotaoSecundario"]
    };

    public static Entry Campo(string placeholder, Keyboard? teclado = null, bool senha = false) => new()
    {
        Placeholder = placeholder, Keyboard = teclado ?? Keyboard.Default, IsPassword = senha, FontSize = 16,
        TextColor = Titulo, PlaceholderColor = Color.FromArgb("#94A3B8"), BackgroundColor = Colors.White, HeightRequest = 48
    };

    public static View CampoComRotulo(string rotulo, View campo) => new VerticalStackLayout
    {
        Spacing = 4,
        Children =
        {
            L(rotulo, 12, Titulo, true),
            new Border { Stroke = Borda, StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 12 }, Padding = new Thickness(10, 0), BackgroundColor = Colors.White, Content = campo }
        }
    };

    /// <summary>Cabeçalho em degradê com título, usado nas telas secundárias.</summary>
    public static View Cabecalho(string titulo, bool voltar = true)
    {
        var grid = new Grid { Background = Degrade, Padding = new Thickness(16, 48, 16, 20), ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star)] };
        if (voltar)
        {
            var b = new Button { Text = "‹", FontSize = 30, TextColor = Colors.White, BackgroundColor = Colors.Transparent, Padding = 0, WidthRequest = 40 };
            b.Clicked += async (_, _) => await Shell.Current.GoToAsync("..");
            grid.Add(b, 0);
        }
        grid.Add(L(titulo, 20, Colors.White, true, TextAlignment.Center), 1);
        return grid;
    }

    public static Task Erro(string mensagem) => Shell.Current is { } s
        ? s.DisplayAlertAsync("PersonaliPonto", mensagem, "OK")
        : Application.Current!.Windows[0].Page!.DisplayAlertAsync("PersonaliPonto", mensagem, "OK");

    public static string Mensagem(Exception e) => e switch
    {
        ApiException a => a.Message,
        HttpRequestException or TaskCanceledException => "Sem conexão com o servidor. Verifique a internet.",
        _ => "Não foi possível concluir. Tente novamente."
    };

    /// <summary>Baixa um PDF autenticado e abre no visualizador do aparelho.</summary>
    public static async Task AbrirPdfAsync(ApiClient api, string rota, string nome)
    {
        var bytes = await api.GetBytesAsync(rota);
        var caminho = System.IO.Path.Combine(FileSystem.CacheDirectory, nome);
        await File.WriteAllBytesAsync(caminho, bytes);
        await Launcher.Default.OpenAsync(new OpenFileRequest("Comprovante", new ReadOnlyFile(caminho, "application/pdf")));
    }
}
