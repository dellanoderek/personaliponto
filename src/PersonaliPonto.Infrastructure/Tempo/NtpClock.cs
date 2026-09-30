using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonaliPonto.Core.RepP.Abstractions;

namespace PersonaliPonto.Infrastructure.Tempo;

public sealed class NtpOptions
{
    /// <summary>Servidores NTP.br (sincronizados com a Hora Legal Brasileira do Observatório Nacional).</summary>
    public string[] Servidores { get; set; } = ["a.st1.ntp.br", "b.st1.ntp.br", "c.st1.ntp.br", "d.st1.ntp.br", "a.ntp.br"];
    public int IntervaloMinutos { get; set; } = 10;
    /// <summary>Variação máxima tolerada (Anexo IX, item 2: 30 segundos).</summary>
    public int ToleranciaSegundos { get; set; } = 30;
    public bool Habilitado { get; set; } = true;
}

/// <summary>
/// Relógio oficial do REP-P: hora do servidor corrigida pelo desvio medido contra o NTP.br.
/// A correção é aplicada sempre; "Sincronizado" indica sincronização recente (últimos 60 min).
/// </summary>
public sealed class NtpClock : IClock
{
    private long _desvioTicks;
    private DateTimeOffset? _ultimaSync;

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow.AddTicks(Interlocked.Read(ref _desvioTicks));
    public TimeSpan? UltimoDesvio { get; private set; }
    public bool Sincronizado => _ultimaSync is { } s && DateTimeOffset.UtcNow - s < TimeSpan.FromMinutes(60);
    public DateTimeOffset? UltimaSincronizacao => _ultimaSync;
    public string? UltimoServidor { get; private set; }

    internal void Atualizar(TimeSpan desvio, string servidor)
    {
        Interlocked.Exchange(ref _desvioTicks, desvio.Ticks);
        UltimoDesvio = desvio;
        UltimoServidor = servidor;
        _ultimaSync = DateTimeOffset.UtcNow;
    }

    /// <summary>Consulta SNTP (RFC 4330). Retorna o desvio (NTP - local).</summary>
    public static async Task<TimeSpan> ConsultarAsync(string servidor, CancellationToken ct)
    {
        var dados = new byte[48];
        dados[0] = 0x1B; // LI=0, VN=3, Mode=3 (cliente)
        var enderecos = await Dns.GetHostAddressesAsync(servidor, ct);
        var ip = enderecos.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? enderecos.First();
        using var udp = new UdpClient(ip.AddressFamily);
        udp.Connect(ip, 123);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(3));
        var t1 = DateTimeOffset.UtcNow;
        await udp.SendAsync(dados, cts.Token);
        var resp = await udp.ReceiveAsync(cts.Token);
        var t4 = DateTimeOffset.UtcNow;
        var r = resp.Buffer;
        if (r.Length < 48) throw new InvalidOperationException("Resposta NTP inválida.");
        var t2 = Timestamp(r, 32);
        var t3 = Timestamp(r, 40);
        // offset = ((t2 - t1) + (t3 - t4)) / 2
        return TimeSpan.FromTicks(((t2 - t1).Ticks + (t3 - t4).Ticks) / 2);
    }

    private static DateTimeOffset Timestamp(byte[] b, int i)
    {
        ulong seg = (ulong)b[i] << 24 | (ulong)b[i + 1] << 16 | (ulong)b[i + 2] << 8 | b[i + 3];
        ulong frac = (ulong)b[i + 4] << 24 | (ulong)b[i + 5] << 16 | (ulong)b[i + 6] << 8 | b[i + 7];
        var ms = seg * 1000 + frac * 1000 / 0x100000000UL;
        return new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMilliseconds(ms);
    }
}

public sealed class NtpSyncService(NtpClock clock, IOptions<NtpOptions> options, ILogger<NtpSyncService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (!o.Habilitado)
        {
            log.LogWarning("Sincronização NTP desabilitada — usando relógio do servidor sem correção.");
            return;
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var s in o.Servidores)
            {
                try
                {
                    var desvio = await NtpClock.ConsultarAsync(s, stoppingToken);
                    clock.Atualizar(desvio, s);
                    if (Math.Abs(desvio.TotalSeconds) > o.ToleranciaSegundos)
                        log.LogWarning("Relógio do servidor com desvio de {Desvio:0.000}s em relação a {Servidor}; correção aplicada.", desvio.TotalSeconds, s);
                    else
                        log.LogDebug("NTP {Servidor}: desvio {Desvio:0.000}s", s, desvio.TotalSeconds);
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    log.LogWarning("Falha ao consultar NTP {Servidor}: {Erro}", s, ex.Message);
                }
            }
            try { await Task.Delay(TimeSpan.FromMinutes(o.IntervaloMinutos), stoppingToken); }
            catch (OperationCanceledException) { }
        }
    }
}
