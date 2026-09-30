namespace PersonaliPonto.Shared.Contracts;

/// <summary>Identificador do coletor (AFD registro tipo 7, campo 6).</summary>
public enum Coletor
{
    AplicativoMobile = 1,
    Browser = 2,
    AplicativoDesktop = 3,
    DispositivoEletronico = 4,
    Outro = 5
}

/// <summary>
/// Pedido de marcação enviado por qualquer coletor.
/// O horário oficial é sempre decidido pelo servidor. Para marcações off-line o coletor envia
/// o horário calculado a partir da última âncora de hora do servidor + relógio monotônico do
/// dispositivo (nunca o relógio de parede do dispositivo).
/// </summary>
public sealed record RegistrarMarcacaoRequest(
    Guid ClientId,
    bool Offline,
    DateTimeOffset? HorarioOfflineReferenciado,
    Guid? AncoraId,
    double? Latitude,
    double? Longitude,
    string? DispositivoId);

public sealed record MarcacaoRegistradaDto(
    Guid Id,
    Guid ClientId,
    long Nsr,
    DateTimeOffset DataHoraMarcacao,
    DateTimeOffset DataHoraGravacao,
    bool Offline,
    string Hash,
    string FuncionarioNome,
    bool Duplicada);

public sealed record UltimaMarcacaoDto(
    Guid Id,
    long Nsr,
    DateTimeOffset DataHoraMarcacao,
    bool Offline,
    string Hash);

/// <summary>Âncora de hora do servidor usada pelos coletores para marcação off-line.</summary>
public sealed record AncoraHoraDto(Guid AncoraId, DateTimeOffset HoraServidor, string FusoHorario, int OffsetMinutos);

/// <summary>Estados da fila off-line do coletor (app e terminal).</summary>
public enum EstadoSincronizacao
{
    Pending,
    Syncing,
    Synced,
    Failed,
    Retry,
    Conflict
}
