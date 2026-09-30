using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Modules.RH;
using PersonaliPonto.Modules.RH.Domain;
using PersonaliPonto.Modules.SaaS;
using PersonaliPonto.Modules.SaaS.Domain;

namespace PersonaliPonto.Infrastructure.Persistence;

/// <summary>
/// DbContext único da aplicação. Isolamento multi-tenant em duas camadas:
/// (1) filtros globais do EF Core por TenantId; (2) Row-Level Security no PostgreSQL
/// (variável de sessão app.tenant_id aplicada pelo <see cref="TenantSessionInterceptor"/>).
/// </summary>
public class PersonaliPontoDbContext(DbContextOptions<PersonaliPontoDbContext> options, ITenantContext tenant)
    : DbContext(options), IRhDbContext, ISaasDbContext
{
    public const string Schema = "personaliponto";

    public ITenantContext TenantContext { get; } = tenant;

    // Usados pelos filtros globais (parametrizados por instância).
    private Guid? TenantAtual => TenantContext.TenantId;
    private bool Sistema => TenantContext.IsSystem;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Empregador> Empregadores => Set<Empregador>();
    public DbSet<Estabelecimento> Estabelecimentos => Set<Estabelecimento>();
    public DbSet<Funcionario> Funcionarios => Set<Funcionario>();
    public DbSet<Jornada> Jornadas => Set<Jornada>();
    public DbSet<RegistroRep> RegistrosRep => Set<RegistroRep>();
    public DbSet<MarcacaoContexto> MarcacoesContexto => Set<MarcacaoContexto>();
    public DbSet<NsrContador> NsrContadores => Set<NsrContador>();
    public DbSet<Tratamento> Tratamentos => Set<Tratamento>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<TerminalWeb> TerminaisWeb => Set<TerminalWeb>();
    public DbSet<AncoraHora> AncorasHora => Set<AncoraHora>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<ArquivoArmazenado> Arquivos => Set<ArquivoArmazenado>();

    public DbSet<SolicitacaoCorrecao> Solicitacoes => Set<SolicitacaoCorrecao>();
    public DbSet<Atestado> Atestados => Set<Atestado>();
    public DbSet<BancoHorasLancamento> BancoHoras => Set<BancoHorasLancamento>();
    public DbSet<Feriado> Feriados => Set<Feriado>();
    public DbSet<EscalaFuncionario> Escalas => Set<EscalaFuncionario>();
    public DbSet<FechamentoPeriodo> Fechamentos => Set<FechamentoPeriodo>();
    public DbSet<PoliticaBancoHoras> PoliticasBancoHoras => Set<PoliticaBancoHoras>();
    public DbSet<AssinaturaEspelho> AssinaturasEspelho => Set<AssinaturaEspelho>();
    public DbSet<ConfiguracaoSeguranca> ConfiguracoesSeguranca => Set<ConfiguracaoSeguranca>();
    public DbSet<DispositivoFuncionario> Dispositivos => Set<DispositivoFuncionario>();
    public DbSet<FotoMarcacao> FotosMarcacao => Set<FotoMarcacao>();

    public DbSet<Plano> Planos => Set<Plano>();
    public DbSet<Assinatura> Assinaturas => Set<Assinatura>();
    public DbSet<Fatura> Faturas => Set<Fatura>();
    public DbSet<AcessoSuporte> AcessosSuporte => Set<AcessoSuporte>();

    /// <summary>O PostgreSQL (timestamptz) só aceita UTC: normaliza qualquer offset na gravação e nos parâmetros.</summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        c.Properties<DateTimeOffset>().HaveConversion<UtcConverter>();
        c.Properties<DateTimeOffset?>().HaveConversion<UtcConverterNulo>();
    }

    private sealed class UtcConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset, DateTimeOffset>(
        v => v.ToUniversalTime(), v => v);

    private sealed class UtcConverterNulo() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset?, DateTimeOffset?>(
        v => v.HasValue ? v.Value.ToUniversalTime() : v, v => v);

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

        b.Entity<Tenant>(e =>
        {
            e.Property(x => x.Nome).HasMaxLength(200);
            e.Property(x => x.Slug).HasMaxLength(60);
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasQueryFilter(x => Sistema || x.Id == TenantAtual);
        });

        b.Entity<Empregador>(e =>
        {
            e.Property(x => x.RazaoSocial).HasMaxLength(150);
            e.Property(x => x.Identificador).HasMaxLength(14);
        });

        b.Entity<Estabelecimento>(e =>
        {
            e.Property(x => x.Nome).HasMaxLength(120);
            e.Property(x => x.Identificador).HasMaxLength(14);
            e.Property(x => x.CnoCaepf).HasMaxLength(14);
            e.Property(x => x.LocalPrestacao).HasMaxLength(100);
            e.Property(x => x.FusoHorario).HasMaxLength(60);
            e.HasIndex(x => new { x.TenantId, x.Identificador }).IsUnique();
            e.HasOne(x => x.Empregador).WithMany().HasForeignKey(x => x.EmpregadorId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Funcionario>(e =>
        {
            e.Property(x => x.Nome).HasMaxLength(150);
            e.Property(x => x.Cpf).HasMaxLength(11);
            e.Property(x => x.Matricula).HasMaxLength(30);
            e.Property(x => x.MatriculaEsocial).HasMaxLength(30);
            e.HasIndex(x => new { x.TenantId, x.EstabelecimentoId, x.Matricula }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Cpf });
            e.HasOne(x => x.Estabelecimento).WithMany().HasForeignKey(x => x.EstabelecimentoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Jornada).WithMany().HasForeignKey(x => x.JornadaId).OnDelete(DeleteBehavior.Restrict);
        });

        var jsonOpts = new JsonSerializerOptions();
        b.Entity<Jornada>(e =>
        {
            e.Property(x => x.Codigo).HasMaxLength(20);
            e.HasIndex(x => new { x.TenantId, x.Codigo }).IsUnique();
            e.Property(x => x.Horarios)
                .HasColumnType("jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, jsonOpts),
                    v => JsonSerializer.Deserialize<List<HorarioDia>>(v, jsonOpts) ?? new List<HorarioDia>(),
                    new ValueComparer<List<HorarioDia>>(
                        (a, c) => JsonSerializer.Serialize(a, jsonOpts) == JsonSerializer.Serialize(c, jsonOpts),
                        v => JsonSerializer.Serialize(v, jsonOpts).GetHashCode(),
                        v => JsonSerializer.Deserialize<List<HorarioDia>>(JsonSerializer.Serialize(v, jsonOpts), jsonOpts)!));
        });

        b.Entity<RegistroRep>(e =>
        {
            e.HasIndex(x => new { x.EstabelecimentoId, x.Nsr }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.ClientId }).IsUnique().HasFilter("client_id IS NOT NULL");
            e.HasIndex(x => new { x.TenantId, x.FuncionarioId, x.DataHoraMarcacao });
            e.HasIndex(x => new { x.EstabelecimentoId, x.DataHoraGravacao });
            e.Property(x => x.Cpf).HasMaxLength(11);
            e.Property(x => x.Hash).HasMaxLength(64);
            e.Property(x => x.IdentificadorEmpregador).HasMaxLength(14);
            e.Property(x => x.CnoCaepf).HasMaxLength(14);
            e.Property(x => x.RazaoSocial).HasMaxLength(150);
            e.Property(x => x.LocalPrestacao).HasMaxLength(100);
            e.Property(x => x.CpfResponsavel).HasMaxLength(11);
            e.Property(x => x.NomeEmpregado).HasMaxLength(150);
            e.Property(x => x.DemaisDados).HasMaxLength(4);
            e.HasOne<Estabelecimento>().WithMany().HasForeignKey(x => x.EstabelecimentoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Funcionario>().WithMany().HasForeignKey(x => x.FuncionarioId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<MarcacaoContexto>(e =>
        {
            e.HasIndex(x => x.RegistroRepId).IsUnique();
            e.HasOne<RegistroRep>().WithMany().HasForeignKey(x => x.RegistroRepId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<NsrContador>(e =>
        {
            e.HasKey(x => x.EstabelecimentoId);
            e.Property(x => x.UltimoHashTipo7).HasMaxLength(64);
            e.HasQueryFilter(x => Sistema || x.TenantId == TenantAtual);
        });

        b.Entity<Tratamento>(e =>
        {
            e.Property(x => x.Motivo).HasMaxLength(150);
            e.HasIndex(x => new { x.TenantId, x.FuncionarioId, x.DataHora });
            e.HasIndex(x => x.RegistroRepId);
            e.HasIndex(x => x.TratamentoRevogadoId);
        });

        b.Entity<AuditLog>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Em });
            e.Property(x => x.Dados).HasColumnType("jsonb");
            e.HasQueryFilter(x => Sistema || x.TenantId == TenantAtual);
        });

        b.Entity<Usuario>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(200);
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Cpf).HasMaxLength(11);
            e.Property(x => x.Papel).HasMaxLength(30);
            e.HasQueryFilter(x => Sistema || x.TenantId == TenantAtual);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasQueryFilter(x => Sistema || x.TenantId == TenantAtual);
        });

        b.Entity<TerminalWeb>(e => e.HasIndex(x => x.TokenHash).IsUnique());

        b.Entity<OutboxMessage>(e =>
        {
            e.HasIndex(x => new { x.ProcessadoEm, x.ProximaTentativa });
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.HasQueryFilter(x => Sistema || x.TenantId == TenantAtual);
        });

        b.Entity<ArquivoArmazenado>(e => e.HasIndex(x => new { x.TenantId, x.CriadoEm }));

        // RH
        b.Entity<SolicitacaoCorrecao>(e =>
        {
            e.Property(x => x.Motivo).HasMaxLength(150);
            e.HasIndex(x => new { x.TenantId, x.Status });
            e.HasIndex(x => x.FuncionarioId);
        });
        b.Entity<Atestado>(e => e.HasIndex(x => new { x.TenantId, x.FuncionarioId, x.DataInicio }));
        b.Entity<BancoHorasLancamento>(e => e.HasIndex(x => new { x.TenantId, x.FuncionarioId, x.Data }));
        b.Entity<Feriado>(e => e.HasIndex(x => new { x.TenantId, x.Data }));
        b.Entity<EscalaFuncionario>(e => e.HasIndex(x => new { x.FuncionarioId, x.Inicio }).IsUnique());
        b.Entity<FechamentoPeriodo>(e => e.HasIndex(x => new { x.FuncionarioId, x.Ano, x.Mes }).IsUnique());
        b.Entity<PoliticaBancoHoras>(e => e.HasIndex(x => x.TenantId).IsUnique());
        b.Entity<AssinaturaEspelho>(e => e.HasIndex(x => new { x.FuncionarioId, x.Ano, x.Mes }).IsUnique());
        b.Entity<ConfiguracaoSeguranca>(e => e.HasIndex(x => x.TenantId).IsUnique());
        b.Entity<DispositivoFuncionario>(e =>
        {
            e.Property(x => x.DispositivoId).HasMaxLength(120);
            e.HasIndex(x => new { x.FuncionarioId, x.DispositivoId }).IsUnique();
        });
        b.Entity<FotoMarcacao>(e =>
        {
            e.HasIndex(x => x.RegistroRepId).IsUnique();
            e.HasIndex(x => new { x.ExcluidaEm, x.ExpiraEm });
        });

        // SaaS
        b.Entity<Plano>(e => e.Property(x => x.PrecoMensal).HasPrecision(12, 2));
        b.Entity<Assinatura>(e =>
        {
            e.HasIndex(x => x.TenantId).IsUnique();
            e.HasIndex(x => x.Cnpj);
            e.Property(x => x.ValorMensal).HasPrecision(12, 2);
            e.Property(x => x.CustoMensal).HasPrecision(12, 2);
            e.HasOne(x => x.Plano).WithMany().HasForeignKey(x => x.PlanoId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<Fatura>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Competencia }).IsUnique();
            e.Property(x => x.Valor).HasPrecision(12, 2);
            e.Property(x => x.ValorPago).HasPrecision(12, 2);
        });
        b.Entity<AcessoSuporte>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Inicio });
            e.HasQueryFilter(x => Sistema);
        });

        // Filtro de tenant para todas as entidades ITenantEntity que ainda não têm filtro próprio.
        foreach (var et in b.Model.GetEntityTypes())
        {
            if (!typeof(ITenantEntity).IsAssignableFrom(et.ClrType) || et.GetDeclaredQueryFilters().Count > 0) continue;
            var metodo = typeof(PersonaliPontoDbContext).GetMethod(nameof(AplicarFiltroTenant), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .MakeGenericMethod(et.ClrType);
            metodo.Invoke(this, [b]);
        }

        AplicarSnakeCase(b);
    }

    private void AplicarFiltroTenant<T>(ModelBuilder b) where T : class, ITenantEntity =>
        b.Entity<T>().HasQueryFilter(x => Sistema || x.TenantId == TenantAtual);

    private static void AplicarSnakeCase(ModelBuilder b)
    {
        foreach (var et in b.Model.GetEntityTypes())
        {
            et.SetTableName(Snake(et.GetTableName()!));
            foreach (var p in et.GetProperties()) p.SetColumnName(Snake(p.Name));
            foreach (var k in et.GetKeys()) k.SetName(Snake(k.GetName()!));
            foreach (var fk in et.GetForeignKeys()) fk.SetConstraintName(Snake(fk.GetConstraintName()!));
            foreach (var ix in et.GetIndexes()) ix.SetDatabaseName(Snake(ix.GetDatabaseName()!));
        }
    }

    public static string Snake(string nome)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < nome.Length; i++)
        {
            var c = nome[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && nome[i - 1] != '_' && (char.IsLower(nome[i - 1]) || (i + 1 < nome.Length && char.IsLower(nome[i + 1]))))
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    // ---------------- Salvaguardas de gravação ----------------

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        Validar();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        Validar();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void Validar()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is IImmutableEntity && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException($"{entry.Entity.GetType().Name} é imutável: registros originais não podem ser alterados nem excluídos.");

            if (entry.Entity is ITenantEntity te)
            {
                if (entry.State == EntityState.Added && te.TenantId == Guid.Empty && TenantAtual is { } t) te.TenantId = t;
                if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                {
                    if (te.TenantId == Guid.Empty) throw new InvalidOperationException($"{entry.Entity.GetType().Name} sem TenantId.");
                    if (!Sistema && te.TenantId != TenantAtual)
                        throw new UnauthorizedAccessException("Tentativa de gravar dados de outro tenant.");
                    if (entry.State == EntityState.Modified && entry.Property(nameof(ITenantEntity.TenantId)).IsModified)
                        throw new UnauthorizedAccessException("TenantId não pode ser alterado.");
                }
            }
        }
    }
}
