using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Mobile.Servicos;

/// <summary>
/// Hora oficial no aparelho: âncora recebida do servidor + tempo monotônico decorrido desde então.
/// O relógio de parede do aparelho nunca é usado — alterar a hora do celular não altera a marcação.
/// </summary>
public sealed class RelogioOficial
{
    private const string Chave = "tc.ancora";
    private AncoraHoraDto? _ancora;
    private long _monotonicoNaAncora;

    public sealed record AncoraSalva(Guid AncoraId, DateTimeOffset HoraServidor, string Fuso, int OffsetMinutos, long Monotonico);

    public RelogioOficial()
    {
        var json = Preferences.Default.Get(Chave, "");
        if (json.Length == 0) return;
        try
        {
            var a = System.Text.Json.JsonSerializer.Deserialize<AncoraSalva>(json)!;
            // Reinício do aparelho zera o relógio monotônico: a âncora deixa de valer.
            if (Monotonico.Agora() >= a.Monotonico)
            {
                _ancora = new AncoraHoraDto(a.AncoraId, a.HoraServidor, a.Fuso, a.OffsetMinutos);
                _monotonicoNaAncora = a.Monotonico;
            }
        }
        catch { }
    }

    public bool Disponivel => _ancora is not null && Decorrido < TimeSpan.FromDays(7);
    public AncoraHoraDto? Ancora => _ancora;
    public TimeSpan Decorrido => TimeSpan.FromMilliseconds(Monotonico.Agora() - _monotonicoNaAncora);

    public DateTimeOffset? Agora => _ancora is null ? null : _ancora.HoraServidor + Decorrido;

    public DateTimeOffset? AgoraLocal => Agora?.ToOffset(TimeSpan.FromMinutes(_ancora!.OffsetMinutos));

    public void Definir(AncoraHoraDto ancora)
    {
        _ancora = ancora;
        _monotonicoNaAncora = Monotonico.Agora();
        Preferences.Default.Set(Chave, System.Text.Json.JsonSerializer.Serialize(
            new AncoraSalva(ancora.AncoraId, ancora.HoraServidor, ancora.FusoHorario, ancora.OffsetMinutos, _monotonicoNaAncora)));
    }
}

/// <summary>Relógio monotônico que continua contando com o aparelho em repouso.</summary>
public static class Monotonico
{
    public static long Agora()
    {
#if ANDROID
        return Android.OS.SystemClock.ElapsedRealtime();
#else
        return Environment.TickCount64;
#endif
    }
}
