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
    private Guid[] CanaisCanal => [.. TenantContext.CanaisVisiveis];
    private Guid[] TenantsCanal => [.. TenantContext.TenantsCanal];
    private Guid? CanalAtual => TenantContext.CanalId;

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
    public DbSet<Canal> Canais => Set<Canal>();
    public DbSet<Municipio> Municipios => Set<Municipio>();
    public DbSet<TabelaPrecoCanal> TabelasPrecoCanal => Set<TabelaPrecoCanal>();
    public DbSet<ApuracaoUsoCanal> ApuracoesUsoCanal => Set<ApuracaoUsoCanal>();
    public DbSet<ApuracaoUsoTenant> ApuracoesUsoTenant => Set<ApuracaoUsoTenant>();
    public DbSet<FaturaCanal> FaturasCanal => Set<FaturaCanal>();
    public DbSet<ItemFaturaCanal> ItensFaturaCanal => Set<ItemFaturaCanal>();
    public DbSet<AssinaturaPremium> AssinaturasPremium => Set<AssinaturaPremium>();
    public DbSet<ContaGatewayCanal> ContasGateway => Set<ContaGatewayCanal>();
    public DbSet<EventoGateway> EventosGateway => Set<EventoGateway>();

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
            e.HasIndex(x => x.CanalDonoId);
            e.HasIndex(x => x.MunicipioId);
            e.HasOne<Canal>().WithMany().HasForeignKey(x => x.CanalDonoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Municipio>().WithMany().HasForeignKey(x => x.MunicipioId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => Sistema || x.Id == TenantAtual || TenantsCanal.Contains(x.Id));
        });

        // Canais (revenda): o usuário de canal vê o próprio canal e os descendentes.
        b.Entity<Canal>(e =>
        {
            e.Property(x => x.Caminho).HasMaxLength(200);
            e.Property(x => x.Slug).HasMaxLength(60);
            e.Property(x => x.RazaoSocial).HasMaxLength(150);
            e.Property(x => x.Cnpj).HasMaxLength(14);
            e.Property(x => x.NomeMarca).HasMaxLength(80);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Telefone).HasMaxLength(30);
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => x.Caminho).IsUnique();
            e.HasIndex(x => x.CanalPaiId);
            // Um único Owner.
            e.HasIndex(x => x.Tipo).IsUnique().HasFilter("tipo = 1").HasDatabaseName("ix_canais_owner_unico");
            e.HasOne<Canal>().WithMany().HasForeignKey(x => x.CanalPaiId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_canais_nivel", "nivel BETWEEN 1 AND 3 AND nivel = tipo");
                t.HasCheckConstraint("ck_canais_pai", "(tipo = 1) = (canal_pai_id IS NULL)");
            });
            e.HasQueryFilter(x => Sistema || CanaisCanal.Contains(x.Id));
        });

        b.Entity<Municipio>(e =>
        {
            e.Property(x => x.Nome).HasMaxLength(120);
            e.Property(x => x.Uf).HasMaxLength(2).IsFixedLength();
            e.HasIndex(x => x.CodigoIbge).IsUnique();
            e.HasIndex(x => new { x.Uf, x.Nome });
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
            e.HasIndex(x => new { x.CanalId, x.Em });
            e.Property(x => x.Dados).HasColumnType("jsonb");
            e.HasQueryFilter(x => Sistema || x.TenantId == TenantAtual || (x.CanalId != null && CanaisCanal.Contains(x.CanalId.Value)));
        });

        b.Entity<Usuario>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(200);
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Cpf).HasMaxLength(11);
            e.Property(x => x.Papel).HasMaxLength(30);
            e.HasIndex(x => x.CanalId);
            e.HasOne<Canal>().WithMany().HasForeignKey(x => x.CanalId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("ck_usuarios_escopo",
                "(escopo = 0 AND tenant_id IS NULL AND canal_id IS NULL) OR (escopo = 1 AND canal_id IS NOT NULL AND tenant_id IS NULL) OR (escopo = 2 AND tenant_id IS NOT NULL AND canal_id IS NULL)"));
            e.HasQueryFilter(x => Sistema || x.TenantId == TenantAtual || (x.CanalId != null && CanaisCanal.Contains(x.CanalId.Value)));
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
            ConfigurarGateway(e);
        });
        b.Entity<AcessoSuporte>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Inicio });
            e.HasIndex(x => new { x.CanalId, x.Inicio });
            // A plataforma vê tudo; o canal vê os acessos iniciados por ele ou por canais descendentes.
            e.HasQueryFilter(x => Sistema || (x.CanalId != null && CanaisCanal.Contains(x.CanalId.Value)));
        });

        // Faturamento de canais (E2)
        b.Entity<Canal>(e =>
        {
            e.Property(x => x.PrecoFuncionarioParceiro).HasPrecision(12, 4);
            e.Property(x => x.MinimoMensalParceiro).HasPrecision(12, 2);
        });
        b.Entity<TabelaPrecoCanal>(e =>
        {
            e.HasIndex(x => x.VigenciaInicio).IsUnique();
            e.Property(x => x.Observacao).HasMaxLength(300);
            foreach (var p in new[] { nameof(TabelaPrecoCanal.PrecoEmpresaAtiva), nameof(TabelaPrecoCanal.Faixa1Preco), nameof(TabelaPrecoCanal.Faixa2Preco),
                         nameof(TabelaPrecoCanal.Faixa3Preco), nameof(TabelaPrecoCanal.MinimoMensal), nameof(TabelaPrecoCanal.PrecoPremium),
                         nameof(TabelaPrecoCanal.AppProprioImplantacao), nameof(TabelaPrecoCanal.AppProprioMensal) })
                e.Property(p).HasPrecision(12, 4);
            e.ToTable(t => t.HasCheckConstraint("ck_tabelas_preco_canal_faixas", "faixa1_ate > 0 AND faixa2_ate > faixa1_ate"));
        });
        b.Entity<ApuracaoUsoCanal>(e =>
        {
            e.HasIndex(x => new { x.CanalId, x.Competencia }).IsUnique();
            e.HasOne<Canal>().WithMany().HasForeignKey(x => x.CanalId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => Sistema || CanaisCanal.Contains(x.CanalId));
        });
        b.Entity<ApuracaoUsoTenant>(e =>
        {
            e.Property(x => x.NomeCliente).HasMaxLength(200);
            e.HasIndex(x => new { x.ApuracaoId, x.ClienteId }).IsUnique();
            e.HasIndex(x => x.CanalId);
            e.HasOne<ApuracaoUsoCanal>().WithMany().HasForeignKey(x => x.ApuracaoId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => Sistema || CanaisCanal.Contains(x.CanalId));
        });
        b.Entity<FaturaCanal>(e =>
        {
            // Uma fatura mensal por par pagador/recebedor/competência; avulsas (compras no painel) ficam fora da unicidade.
            e.HasIndex(x => new { x.CanalPagadorId, x.CanalRecebedorId, x.Competencia }).IsUnique().HasFilter("tipo <> 3")
                .HasDatabaseName("ix_faturas_canal_mensal_unica");
            e.HasIndex(x => x.CanalPagadorId);
            ConfigurarGateway(e);
            e.HasIndex(x => new { x.CanalRecebedorId, x.Competencia });
            e.Property(x => x.Valor).HasPrecision(12, 2);
            e.Property(x => x.ValorPago).HasPrecision(12, 2);
            e.Property(x => x.FormaPagamento).HasMaxLength(40);
            e.Property(x => x.Observacao).HasMaxLength(500);
            e.HasOne<Canal>().WithMany().HasForeignKey(x => x.CanalPagadorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Canal>().WithMany().HasForeignKey(x => x.CanalRecebedorId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Itens).WithOne().HasForeignKey(x => x.FaturaCanalId).OnDelete(DeleteBehavior.Cascade);
            // Pagador e recebedor enxergam; o parceiro nunca vê a fatura do revendedor com o Owner.
            e.HasQueryFilter(x => Sistema || CanaisCanal.Contains(x.CanalPagadorId) || x.CanalRecebedorId == CanalAtual);
        });
        b.Entity<ItemFaturaCanal>(e =>
        {
            e.Property(x => x.Descricao).HasMaxLength(200);
            e.Property(x => x.Quantidade).HasPrecision(12, 2);
            e.Property(x => x.ValorUnitario).HasPrecision(12, 4);
            e.Property(x => x.Valor).HasPrecision(12, 2);
        });
        b.Entity<AssinaturaPremium>(e =>
        {
            e.HasIndex(x => x.CanalId);
            e.HasIndex(x => x.CanalId).IsUnique().HasFilter("cancelada_em IS NULL").HasDatabaseName("ix_assinaturas_premium_vigente");
            e.HasOne<Canal>().WithMany().HasForeignKey(x => x.CanalId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => Sistema || CanaisCanal.Contains(x.CanalId));
        });

        // Pagamentos (E3/E9)
        b.Entity<ContaGatewayCanal>(e =>
        {
            e.Property(x => x.Provedor).HasMaxLength(20);
            e.Property(x => x.ChaveCifrada).HasMaxLength(2000);
            e.Property(x => x.ChaveFinal).HasMaxLength(4);
            e.Property(x => x.WebhookId).HasMaxLength(80);
            e.Property(x => x.WebhookTokenHash).HasMaxLength(64);
            e.Property(x => x.NomeConta).HasMaxLength(200);
            e.Property(x => x.DocumentoConta).HasMaxLength(14);
            e.HasIndex(x => x.CanalId).IsUnique().HasFilter("ativa").HasDatabaseName("ix_contas_gateway_canal_ativa");
            e.HasIndex(x => x.WebhookTokenHash).IsUnique();
            e.HasOne<Canal>().WithMany().HasForeignKey(x => x.CanalId).OnDelete(DeleteBehavior.Restrict);
            // Só o próprio canal (e o sistema) enxerga a conta; ascendentes/descendentes não.
            e.HasQueryFilter(x => Sistema || x.CanalId == CanalAtual);
        });
        b.Entity<EventoGateway>(e =>
        {
            e.Property(x => x.Origem).HasMaxLength(40);
            e.Property(x => x.EventoId).HasMaxLength(100);
            e.Property(x => x.Tipo).HasMaxLength(60);
            e.Property(x => x.CobrancaId).HasMaxLength(60);
            e.Property(x => x.Resultado).HasMaxLength(300);
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.HasIndex(x => new { x.Origem, x.EventoId }).IsUnique();
            e.HasIndex(x => x.CobrancaId);
            e.HasQueryFilter(x => Sistema);
        });

        // Filtro de tenant para todas as entidades ITenantEntity que ainda não têm filtro próprio.
        foreach (var et in b.Model.GetEntityTypes())
        {
            if (!typeof(ITenantEntity).IsAssignableFrom(et.ClrType) || et.GetDeclaredQueryFilters().Count > 0) continue;
            var nome = EntidadesCarteiraCanal.Contains(et.ClrType) ? nameof(AplicarFiltroCarteira) : nameof(AplicarFiltroTenant);
            var metodo = typeof(PersonaliPontoDbContext).GetMethod(nome,System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .MakeGenericMethod(et.ClrType);
            metodo.Invoke(this, [b]);
        }

        AplicarSnakeCase(b);
    }

    /// <summary>
    /// Entidades de cadastro/financeiro do cliente que o canal dono (e os canais ascendentes) podem ler e
    /// alterar. Dados de ponto/RH ficam fora: o canal só os acessa em modo suporte auditado.
    /// Deve coincidir com <see cref="SegurancaBanco.TabelasCarteiraCanal"/> (RLS).
    /// </summary>
    public static readonly Type[] EntidadesCarteiraCanal = [typeof(Assinatura), typeof(Fatura)];

    private static void ConfigurarGateway<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> e) where T : class, ICobrancaGateway
    {
        e.Property(x => x.GatewayCobrancaId).HasMaxLength(60);
        e.Property(x => x.GatewayLink).HasMaxLength(500);
        e.Property(x => x.GatewayBoletoUrl).HasMaxLength(500);
        e.Property(x => x.GatewayLinhaDigitavel).HasMaxLength(100);
        e.Property(x => x.GatewayPixCopiaCola).HasMaxLength(1000);
        e.Property(x => x.GatewayErro).HasMaxLength(500);
        e.HasIndex(x => x.GatewayCobrancaId);
    }

    private void AplicarFiltroTenant<T>(ModelBuilder b) where T : class, ITenantEntity =>
        b.Entity<T>().HasQueryFilter(x => Sistema || x.TenantId == TenantAtual);

    private void AplicarFiltroCarteira<T>(ModelBuilder b) where T : class, ITenantEntity =>
        b.Entity<T>().HasQueryFilter(x => Sistema || x.TenantId == TenantAtual || TenantsCanal.Contains(x.TenantId));

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
        // Régua de canal (E2): com o painel bloqueado/suspenso, o usuário de canal não grava nada (inclusive em modo
        // suporte de cliente). Não afeta usuários do cliente nem a marcação do trabalhador (sem escopo de canal).
        if (!Sistema && TenantContext is Tenancy.RequestContext { PainelCanalBloqueado: true }
            && ChangeTracker.Entries().Any(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            throw new Core.RepP.Services.AcessoNegadoException("Painel do canal bloqueado por pendência financeira: regularize as faturas para voltar a cadastrar ou alterar.");

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is IImmutableEntity && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException($"{entry.Entity.GetType().Name} é imutável: registros originais não podem ser alterados nem excluídos.");

            // Cliente sem canal dono explícito: canal do usuário ou, para a plataforma, o Owner raiz.
            if (entry.Entity is Tenant tn && entry.State == EntityState.Added && tn.CanalDonoId == Guid.Empty)
                tn.CanalDonoId = TenantContext.CanalId ?? Canal.OwnerRaizId;

            if (entry.Entity is Usuario u && entry.State is EntityState.Added or EntityState.Modified)
                u.Escopo = u.CanalId is not null ? EscopoUsuario.Canal : u.TenantId is null ? EscopoUsuario.Plataforma : EscopoUsuario.Tenant;

            if (entry.Entity is ITenantEntity te)
            {
                if (entry.State == EntityState.Added && te.TenantId == Guid.Empty && TenantAtual is { } t) te.TenantId = t;
                if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                {
                    if (te.TenantId == Guid.Empty) throw new InvalidOperationException($"{entry.Entity.GetType().Name} sem TenantId.");
                    var carteira = EntidadesCarteiraCanal.Contains(entry.Entity.GetType()) && TenantContext.TenantsCanal.Contains(te.TenantId);
                    if (!Sistema && te.TenantId != TenantAtual && !carteira)
                        throw new UnauthorizedAccessException("Tentativa de gravar dados de outro tenant.");
                    if (entry.State == EntityState.Modified && entry.Property(nameof(ITenantEntity.TenantId)).IsModified)
                        throw new UnauthorizedAccessException("TenantId não pode ser alterado.");
                }
            }
        }
    }
}
