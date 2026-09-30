using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PersonaliPonto.Core.RepP.Domain;

namespace PersonaliPonto.Core.RepP.Abstractions;

/// <summary>Acesso a dados do núcleo REP-P. Implementado pelo DbContext da infraestrutura.</summary>
public interface IRepPDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<Empregador> Empregadores { get; }
    DbSet<Estabelecimento> Estabelecimentos { get; }
    DbSet<Funcionario> Funcionarios { get; }
    DbSet<Jornada> Jornadas { get; }
    DbSet<RegistroRep> RegistrosRep { get; }
    DbSet<MarcacaoContexto> MarcacoesContexto { get; }
    DbSet<Tratamento> Tratamentos { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<Usuario> Usuarios { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<TerminalWeb> TerminaisWeb { get; }
    DbSet<AncoraHora> AncorasHora { get; }
    DbSet<OutboxMessage> Outbox { get; }
    DbSet<ArquivoArmazenado> Arquivos { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
