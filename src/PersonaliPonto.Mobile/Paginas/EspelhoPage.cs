using PersonaliPonto.Mobile.Servicos;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Mobile.Paginas;

/// <summary>Espelho do funcionário: período, cartões por dia (Falta / Extra / Trabalhado) e totais.</summary>
public sealed class EspelhoPage : ContentPage
{
    private readonly ApiClient _api;
    private readonly Sessao _sessao;
    private readonly DatePicker _de = new() { Format = "dd/MM/yyyy", TextColor = Ui.Azul, FontSize = 16 };
    private readonly DatePicker _ate = new() { Format = "dd/MM/yyyy", TextColor = Ui.Azul, FontSize = 16 };
    private readonly VerticalStackLayout _lista = new() { Spacing = 12, Padding = new Thickness(16, 8, 16, 24) };
    private readonly Label _nome = Ui.L("", 18, Ui.Titulo, true, TextAlignment.Center);
    private readonly Button _abaRegistros = new() { Text = "Registros", BackgroundColor = Colors.Transparent, TextColor = Ui.Azul };
    private readonly Button _abaTotais = new() { Text = "Totais", BackgroundColor = Colors.Transparent, TextColor = Ui.Texto };
    private readonly ActivityIndicator _carregando = new() { Color = Ui.Azul };
    private EspelhoDto? _espelho;
    private bool _totais;

    public EspelhoPage(ApiClient api, Sessao sessao)
    {
        _api = api;
        _sessao = sessao;
        BackgroundColor = Ui.Fundo;
        var hoje = DateTime.Today;
        _de.Date = hoje.AddDays(-7);
        _ate.Date = hoje;
        _ate.MaximumDate = hoje;
        _de.DateSelected += async (_, _) => await CarregarAsync();
        _ate.DateSelected += async (_, _) => await CarregarAsync();
        _abaRegistros.Clicked += (_, _) => { _totais = false; Renderizar(); };
        _abaTotais.Clicked += (_, _) => { _totais = true; Renderizar(); };

        var rotuloPeriodo = Ui.L("Período:", 15, Ui.Titulo);
        rotuloPeriodo.VerticalOptions = LayoutOptions.Center;
        var rotuloA = Ui.L("a", 15, Ui.Azul);
        rotuloA.VerticalOptions = LayoutOptions.Center;
        var periodo = new HorizontalStackLayout
        {
            Spacing = 6, HorizontalOptions = LayoutOptions.Center,
            Children = { rotuloPeriodo, _de, rotuloA, _ate }
        };

        var abas = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)], Children = { _abaRegistros, _abaTotais } };
        Grid.SetColumn(_abaTotais, 1);

        var raiz = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star)] };
        raiz.Add(Ui.Cabecalho("Espelho", voltar: false), 0, 0);
        raiz.Add(new VerticalStackLayout { Padding = new Thickness(16, 16, 16, 0), Spacing = 10, Children = { _nome, periodo, abas, _carregando } }, 0, 1);
        raiz.Add(new ScrollView { Content = _lista }, 0, 2);
        Content = raiz;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _nome.Text = _sessao.Perfil?.Nome.ToUpperInvariant() ?? "";
        await CarregarAsync();
    }

    private async Task CarregarAsync()
    {
        if (_ate.Date < _de.Date) return;
        _carregando.IsRunning = true;
        try
        {
            _espelho = await _api.GetAsync<EspelhoDto>($"/api/app/espelho?inicio={_de.Date:yyyy-MM-dd}&fim={_ate.Date:yyyy-MM-dd}");
            Renderizar();
        }
        catch (Exception e)
        {
            _lista.Children.Clear();
            _lista.Children.Add(Ui.L(Ui.Mensagem(e), 14, h: TextAlignment.Center));
        }
        finally
        {
            _carregando.IsRunning = false;
        }
    }

    private void Renderizar()
    {
        _abaRegistros.TextColor = _totais ? Ui.Texto : Ui.Azul;
        _abaTotais.TextColor = _totais ? Ui.Azul : Ui.Texto;
        _lista.Children.Clear();
        if (_espelho is null) return;

        if (_totais)
        {
            View Linha(string r, int v, Color c) => new Grid
            {
                ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)],
                Children = { Ui.L(r, 15, Ui.Titulo), Ui.L(Configuracao.Hhmm(v), 17, c, true) }
            }.Also(g => Grid.SetColumn((BindableObject)g.Children[1], 1));
            _lista.Children.Add(Ui.Cartao(new VerticalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    Linha("Trabalhado", _espelho.TotalTrabalhado, Ui.Azul),
                    Linha("Horas extras", _espelho.TotalExtras, Ui.Verde),
                    Linha("Atrasos", _espelho.TotalAtrasos, Ui.Vermelho),
                    Linha("Faltas", _espelho.TotalFaltas, Ui.Vermelho),
                    new BoxView { HeightRequest = 1, Color = Ui.Borda },
                    Linha("Saldo do banco de horas", _espelho.SaldoBancoHoras, Ui.Marinho)
                }
            }));
            var pdf = Ui.Botao("Baixar espelho em PDF", false);
            pdf.Clicked += async (_, _) =>
            {
                try { await Ui.AbrirPdfAsync(_api, $"/api/app/espelho/pdf?inicio={_de.Date:yyyy-MM-dd}&fim={_ate.Date:yyyy-MM-dd}", "espelho.pdf"); }
                catch (Exception e) { await Ui.Erro(Ui.Mensagem(e)); }
            };
            _lista.Children.Add(pdf);
            return;
        }

        foreach (var d in _espelho.Dias.OrderByDescending(x => x.Data))
        {
            var data = new VerticalStackLayout
            {
                WidthRequest = 56, VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    Ui.L(d.Data.Year.ToString(), 12, h: TextAlignment.Center),
                    Ui.L(d.Data.Day.ToString("00"), 26, Ui.Titulo, true, TextAlignment.Center),
                    Ui.L(Configuracao.Br.TextInfo.ToTitleCase(d.Data.ToString("MMM", Configuracao.Br)), 12, h: TextAlignment.Center)
                }
            };
            var valores = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)] };
            void Valor(int col, string rotulo, int min, Color cor)
            {
                var v = new VerticalStackLayout { Children = { Ui.L(rotulo, 13, Ui.Titulo, h: TextAlignment.Center), Ui.L(Configuracao.Hhmm(min), 15, cor, false, TextAlignment.Center) } };
                valores.Add(v, col);
            }
            Valor(0, "Falta", d.MinutosFalta + d.MinutosAtraso, Ui.Vermelho);
            Valor(1, "Extra", d.MinutosExtras, Ui.Verde);
            Valor(2, "Trab.", d.MinutosTrabalhados, Ui.Azul);

            var marcas = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
            foreach (var m in d.Marcacoes)
            {
                var chip = Ui.Chip(m.DataHora.ToString("HH:mm"), m.Desconsiderada ? Color.FromArgb("#EEF2F6") : m.Fonte == "Incluída" ? Color.FromArgb("#FDF6E3") : null,
                    m.Desconsiderada ? Color.FromArgb("#94A3B8") : null, m.Desconsiderada);
                chip.Margin = new Thickness(0, 6, 6, 0);
                marcas.Children.Add(chip);
            }

            var corpo = new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    Ui.L(Configuracao.Br.TextInfo.ToTitleCase(d.Data.ToString("dddd", Configuracao.Br)), 17, Ui.Titulo, true, TextAlignment.Center),
                    new BoxView { HeightRequest = 1, Color = Ui.Borda },
                    valores
                }
            };
            if (d.Marcacoes.Count > 0) corpo.Children.Add(marcas);
            if (d.Ocorrencia is not null) corpo.Children.Add(Ui.L(d.Ocorrencia, 12, d.Inconsistente ? Ui.Amarelo : Ui.Texto));

            var grade = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star)], ColumnSpacing = 12 };
            grade.Add(data, 0);
            grade.Add(corpo, 1);
            _lista.Children.Add(Ui.Cartao(grade));
        }
    }
}

internal static class Extensoes
{
    public static T Also<T>(this T obj, Action<T> acao)
    {
        acao(obj);
        return obj;
    }
}
