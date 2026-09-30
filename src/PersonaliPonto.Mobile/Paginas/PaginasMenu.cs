using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using PersonaliPonto.Mobile.Servicos;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Mobile.Paginas;

/// <summary>Pedido de inclusão de marcação esquecida (vai para aprovação do RH).</summary>
public sealed class AjustePage : ContentPage
{
    public AjustePage(ApiClient api)
    {
        BackgroundColor = Ui.Fundo;
        var data = new DatePicker { Format = "dd/MM/yyyy", MaximumDate = DateTime.Today, Date = DateTime.Today, TextColor = Ui.Titulo };
        var hora = new TimePicker { Time = new TimeSpan(8, 0, 0), Format = "HH:mm", TextColor = Ui.Titulo };
        var motivo = new Editor { Placeholder = "Descreva o motivo", HeightRequest = 100, MaxLength = 150, TextColor = Ui.Titulo };
        var enviar = Ui.Botao("Enviar ao RH");
        enviar.Clicked += async (_, _) =>
        {
            enviar.IsEnabled = false;
            try
            {
                var (status, _, erro) = await api.PostAsync<JsonElement>("/api/app/solicitacoes", new
                {
                    tipo = 1, data = DateOnly.FromDateTime(data.Date!.Value), horario = TimeOnly.FromTimeSpan(hora.Time!.Value).ToString("HH:mm"), motivo = motivo.Text ?? ""
                });
                if (status == HttpStatusCode.Created)
                {
                    await DisplayAlertAsync("PersonaliPonto", "Solicitação enviada. Acompanhe a resposta do RH no histórico.", "OK");
                    await Shell.Current.GoToAsync("..");
                }
                else await Ui.Erro(erro ?? "Não foi possível enviar.");
            }
            catch (Exception e) { await Ui.Erro(Ui.Mensagem(e)); }
            finally { enviar.IsEnabled = true; }
        };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Children =
                {
                    Ui.Cabecalho("Ajuste de ponto"),
                    new VerticalStackLayout
                    {
                        Padding = new Thickness(16), Spacing = 12,
                        Children =
                        {
                            Ui.L("Informe o dia e o horário da marcação que ficou faltando. O registro original nunca é alterado: o ajuste é analisado e registrado pelo RH.", 13),
                            Ui.CampoComRotulo("Data", data), Ui.CampoComRotulo("Horário", hora), Ui.CampoComRotulo("Motivo", motivo), enviar
                        }
                    }
                }
            }
        };
    }
}

/// <summary>Envio de atestado/justificativa com foto (câmera ou galeria) ou PDF.</summary>
public sealed class AbonoPage : ContentPage
{
    private byte[]? _arquivo;
    private string _nome = "";
    private string _tipoMime = "";

    public AbonoPage(ApiClient api)
    {
        BackgroundColor = Ui.Fundo;
        var tipo = new Picker { Title = "Tipo", ItemsSource = new[] { "Atestado médico", "Declaração de comparecimento", "Justificativa" }, SelectedIndex = 0, TextColor = Ui.Titulo };
        var de = new DatePicker { Format = "dd/MM/yyyy", Date = DateTime.Today, TextColor = Ui.Titulo };
        var ate = new DatePicker { Format = "dd/MM/yyyy", Date = DateTime.Today, TextColor = Ui.Titulo };
        var obs = new Entry { Placeholder = "Observação (opcional)", TextColor = Ui.Titulo };
        var anexo = Ui.L("Nenhum arquivo anexado.", 13);
        var foto = Ui.Botao("Tirar foto", false);
        var galeria = Ui.Botao("Escolher arquivo", false);
        var enviar = Ui.Botao("Enviar ao RH");

        async Task Carregar(FileResult? f)
        {
            if (f is null) return;
            await using var s = await f.OpenReadAsync();
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms);
            if (ms.Length > 10 * 1024 * 1024) { await Ui.Erro("Arquivo acima de 10 MB."); return; }
            _arquivo = ms.ToArray();
            _nome = f.FileName;
            _tipoMime = f.ContentType ?? "application/octet-stream";
            anexo.Text = $"Anexo: {f.FileName} ({ms.Length / 1024} KB)";
        }

        foto.Clicked += async (_, _) =>
        {
            try
            {
                if (!MediaPicker.Default.IsCaptureSupported) { await Ui.Erro("Câmera indisponível."); return; }
                await Carregar(await MediaPicker.Default.CapturePhotoAsync());
            }
            catch (Exception e) { await Ui.Erro(e.Message); }
        };
        galeria.Clicked += async (_, _) =>
        {
            try
            {
                await Carregar(await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = "Atestado",
                    FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                    {
                        [DevicePlatform.Android] = ["image/*", "application/pdf"],
                        [DevicePlatform.iOS] = ["public.image", "com.adobe.pdf"],
                        [DevicePlatform.WinUI] = [".jpg", ".jpeg", ".png", ".webp", ".pdf"]
                    })
                }));
            }
            catch (Exception e) { await Ui.Erro(e.Message); }
        };
        enviar.Clicked += async (_, _) =>
        {
            enviar.IsEnabled = false;
            try
            {
                using var form = new MultipartFormDataContent
                {
                    { new StringContent((tipo.SelectedIndex switch { 1 => 2, 2 => 3, _ => 1 }).ToString()), "tipo" },
                    { new StringContent(de.Date!.Value.ToString("yyyy-MM-dd")), "inicio" },
                    { new StringContent(ate.Date!.Value.ToString("yyyy-MM-dd")), "fim" },
                    { new StringContent(obs.Text ?? ""), "observacao" }
                };
                if (_arquivo is not null)
                {
                    var c = new ByteArrayContent(_arquivo);
                    c.Headers.ContentType = new MediaTypeHeaderValue(_tipoMime);
                    form.Add(c, "arquivo", _nome);
                }
                await api.PostMultipartAsync("/api/app/atestados", form);
                await DisplayAlertAsync("PersonaliPonto", "Documento enviado ao RH.", "OK");
                await Shell.Current.GoToAsync("..");
            }
            catch (Exception e) { await Ui.Erro(Ui.Mensagem(e)); }
            finally { enviar.IsEnabled = true; }
        };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Children =
                {
                    Ui.Cabecalho("Abono de falta"),
                    new VerticalStackLayout
                    {
                        Padding = new Thickness(16), Spacing = 12,
                        Children =
                        {
                            Ui.CampoComRotulo("Tipo", tipo), Ui.CampoComRotulo("De", de), Ui.CampoComRotulo("Até", ate), Ui.CampoComRotulo("Observação", obs),
                            new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)], ColumnSpacing = 10, Children = { foto, galeria } }.Also(g => Grid.SetColumn(galeria, 1)),
                            anexo, enviar
                        }
                    }
                }
            }
        };
    }
}

/// <summary>Assinatura (ciência) do espelho mensal.</summary>
public sealed class AssinaturaPage : ContentPage
{
    public AssinaturaPage(ApiClient api)
    {
        BackgroundColor = Ui.Fundo;
        var meses = Enumerable.Range(1, 6).Select(i => DateTime.Today.AddMonths(-i)).Select(d => new DateTime(d.Year, d.Month, 1)).ToList();
        var mes = new Picker { Title = "Mês", ItemsSource = meses.Select(m => Configuracao.Br.TextInfo.ToTitleCase(m.ToString("MMMM 'de' yyyy", Configuracao.Br))).ToList(), SelectedIndex = 0, TextColor = Ui.Titulo };
        var situacao = Ui.L("", 14, Ui.Titulo);
        var obs = new Editor { Placeholder = "Observação (obrigatória se discordar)", HeightRequest = 90, TextColor = Ui.Titulo };
        var concordo = Ui.Botao("Concordo e assino");
        var ressalva = Ui.Botao("Assinar com ressalva", false);
        var pdf = Ui.Botao("Ver espelho do mês", false);
        DateTime Sel() => meses[Math.Max(0, mes.SelectedIndex)];

        async Task Consultar()
        {
            try
            {
                var m = Sel();
                var r = await api.GetBytesAsync($"/api/app/espelho/assinatura?ano={m.Year}&mes={m.Month}");
                if (r.Length == 0) { situacao.Text = "Pendente de assinatura."; concordo.IsEnabled = ressalva.IsEnabled = true; return; }
                var j = JsonSerializer.Deserialize<JsonElement>(r);
                situacao.Text = $"Assinado em {j.GetProperty("assinadoEm").GetDateTimeOffset().ToLocalTime():dd/MM/yyyy HH:mm}" + (j.GetProperty("concorda").GetBoolean() ? "." : " com ressalva.");
                concordo.IsEnabled = ressalva.IsEnabled = false;
            }
            catch (Exception e) { situacao.Text = Ui.Mensagem(e); }
        }

        async Task Assinar(bool concorda)
        {
            var m = Sel();
            var (status, _, erro) = await api.PostAsync<JsonElement>("/api/app/espelho/assinatura", new { ano = m.Year, mes = m.Month, concorda, observacao = obs.Text });
            if ((int)status < 300) { await DisplayAlertAsync("PersonaliPonto", "Espelho assinado.", "OK"); await Consultar(); }
            else await Ui.Erro(erro ?? "Não foi possível assinar.");
        }

        mes.SelectedIndexChanged += async (_, _) => await Consultar();
        concordo.Clicked += async (_, _) => await Assinar(true);
        ressalva.Clicked += async (_, _) => await Assinar(false);
        pdf.Clicked += async (_, _) =>
        {
            var m = Sel();
            try { await Ui.AbrirPdfAsync(api, $"/api/app/espelho/pdf?inicio={m:yyyy-MM-dd}&fim={m.AddMonths(1).AddDays(-1):yyyy-MM-dd}", $"espelho-{m:yyyy-MM}.pdf"); }
            catch (Exception e) { await Ui.Erro(Ui.Mensagem(e)); }
        };
        Loaded += async (_, _) => await Consultar();

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Children =
                {
                    Ui.Cabecalho("Assinatura de espelho"),
                    new VerticalStackLayout
                    {
                        Padding = new Thickness(16), Spacing = 12,
                        Children = { Ui.CampoComRotulo("Competência", mes), Ui.Cartao(situacao), pdf, Ui.CampoComRotulo("Observação", obs), concordo, ressalva }
                    }
                }
            }
        };
    }
}

/// <summary>Histórico: marcações do servidor, pendências locais e acesso aos comprovantes.</summary>
public sealed class HistoricoPage : ContentPage
{
    public HistoricoPage(ApiClient api, FilaMarcacoes fila)
    {
        BackgroundColor = Ui.Fundo;
        var lista = new VerticalStackLayout { Padding = new Thickness(16), Spacing = 10 };
        Content = new ScrollView { Content = new VerticalStackLayout { Children = { Ui.Cabecalho("Histórico de pontos"), lista } } };

        Loaded += async (_, _) =>
        {
            lista.Children.Clear();
            var pendentes = (await fila.ItensAsync()).Where(i => i.Estado != EstadoSincronizacao.Synced).OrderByDescending(i => i.Horario).ToList();
            if (pendentes.Count > 0)
            {
                lista.Children.Add(Ui.L("Neste aparelho", 14, Ui.Titulo, true));
                foreach (var p in pendentes)
                {
                    var (texto, cor) = p.Estado switch
                    {
                        EstadoSincronizacao.Conflict => ("conflito — procure o RH", Ui.Vermelho),
                        EstadoSincronizacao.Failed => ("recusada", Ui.Vermelho),
                        _ => ("aguardando envio", Ui.Amarelo)
                    };
                    lista.Children.Add(Ui.Cartao(new VerticalStackLayout
                    {
                        Children =
                        {
                            Ui.L(p.Horario.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), 16, Ui.Titulo, true),
                            Ui.L(texto + (p.Mensagem is null ? "" : $" · {p.Mensagem}"), 12, cor)
                        }
                    }));
                }
            }
            try
            {
                var ultimas = await api.GetAsync<List<UltimaMarcacaoDto>>("/api/app/marcacoes?quantidade=60");
                lista.Children.Add(Ui.L("Registradas", 14, Ui.Titulo, true));
                foreach (var m in ultimas)
                {
                    var g = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
                    g.Add(new VerticalStackLayout
                    {
                        Children =
                        {
                            Ui.L(m.DataHoraMarcacao.ToLocalTime().ToString("ddd, dd/MM/yyyy · HH:mm", Configuracao.Br), 15, Ui.Titulo, true),
                            Ui.L($"NSR {m.Nsr:000000000}{(m.Offline ? " · off-line" : "")}", 12)
                        }
                    }, 0);
                    var b = new Button { Text = "Comprovante", FontSize = 12, TextColor = Ui.Azul, BackgroundColor = Ui.FundoAzul, CornerRadius = 10, Padding = new Thickness(10, 4) };
                    b.Clicked += async (_, _) =>
                    {
                        try { await Ui.AbrirPdfAsync(api, $"/api/app/comprovantes/{m.Id}", $"comprovante-{m.Nsr}.pdf"); }
                        catch (Exception e) { await Ui.Erro(Ui.Mensagem(e)); }
                    };
                    g.Add(b, 1);
                    lista.Children.Add(Ui.Cartao(g, new Thickness(14)));
                }
            }
            catch (Exception e) { lista.Children.Add(Ui.L(Ui.Mensagem(e), 13)); }
        };
    }
}

public sealed class BancoHorasPage : ContentPage
{
    private sealed record Lancamento(DateOnly Data, int Minutos, string Tipo, string Descricao);
    private sealed record Extrato(int Saldo, List<Lancamento> Lancamentos);

    public BancoHorasPage(ApiClient api)
    {
        BackgroundColor = Ui.Fundo;
        var saldo = Ui.L("--:--", 40, Ui.Marinho, true, TextAlignment.Center);
        var lista = new VerticalStackLayout { Spacing = 8 };
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Children =
                {
                    Ui.Cabecalho("Banco de horas"),
                    new VerticalStackLayout { Padding = new Thickness(16), Spacing = 12, Children = { Ui.Cartao(new VerticalStackLayout { Children = { Ui.L("Saldo atual", 13, h: TextAlignment.Center), saldo } }), lista } }
                }
            }
        };
        Loaded += async (_, _) =>
        {
            try
            {
                var e = await api.GetAsync<Extrato>("/api/app/banco-horas");
                saldo.Text = Configuracao.Hhmm(e.Saldo);
                saldo.TextColor = e.Saldo < 0 ? Ui.Vermelho : Ui.Marinho;
                foreach (var l in e.Lancamentos)
                {
                    var g = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
                    g.Add(new VerticalStackLayout { Children = { Ui.L(l.Descricao, 14, Ui.Titulo, true), Ui.L(l.Data.ToString("dd/MM/yyyy"), 12) } }, 0);
                    g.Add(Ui.L(Configuracao.Hhmm(l.Minutos), 16, l.Minutos < 0 ? Ui.Vermelho : Ui.Verde, true), 1);
                    lista.Children.Add(Ui.Cartao(g, new Thickness(14)));
                }
                if (e.Lancamentos.Count == 0) lista.Children.Add(Ui.L("Nenhum lançamento ainda.", 13, h: TextAlignment.Center));
            }
            catch (Exception ex) { lista.Children.Add(Ui.L(Ui.Mensagem(ex), 13)); }
        };
    }
}

public sealed class ConfiguracoesPage : ContentPage
{
    public ConfiguracoesPage(ApiClient api)
    {
        BackgroundColor = Ui.Fundo;
        var atual = Ui.Campo("Senha atual", senha: true);
        var nova = Ui.Campo("Nova senha (mín. 10, letras e números)", senha: true);
        var trocar = Ui.Botao("Trocar senha");
        trocar.Clicked += async (_, _) =>
        {
            var (status, _, erro) = await api.PostAsync<JsonElement>("/api/auth/senha", new { senhaAtual = atual.Text, novaSenha = nova.Text });
            if ((int)status < 300)
            {
                await DisplayAlertAsync("PersonaliPonto", "Senha alterada. Entre novamente.", "OK");
                await api.SairAsync();
                ((App)Application.Current!).IrParaLogin();
            }
            else await Ui.Erro(erro ?? "Não foi possível trocar a senha.");
        };
        var servidor = Ui.Campo("https://", Keyboard.Url);
        servidor.Text = ApiClient.Servidor;
        var salvar = Ui.Botao("Salvar servidor", false);
        salvar.Clicked += async (_, _) =>
        {
            if (!Uri.TryCreate(servidor.Text, UriKind.Absolute, out _)) { await Ui.Erro("Endereço inválido."); return; }
            ApiClient.Servidor = servidor.Text!;
            await DisplayAlertAsync("PersonaliPonto", "Servidor atualizado.", "OK");
        };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Children =
                {
                    Ui.Cabecalho("Configurações"),
                    new VerticalStackLayout
                    {
                        Padding = new Thickness(16), Spacing = 12,
                        Children =
                        {
                            Ui.L("Senha", 16, Ui.Titulo, true), Ui.CampoComRotulo("Senha atual", atual), Ui.CampoComRotulo("Nova senha", nova), trocar,
                            Ui.L("Servidor", 16, Ui.Titulo, true), Ui.CampoComRotulo("Endereço da API", servidor), salvar,
                            Ui.L("O relógio do aplicativo é sincronizado com o servidor oficial (NTP.br); alterar a hora do celular não altera suas marcações.", 12)
                        }
                    }
                }
            }
        };
    }
}
