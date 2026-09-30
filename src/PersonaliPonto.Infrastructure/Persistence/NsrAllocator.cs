using Microsoft.EntityFrameworkCore;
using PersonaliPonto.Core.RepP.Abstractions;

namespace PersonaliPonto.Infrastructure.Persistence;

/// <summary>
/// NSR por estabelecimento com bloqueio de linha (UPDATE ... RETURNING): chamadas concorrentes são
/// serializadas até o commit da transação, garantindo sequência sem lacunas nem duplicidade.
/// Exige transação aberta.
/// </summary>
public sealed class NsrAllocator(PersonaliPontoDbContext db) : INsrAllocator
{
    private sealed record Linha(long UltimoNsr, string? UltimoHashTipo7);

    public async Task<NsrReservado> ProximoAsync(Guid tenantId, Guid estabelecimentoId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Alocação de NSR exige transação.");

        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO personaliponto.nsr_contadores (estabelecimento_id, tenant_id, ultimo_nsr) VALUES ({0}, {1}, 0) ON CONFLICT (estabelecimento_id) DO NOTHING",
            [estabelecimentoId, tenantId], ct);

        var linhas = await db.Database.SqlQueryRaw<Linha>(
                "UPDATE personaliponto.nsr_contadores SET ultimo_nsr = ultimo_nsr + 1 WHERE estabelecimento_id = {0} " +
                "RETURNING ultimo_nsr AS \"UltimoNsr\", ultimo_hash_tipo7 AS \"UltimoHashTipo7\"",
                estabelecimentoId)
            .ToListAsync(ct);
        var l = linhas.Single();
        return new NsrReservado(l.UltimoNsr, l.UltimoHashTipo7);
    }

    public Task AtualizarUltimoHashAsync(Guid estabelecimentoId, string hash, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync(
            "UPDATE personaliponto.nsr_contadores SET ultimo_hash_tipo7 = {0} WHERE estabelecimento_id = {1}",
            [hash, estabelecimentoId], ct);
}
