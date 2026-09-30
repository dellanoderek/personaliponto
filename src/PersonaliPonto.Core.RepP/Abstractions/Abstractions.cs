namespace PersonaliPonto.Core.RepP.Abstractions;

/// <summary>
/// Contexto de tenant da requisição/circuito atual. É a única fonte do TenantId usado pelos
/// filtros globais do EF Core e pela variável de sessão do Postgres usada nas políticas de RLS.
/// </summary>
public interface ITenantContext
{
    Guid? TenantId { get; }

    /// <summary>Operação de plataforma (Super Admin, login, jobs). Ignora o filtro de tenant.</summary>
    bool IsSystem { get; }
}

/// <summary>
/// Relógio oficial. Referência única: hora do servidor sincronizada por NTP com a Hora Legal
/// Brasileira (Anexo IX, item 2 — variação máxima de 30s). O dispositivo nunca é fonte de verdade.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }

    /// <summary>Diferença (servidor - NTP) observada na última sincronização.</summary>
    TimeSpan? UltimoDesvio { get; }

    /// <summary>Verdadeiro quando a última sincronização NTP está dentro da tolerância legal.</summary>
    bool Sincronizado { get; }
}

public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Nome { get; }
    string? Cpf { get; }
    string? Ip { get; }
}

/// <summary>Aloca NSR sequencial por estabelecimento, dentro da transação corrente (sem lacunas).</summary>
public interface INsrAllocator
{
    Task<NsrReservado> ProximoAsync(Guid tenantId, Guid estabelecimentoId, CancellationToken ct);

    Task AtualizarUltimoHashAsync(Guid estabelecimentoId, string hash, CancellationToken ct);
}

public sealed record NsrReservado(long Nsr, string? UltimoHashTipo7);

/// <summary>Assinatura CAdES destacada (.p7s) e PAdES com certificado ICP-Brasil.</summary>
public interface IAssinaturaDigital
{
    bool CertificadoIcpBrasil { get; }
    string? TitularCertificado { get; }
    byte[] AssinarCadesDestacado(byte[] conteudo);
    byte[] AssinarPdf(byte[] pdf, string motivo);
}

/// <summary>Validação opcional executada antes da gravação de uma marcação (ex.: geofence — Fase 2).</summary>
public interface IMarcacaoInterceptor
{
    Task<MarcacaoAvaliacao> AvaliarAsync(MarcacaoContextoEntrada entrada, CancellationToken ct);
}

public sealed record MarcacaoContextoEntrada(
    Guid TenantId,
    Guid FuncionarioId,
    Guid EstabelecimentoId,
    double? Latitude,
    double? Longitude,
    Shared.Contracts.Coletor Coletor,
    string? DispositivoId = null);

public sealed record MarcacaoAvaliacao(bool Bloquear, bool? DentroCerca, string? Mensagem)
{
    public static readonly MarcacaoAvaliacao Livre = new(false, null, null);
}
