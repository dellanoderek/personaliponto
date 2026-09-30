using Microsoft.EntityFrameworkCore;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Modules.SaaS.Domain;

namespace PersonaliPonto.Modules.SaaS.Services;

/// <summary>
/// Tabela global de municípios (IBGE). Importação opcional a partir de CSV "codigo_ibge;nome;uf"
/// (separador ";" ou ","; cabeçalho opcional). Idempotente: atualiza pelo código IBGE. Escrita só pela plataforma.
/// </summary>
public sealed class MunicipioService(ISaasDbContext db, ITenantContext contexto)
{
    public async Task<int> ImportarCsvAsync(TextReader csv, CancellationToken ct)
    {
        if (!contexto.IsSystem) throw new AcessoNegadoException("Somente a plataforma importa municípios.");
        var existentes = await db.Municipios.ToDictionaryAsync(m => m.CodigoIbge, ct);
        var n = 0;
        while (await csv.ReadLineAsync(ct) is { } linha)
        {
            var p = linha.Split(linha.Contains(';') ? ';' : ',', StringSplitOptions.TrimEntries);
            if (p.Length < 3 || !int.TryParse(p[0], out var codigo) || codigo is < 1000000 or > 9999999) continue;
            var uf = p[2].ToUpperInvariant();
            if (uf.Length != 2 || string.IsNullOrWhiteSpace(p[1])) continue;
            if (!existentes.TryGetValue(codigo, out var m))
            {
                m = new Municipio { CodigoIbge = codigo };
                db.Municipios.Add(m);
                existentes[codigo] = m;
            }
            m.Nome = p[1];
            m.Uf = uf;
            n++;
        }
        await db.SaveChangesAsync(ct);
        return n;
    }

    public Task<List<Municipio>> BuscarAsync(string? uf, string? nome, CancellationToken ct) =>
        db.Municipios.AsNoTracking()
            .Where(m => uf == null || m.Uf == uf)
            .Where(m => nome == null || m.Nome.ToLower().Contains(nome.ToLower()))
            .OrderBy(m => m.Uf).ThenBy(m => m.Nome).Take(50).ToListAsync(ct);
}
