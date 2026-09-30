using System.Net;
using System.Text.Json;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Mobile.Servicos;

public sealed class ItemFila
{
    public Guid ClientId { get; set; }
    public DateTimeOffset Horario { get; set; }
    public Guid AncoraId { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public EstadoSincronizacao Estado { get; set; }
    public int Tentativas { get; set; }
    public string? Mensagem { get; set; }
    public long? Nsr { get; set; }
    public Guid? RegistroId { get; set; }
}

/// <summary>
/// Fila local de marcações (decisão 8 + Fase 1): toda marcação é gravada primeiro no aparelho e depois enviada.
/// Estados PENDING → SYNCING → SYNCED / FAILED / RETRY / CONFLICT; idempotência pelo ClientId (UUID).
/// </summary>
public sealed class FilaMarcacoes(ApiClient api)
{
    private readonly string _arquivo = Path.Combine(FileSystem.AppDataDirectory, "fila-marcacoes.json");
    private readonly SemaphoreSlim _lock = new(1, 1);
    public event Action? Mudou;

    public async Task<List<ItemFila>> ItensAsync()
    {
        await _lock.WaitAsync();
        try { return Ler(); }
        finally { _lock.Release(); }
    }

    private List<ItemFila> Ler()
    {
        try { return File.Exists(_arquivo) ? JsonSerializer.Deserialize<List<ItemFila>>(File.ReadAllText(_arquivo)) ?? [] : []; }
        catch { return []; }
    }

    private void Gravar(List<ItemFila> itens)
    {
        var tmp = _arquivo + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(itens));
        File.Move(tmp, _arquivo, true);
    }

    public async Task AdicionarAsync(ItemFila item)
    {
        await _lock.WaitAsync();
        try
        {
            var l = Ler();
            l.Add(item);
            Gravar(l);
        }
        finally { _lock.Release(); }
        Mudou?.Invoke();
    }

    private async Task AtualizarAsync(Guid id, Action<ItemFila> acao)
    {
        await _lock.WaitAsync();
        try
        {
            var l = Ler();
            var i = l.FirstOrDefault(x => x.ClientId == id);
            if (i is not null) acao(i);
            // Mantém apenas o necessário: sincronizados há mais de 7 dias saem da fila.
            l.RemoveAll(x => x.Estado == EstadoSincronizacao.Synced && DateTimeOffset.UtcNow - x.Horario > TimeSpan.FromDays(7));
            Gravar(l);
        }
        finally { _lock.Release(); }
        Mudou?.Invoke();
    }

    /// <summary>Envia um item; retorna o DTO quando registrado no servidor.</summary>
    public async Task<MarcacaoRegistradaDto?> EnviarAsync(ItemFila item, bool offline)
    {
        await AtualizarAsync(item.ClientId, i => i.Estado = EstadoSincronizacao.Syncing);
        try
        {
            var req = new RegistrarMarcacaoRequest(item.ClientId, offline, offline ? item.Horario : null, offline ? item.AncoraId : null,
                item.Latitude, item.Longitude, Configuracao.DispositivoId);
            var (status, corpo, erro) = await api.PostAsync<MarcacaoRegistradaDto>("/api/app/marcacoes", req);
            if (corpo is not null && (int)status < 300)
            {
                await AtualizarAsync(item.ClientId, i => { i.Estado = EstadoSincronizacao.Synced; i.Nsr = corpo.Nsr; i.RegistroId = corpo.Id; i.Horario = corpo.DataHoraMarcacao; i.Mensagem = null; });
                return corpo;
            }
            var estado = status switch
            {
                HttpStatusCode.Conflict => EstadoSincronizacao.Conflict,
                HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest or HttpStatusCode.Forbidden => EstadoSincronizacao.Failed,
                _ => EstadoSincronizacao.Retry
            };
            await AtualizarAsync(item.ClientId, i => { i.Estado = estado; i.Mensagem = erro; i.Tentativas++; });
            if (estado != EstadoSincronizacao.Retry) throw new ApiException(status, erro ?? "Marcação recusada.");
            return null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            await AtualizarAsync(item.ClientId, i => { i.Estado = EstadoSincronizacao.Pending; i.Tentativas++; });
            return null;
        }
    }

    /// <summary>Reenvia pendências como marcações off-line (horário referenciado na âncora do servidor).</summary>
    public async Task<int> SincronizarAsync()
    {
        var enviados = 0;
        foreach (var i in (await ItensAsync()).Where(x => x.Estado is EstadoSincronizacao.Pending or EstadoSincronizacao.Retry))
        {
            try
            {
                if (await EnviarAsync(i, offline: true) is not null) enviados++;
                else break; // sem rede: tenta depois
            }
            catch (ApiException) { /* conflito/falha já registrados no item */ }
        }
        return enviados;
    }
}
