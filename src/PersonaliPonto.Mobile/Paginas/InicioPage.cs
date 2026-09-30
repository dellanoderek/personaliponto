using Microsoft.Maui.Controls.Shapes;
using PersonaliPonto.Mobile.Servicos;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Mobile.Paginas;

/// <summary>
/// Tela inicial (referência: app de mercado enviado pelo cliente): relógio oficial, "deslize para registrar",
/// registros do dia, jornada do dia e últimos dias.
/// </summary>
public sealed class InicioPage : ContentPage
{
    private readonly ApiClient _api;
    private readonly RelogioOficial _relogio;
    private readonly FilaMarcacoes _fila;
    private readonly Sessao _sessao;

    private readonly Label _hora = Ui.L("--:--:--", 40, Colors.White, true, TextAlignment.Center);
    private readonly Label _data = Ui.L("", 16, Color.FromArgb("#D8ECFB"), h: TextAlignment.Center);
    private readonly Label _aviso = Ui.L("", 12, Color.FromArgb("#FDE68A"), h: TextAlignment.Center);
    private readonly FlexLayout _registrosHoje = new() { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, Direction = Microsoft.Maui.Layouts.FlexDirection.Row };
    private readonly Label _semRegistros = Ui.L("Você ainda não iniciou sua jornada.", 14);
    private readonly Label _atualizado = Ui.L("", 12);
    private readonly Label _jornada = Ui.L("", 14, Ui.Titulo);
    private readonly VerticalStackLayout _ultimos = new() { Spacing = 8 };
    private readonly Border _polegar;
    private readonly Grid _trilho;
    private readonly Grid _sucesso;
    private readonly Label _sucessoHora = Ui.L("", 48, Ui.Marinho, true, TextAlignment.Center);
    private readonly Label _sucessoInfo = Ui.L("", 13, h: TextAlignment.Center);
    private readonly Button _fechar = Ui.Botao("Fechar", false);
    private MarcacaoRegistradaDto? _ultimo;
    private bool _registrando;
    private IDispatcherTimer? _timer;

    public InicioPage(ApiClient api, RelogioOficial relogio, FilaMarcacoes fila, Sessao sessao)
    {
        _api = api;
        _relogio = relogio;
        _fila = fila;
        _sessao = sessao;
        BackgroundColor = Ui.Fundo;

        // Trilho "deslize para registrar": o polegar precisa chegar ao fim para registrar (evita toques acidentais).
        _polegar = new Border
        {
            WidthRequest = 120, HeightRequest = 120, BackgroundColor = Ui.Azul, StrokeThickness = 0,
            StrokeShape = new Ellipse(), HorizontalOptions = LayoutOptions.Start,
            Shadow = new Shadow { Brush = Ui.Marinho, Opacity = 0.35f, Radius = 16, Offset = new Point(0, 6) },
            Content = Ui.L("Deslize ›", 18, Colors.White, true, TextAlignment.Center)
        };
        ((Label)_polegar.Content!).VerticalTextAlignment = TextAlignment.Center;
        var alvo = new Border
        {
            WidthRequest = 120, HeightRequest = 120, BackgroundColor = Colors.White, StrokeThickness = 0, StrokeShape = new Ellipse(),
            HorizontalOptions = LayoutOptions.End,
            Shadow = new Shadow { Brush = Ui.Marinho, Opacity = 0.2f, Radius = 14, Offset = new Point(0, 6) },
            Content = new Label { Text = "Registrar\nPonto", FontSize = 17, TextColor = Ui.Azul, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center }
        };
        _trilho = new Grid { HeightRequest = 124, Margin = new Thickness(20, -60, 20, 0), Children = { alvo, _polegar } };
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += Deslizar;
        _polegar.GestureRecognizers.Add(pan);
        var toqueAlvo = new TapGestureRecognizer();
        toqueAlvo.Tapped += async (_, _) => await DisplayAlertAsync("Registrar ponto", "Arraste o botão azul até aqui para registrar.", "OK");
        alvo.GestureRecognizers.Add(toqueAlvo);

        var cabecalho = new Grid
        {
            Background = Ui.Degrade, Padding = new Thickness(20, 44, 20, 84),
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto)]
        };
        cabecalho.Add(new Border
        {
            BackgroundColor = Colors.White, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(10, 6), HorizontalOptions = LayoutOptions.Start, Content = new Image { Source = "logo.png", HeightRequest = 36 }
        }, 0, 0);
        cabecalho.Add(new VerticalStackLayout { Margin = new Thickness(0, 22, 0, 0), Children = { _hora, _data } }, 0, 1);
        cabecalho.Add(_aviso, 0, 2);

        var atualizar = new Button { Text = "↻", FontSize = 20, TextColor = Ui.Azul, BackgroundColor = Colors.Transparent, Padding = 0, WidthRequest = 40 };
        atualizar.Clicked += async (_, _) => await AtualizarAsync();
        var cartaoRegistros = Ui.Cartao(new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new Grid
                {
                    ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)],
                    Children = { new VerticalStackLayout { Children = { Ui.L("Registros do dia", 18, Ui.Titulo, true), Ui.L("Seus pontos registrados hoje", 13) } }, atualizar }
                },
                new BoxView { HeightRequest = 1, Color = Ui.Borda },
                _semRegistros, _registrosHoje, _atualizado
            }
        });
        Grid.SetColumn(atualizar, 1);

        var cartaoJornada = Ui.Cartao(new VerticalStackLayout
        {
            Spacing = 6,
            Children = { Ui.L("Jornada do dia", 18, Ui.Titulo, true), Ui.L("Tempo restante para finalizar sua jornada", 13), _jornada }
        });
        var cartaoUltimos = Ui.Cartao(new VerticalStackLayout
        {
            Spacing = 8,
            Children = { Ui.L("Últimos dias", 18, Ui.Titulo, true), Ui.L("Sua jornada nos dias anteriores", 13), _ultimos }
        });

        _fechar.Clicked += (_, _) => _sucesso!.IsVisible = false;
        var verRecibo = Ui.Botao("Visualizar comprovante");
        verRecibo.Clicked += async (_, _) =>
        {
            if (_ultimo is null) return;
            try { await Ui.AbrirPdfAsync(_api, $"/api/app/comprovantes/{_ultimo.Id}", $"comprovante-{_ultimo.Nsr}.pdf"); }
            catch (Exception e) { await Ui.Erro(Ui.Mensagem(e)); }
        };
        _sucesso = new Grid
        {
            IsVisible = false, BackgroundColor = Color.FromArgb("#99082352"), Padding = new Thickness(24),
            Children =
            {
                new Border
                {
                    BackgroundColor = Colors.White, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 24 },
                    Padding = new Thickness(24), VerticalOptions = LayoutOptions.Center,
                    Content = new VerticalStackLayout
                    {
                        Spacing = 12,
                        Children =
                        {
                            Ui.L("Ponto registrado com sucesso!", 20, Ui.Titulo, true, TextAlignment.Center),
                            new Border
                            {
                                WidthRequest = 84, HeightRequest = 84, StrokeShape = new Ellipse(), StrokeThickness = 0, BackgroundColor = Color.FromArgb("#E8F7EE"),
                                HorizontalOptions = LayoutOptions.Center,
                                Content = Ui.L("✓", 44, Ui.Verde, true, TextAlignment.Center)
                            },
                            _sucessoHora, _sucessoInfo, verRecibo, _fechar
                        }
                    }
                }
            }
        };

        var raiz = new Grid();
        raiz.Add(new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Children =
                {
                    cabecalho, _trilho,
                    new VerticalStackLayout { Padding = new Thickness(16, 20, 16, 24), Spacing = 14, Children = { cartaoRegistros, cartaoJornada, cartaoUltimos } }
                }
            }
        });
        raiz.Add(_sucesso);
        Content = raiz;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _timer ??= Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.Tick -= Tique;
        _timer.Tick += Tique;
        _timer.Start();
        await AtualizarAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _timer?.Stop();
    }

    private void Tique(object? s, EventArgs e)
    {
        if (_relogio.AgoraLocal is { } agora && _relogio.Disponivel)
        {
            _hora.Text = agora.ToString("HH:mm:ss");
            _data.Text = Configuracao.Br.TextInfo.ToTitleCase(agora.ToString("dddd, dd 'de' MMMM", Configuracao.Br));
            _aviso.Text = "";
        }
        else
        {
            _hora.Text = "--:--:--";
            _aviso.Text = "Conecte-se à internet para sincronizar o relógio oficial.";
        }
    }

    private void Deslizar(object? sender, PanUpdatedEventArgs e)
    {
        var limite = _trilho.Width - _polegar.Width;
        switch (e.StatusType)
        {
            case GestureStatus.Running:
                _polegar.TranslationX = Math.Clamp(e.TotalX, 0, Math.Max(0, limite));
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                var chegou = limite > 0 && _polegar.TranslationX >= limite * 0.85;
                _ = _polegar.TranslateToAsync(0, 0, 250, Easing.CubicOut);
                if (chegou) _ = RegistrarAsync();
                break;
        }
    }

    private async Task RegistrarAsync()
    {
        if (_registrando) return;
        _registrando = true;
        try
        {
            if (!_relogio.Disponivel) await _sessao.SincronizarHoraAsync();
            if (!_relogio.Disponivel || _relogio.Agora is null || _relogio.Ancora is null)
            {
                await Ui.Erro("Sem sincronização com o relógio oficial. Conecte-se à internet e tente novamente.");
                return;
            }
            try { HapticFeedback.Default.Perform(HapticFeedbackType.LongPress); } catch { }

            Location? local = null;
            try
            {
                if (await Permissions.RequestAsync<Permissions.LocationWhenInUse>() == PermissionStatus.Granted)
                    local = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(6)));
            }
            catch { }

            var item = new ItemFila
            {
                ClientId = Guid.NewGuid(), Horario = _relogio.Agora.Value, AncoraId = _relogio.Ancora.AncoraId,
                Latitude = local?.Latitude, Longitude = local?.Longitude, Estado = EstadoSincronizacao.Pending
            };
            await _fila.AdicionarAsync(item);
            var dto = await _fila.EnviarAsync(item, offline: false);
            if (dto is not null)
            {
                _ultimo = dto;
                if (_sessao.Perfil?.FotoNaMarcacao == true) _ = EnviarFotoAsync(dto.Id);
                var offset = TimeSpan.FromMinutes(_relogio.Ancora.OffsetMinutos);
                _sucessoHora.Text = dto.DataHoraMarcacao.ToOffset(offset).ToString("HH:mm");
                _sucessoInfo.Text = $"NSR {dto.Nsr:000000000}\nO comprovante fica disponível no histórico.";
            }
            else
            {
                if (_sessao.Perfil is { PermiteOffline: false })
                {
                    await Ui.Erro("Sem conexão. Sua empresa não permite marcação off-line; tente novamente quando houver internet.");
                    return;
                }
                _ultimo = null;
                _sucessoHora.Text = _relogio.AgoraLocal!.Value.ToString("HH:mm");
                _sucessoInfo.Text = "Sem conexão: a marcação foi guardada no aparelho e será enviada automaticamente.";
            }
            _sucesso.IsVisible = true;
            _ = ContagemAsync();
            await AtualizarAsync();
        }
        catch (Exception e)
        {
            await Ui.Erro(Ui.Mensagem(e));
        }
        finally
        {
            _registrando = false;
        }
    }

    /// <summary>Foto opcional (habilitada pela empresa). Falhas não afetam a marcação já registrada.</summary>
    private async Task EnviarFotoAsync(Guid registroId)
    {
        try
        {
            if (!MediaPicker.Default.IsCaptureSupported) return;
            var f = await MediaPicker.Default.CapturePhotoAsync(new MediaPickerOptions { Title = "Foto da marcação" });
            if (f is null) return;
            await using var s = await f.OpenReadAsync();
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms);
            using var form = new MultipartFormDataContent();
            var c = new ByteArrayContent(ms.ToArray());
            c.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(f.ContentType ?? "image/jpeg");
            form.Add(c, "foto", f.FileName);
            await _api.PostMultipartAsync($"/api/app/marcacoes/{registroId}/foto", form);
        }
        catch { }
    }

    private async Task ContagemAsync()
    {
        for (var i = 20; i > 0 && _sucesso.IsVisible; i--)
        {
            _fechar.Text = $"Fechar ({i})";
            await Task.Delay(1000);
        }
        _sucesso.IsVisible = false;
    }

    private async Task AtualizarAsync()
    {
        var offset = TimeSpan.FromMinutes(_relogio.Ancora?.OffsetMinutos ?? -180);
        var hoje = (_relogio.AgoraLocal ?? DateTimeOffset.Now.ToOffset(offset)).Date;
        var horarios = new List<(DateTimeOffset Hora, bool Pendente)>();

        foreach (var i in (await _fila.ItensAsync()).Where(i => i.Estado is not EstadoSincronizacao.Synced && i.Estado is not EstadoSincronizacao.Failed))
            if (i.Horario.ToOffset(offset).Date == hoje) horarios.Add((i.Horario, true));

        try
        {
            await _sessao.SincronizarHoraAsync();
            var ultimas = await _api.GetAsync<List<UltimaMarcacaoDto>>("/api/app/marcacoes?quantidade=20");
            horarios.AddRange(ultimas.Where(m => m.DataHoraMarcacao.ToOffset(offset).Date == hoje).Select(m => (m.DataHoraMarcacao, false)));

            var d = DateOnly.FromDateTime(hoje);
            var espelho = await _api.GetAsync<EspelhoDto>($"/api/app/espelho?inicio={d.AddDays(-6):yyyy-MM-dd}&fim={d:yyyy-MM-dd}");
            var dia = espelho.Dias.LastOrDefault();
            if (dia is not null)
            {
                var trabalhado = dia.MinutosTrabalhados;
                var marcas = dia.Marcacoes.Where(m => !m.Desconsiderada).Select(m => m.DataHora).OrderBy(x => x).ToList();
                if (marcas.Count % 2 == 1 && _relogio.Agora is { } agora) trabalhado += (int)(agora - marcas[^1]).TotalMinutes;
                _jornada.Text = dia.MinutosPrevistos == 0
                    ? "Hoje não há jornada prevista."
                    : $"Prevista {Configuracao.Hhmm(dia.MinutosPrevistos)} · trabalhado {Configuracao.Hhmm(trabalhado)} · restante {Configuracao.Hhmm(Math.Max(0, dia.MinutosPrevistos - trabalhado))}";
            }
            _ultimos.Children.Clear();
            foreach (var u in espelho.Dias.Where(x => x.Data < d).OrderByDescending(x => x.Data).Take(5))
            {
                var linha = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 10 };
                linha.Add(Ui.L(u.Data.ToString("dd/MM ddd", Configuracao.Br), 13, Ui.Titulo, true), 0);
                linha.Add(Ui.L(string.Join("  ", u.Marcacoes.Where(m => !m.Desconsiderada).Select(m => m.DataHora.ToString("HH:mm"))), 13), 1);
                linha.Add(Ui.L(Configuracao.Hhmm(u.MinutosTrabalhados), 13, u.MinutosFalta > 0 ? Ui.Vermelho : Ui.Azul, true), 2);
                _ultimos.Children.Add(linha);
            }
            _atualizado.Text = $"Atualizado em: {DateTime.Now:HH:mm}";
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or ApiException)
        {
            _atualizado.Text = "Sem conexão — mostrando dados do aparelho.";
        }

        _registrosHoje.Children.Clear();
        foreach (var (hora, pendente) in horarios.DistinctBy(x => x.Hora.ToUnixTimeSeconds() / 60).OrderBy(x => x.Hora))
        {
            var chip = Ui.Chip(hora.ToOffset(offset).ToString("HH:mm") + (pendente ? " ⏳" : ""), pendente ? Color.FromArgb("#FDF6E3") : null, pendente ? Ui.Amarelo : null);
            chip.Margin = new Thickness(0, 0, 8, 8);
            _registrosHoje.Children.Add(chip);
        }
        _semRegistros.IsVisible = horarios.Count == 0;
    }
}
