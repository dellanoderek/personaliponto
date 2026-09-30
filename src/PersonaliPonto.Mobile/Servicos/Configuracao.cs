namespace PersonaliPonto.Mobile.Servicos;

public static class Configuracao
{
    /// <summary>Endereço padrão da API. Pode ser trocado na tela de configurações do app.</summary>
#if DEBUG && ANDROID
    public const string ServidorPadrao = "http://10.0.2.2:5174"; // emulador Android → localhost da máquina
#elif DEBUG
    public const string ServidorPadrao = "http://localhost:5174";
#else
    public const string ServidorPadrao = "https://api.personaliponto.com.br";
#endif

    public static string Hhmm(int minutos)
    {
        var s = minutos < 0 ? "-" : "";
        minutos = Math.Abs(minutos);
        return $"{s}{minutos / 60:00}:{minutos % 60:00}";
    }

    public static readonly System.Globalization.CultureInfo Br = new("pt-BR");

    /// <summary>Identificador estável desta instalação do app (usado no vínculo de aparelho).</summary>
    public static string DispositivoId
    {
        get
        {
            var id = Preferences.Default.Get("tc.dispositivo", "");
            if (id.Length == 0)
            {
                id = $"{DeviceInfo.Current.Platform}-{Guid.NewGuid():N}";
                Preferences.Default.Set("tc.dispositivo", id);
            }
            return $"{id}|{DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model}";
        }
    }
}
