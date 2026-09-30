using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Mobile.Servicos;

public sealed record Perfil(string Nome, string Cpf, string Matricula, string? Cargo, string Empresa, string Estabelecimento,
    string? Jornada, bool PermiteOffline, bool DeveTrocarSenha, int SaldoBancoHoras, string FusoHorario, bool FotoNaMarcacao = false);

/// <summary>Estado da sessão do funcionário no app (perfil em cache para uso off-line).</summary>
public sealed class Sessao(ApiClient api, RelogioOficial relogio, FilaMarcacoes fila)
{
    private const string ChavePerfil = "tc.perfil";
    public Perfil? Perfil { get; private set; }

    public async Task CarregarAsync()
    {
        try
        {
            Perfil = await api.GetAsync<Perfil>("/api/app/perfil");
            Preferences.Default.Set(ChavePerfil, System.Text.Json.JsonSerializer.Serialize(Perfil));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            var json = Preferences.Default.Get(ChavePerfil, "");
            if (json.Length > 0) Perfil = System.Text.Json.JsonSerializer.Deserialize<Perfil>(json);
        }
        await SincronizarHoraAsync();
    }

    /// <summary>Busca nova âncora de hora no servidor (quando há conexão) e envia pendências.</summary>
    public async Task<bool> SincronizarHoraAsync()
    {
        try
        {
            relogio.Definir(await api.GetAsync<AncoraHoraDto>("/api/app/tempo"));
            await fila.SincronizarAsync();
            return true;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    public async Task SairAsync()
    {
        await api.SairAsync();
        Perfil = null;
        Preferences.Default.Remove(ChavePerfil);
    }
}
