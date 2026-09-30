using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Modules.RH.Domain;

namespace PersonaliPonto.Modules.RH.Services;

/// <summary>Assinatura (ciência) do espelho mensal pelo funcionário, com hash do conteúdo assinado.</summary>
public sealed class AssinaturaEspelhoService(IRhDbContext db, EspelhoService espelho, AuditService audit, ICurrentUser user, IClock clock)
{
    public Task<AssinaturaEspelho?> ObterAsync(Guid funcionarioId, int ano, int mes, CancellationToken ct) =>
        db.AssinaturasEspelho.AsNoTracking().Where(a => a.FuncionarioId == funcionarioId && a.Ano == ano && a.Mes == mes)
            .OrderByDescending(a => a.AssinadoEm).FirstOrDefaultAsync(ct);

    public async Task<AssinaturaEspelho> AssinarAsync(Guid funcionarioId, int ano, int mes, bool concorda, string? observacao, CancellationToken ct)
    {
        var inicio = new DateOnly(ano, mes, 1);
        var fim = inicio.AddMonths(1).AddDays(-1);
        if (fim >= DateOnly.FromDateTime(clock.UtcNow.UtcDateTime)) throw new RegraNegocioException("O espelho só pode ser assinado após o fim do mês.");
        if (!concorda && string.IsNullOrWhiteSpace(observacao)) throw new RegraNegocioException("Descreva o motivo da discordância.");
        if (await ObterAsync(funcionarioId, ano, mes, ct) is not null) throw new RegraNegocioException("Espelho deste mês já assinado.");

        var dto = await espelho.GerarAsync(funcionarioId, inicio, fim, ct);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(dto with { EmitidoEm = default }))));
        var f = await db.Funcionarios.AsNoTracking().FirstAsync(x => x.Id == funcionarioId, ct);
        var a = new AssinaturaEspelho
        {
            TenantId = f.TenantId, FuncionarioId = funcionarioId, Ano = ano, Mes = mes, Concorda = concorda,
            Observacao = observacao?.Trim(), HashEspelho = hash, Ip = user.Ip, AssinadoEm = clock.UtcNow
        };
        db.AssinaturasEspelho.Add(a);
        audit.Registrar("espelho.assinado", nameof(AssinaturaEspelho), a.Id, new { ano, mes, concorda });
        await db.SaveChangesAsync(ct);
        return a;
    }
}
