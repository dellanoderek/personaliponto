using System.Text.Json;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Domain;

namespace PersonaliPonto.Core.RepP.Services;

/// <summary>Auditoria de ações sensíveis: quem, quando, o quê. Grava na mesma unidade de trabalho.</summary>
public sealed class AuditService(IRepPDbContext db, ITenantContext tenant, ICurrentUser user, IClock clock)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public void Registrar(string acao, string entidade, object? entidadeId = null, object? dados = null, Guid? tenantId = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId ?? tenant.TenantId,
            UsuarioId = user.UserId,
            UsuarioNome = user.Nome,
            Acao = acao,
            Entidade = entidade,
            EntidadeId = entidadeId?.ToString(),
            Dados = dados is null ? null : JsonSerializer.Serialize(dados, Json),
            Ip = user.Ip,
            Em = clock.UtcNow
        });
    }
}
