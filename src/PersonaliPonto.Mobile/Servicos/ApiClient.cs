using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Mobile.Servicos;

public sealed class ApiException(HttpStatusCode status, string mensagem) : Exception(mensagem)
{
    public HttpStatusCode Status { get; } = status;
}

/// <summary>Cliente HTTP da API PersonaliPonto com JWT e renovação automática por refresh token.</summary>
public sealed class ApiClient
{
    public const string ChaveServidor = "tc.servidor";
    private const string ChaveRefresh = "tc.refresh";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private string? _access;
    private DateTimeOffset _expira;
    private readonly SemaphoreSlim _renovando = new(1, 1);

    public event Action? SessaoEncerrada;

    public static string Servidor
    {
        get => Preferences.Default.Get(ChaveServidor, Configuracao.ServidorPadrao);
        set => Preferences.Default.Set(ChaveServidor, value.TrimEnd('/'));
    }

    public bool Autenticado => _access is not null;

    public async Task<TokenResponse> EntrarAsync(string login, string senha, string? codigoMfa = null)
    {
        var r = await _http.PostAsJsonAsync($"{Servidor}/api/auth/login", new LoginRequest(login, senha, codigoMfa), Json);
        var t = await LerAsync<TokenResponse>(r);
        await GuardarAsync(t);
        return t;
    }

    /// <summary>Restaura a sessão a partir do refresh token guardado no SecureStorage.</summary>
    public async Task<bool> RestaurarAsync()
    {
        var refresh = await SecureStorage.Default.GetAsync(ChaveRefresh);
        if (string.IsNullOrEmpty(refresh)) return false;
        try
        {
            var r = await _http.PostAsJsonAsync($"{Servidor}/api/auth/refresh", new RefreshRequest(refresh), Json);
            if (r.StatusCode == HttpStatusCode.Unauthorized) { await SairLocalAsync(); return false; }
            await GuardarAsync(await LerAsync<TokenResponse>(r));
            return true;
        }
        catch (HttpRequestException)
        {
            // Sem rede: mantém a sessão para permitir marcação off-line; o access token será renovado depois.
            return true;
        }
    }

    public async Task SairAsync()
    {
        var refresh = await SecureStorage.Default.GetAsync(ChaveRefresh);
        try { if (refresh is not null) await _http.PostAsJsonAsync($"{Servidor}/api/auth/logout", new RefreshRequest(refresh), Json); } catch { }
        await SairLocalAsync();
    }

    private Task SairLocalAsync()
    {
        _access = null;
        SecureStorage.Default.Remove(ChaveRefresh);
        return Task.CompletedTask;
    }

    private async Task GuardarAsync(TokenResponse t)
    {
        _access = t.AccessToken;
        _expira = t.ExpiraEm;
        await SecureStorage.Default.SetAsync(ChaveRefresh, t.RefreshToken);
    }

    private async Task GarantirTokenAsync()
    {
        if (_access is not null && _expira > DateTimeOffset.UtcNow.AddSeconds(30)) return;
        await _renovando.WaitAsync();
        try
        {
            if (_access is not null && _expira > DateTimeOffset.UtcNow.AddSeconds(30)) return;
            if (!await RestaurarAsync() || _access is null)
            {
                SessaoEncerrada?.Invoke();
                throw new ApiException(HttpStatusCode.Unauthorized, "Sessão expirada. Entre novamente.");
            }
        }
        finally
        {
            _renovando.Release();
        }
    }

    private async Task<HttpResponseMessage> EnviarAsync(Func<HttpRequestMessage> criar)
    {
        await GarantirTokenAsync();
        var req = criar();
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _access);
        var r = await _http.SendAsync(req);
        if (r.StatusCode == HttpStatusCode.Unauthorized)
        {
            _expira = DateTimeOffset.MinValue;
            await GarantirTokenAsync();
            req = criar();
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _access);
            r = await _http.SendAsync(req);
        }
        return r;
    }

    public async Task<T> GetAsync<T>(string rota) =>
        await LerAsync<T>(await EnviarAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{Servidor}{rota}")));

    public async Task<byte[]> GetBytesAsync(string rota)
    {
        var r = await EnviarAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{Servidor}{rota}"));
        await GarantirSucessoAsync(r);
        return await r.Content.ReadAsByteArrayAsync();
    }

    public async Task<(HttpStatusCode Status, T? Corpo, string? Erro)> PostAsync<T>(string rota, object corpo)
    {
        var r = await EnviarAsync(() => new HttpRequestMessage(HttpMethod.Post, $"{Servidor}{rota}") { Content = JsonContent.Create(corpo, options: Json) });
        if (r.IsSuccessStatusCode)
        {
            var texto = await r.Content.ReadAsStringAsync();
            return (r.StatusCode, string.IsNullOrWhiteSpace(texto) ? default : JsonSerializer.Deserialize<T>(texto, Json), null);
        }
        return (r.StatusCode, default, await MensagemAsync(r));
    }

    public async Task PostMultipartAsync(string rota, MultipartFormDataContent conteudo)
    {
        var bytes = await conteudo.ReadAsByteArrayAsync();
        var tipo = conteudo.Headers.ContentType;
        var r = await EnviarAsync(() =>
        {
            var c = new ByteArrayContent(bytes);
            c.Headers.ContentType = tipo;
            return new HttpRequestMessage(HttpMethod.Post, $"{Servidor}{rota}") { Content = c };
        });
        await GarantirSucessoAsync(r);
    }

    private static async Task<T> LerAsync<T>(HttpResponseMessage r)
    {
        await GarantirSucessoAsync(r);
        return (await r.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task GarantirSucessoAsync(HttpResponseMessage r)
    {
        if (!r.IsSuccessStatusCode) throw new ApiException(r.StatusCode, await MensagemAsync(r));
    }

    private static async Task<string> MensagemAsync(HttpResponseMessage r)
    {
        try
        {
            var p = await r.Content.ReadFromJsonAsync<JsonElement>(Json);
            if (p.TryGetProperty("title", out var t) && t.GetString() is { Length: > 0 } s) return s;
        }
        catch { }
        return r.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Não autorizado.",
            HttpStatusCode.TooManyRequests => "Muitas tentativas. Aguarde um instante.",
            _ => "Não foi possível falar com o servidor."
        };
    }
}
