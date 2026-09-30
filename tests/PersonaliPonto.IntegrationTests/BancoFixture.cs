using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Infrastructure;
using PersonaliPonto.Infrastructure.Persistence;
using PersonaliPonto.Infrastructure.Tenancy;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.IntegrationTests;

/// <summary>
/// Cria um banco PostgreSQL descartável por execução, aplica as migrations (incluindo RLS/imutabilidade)
/// e expõe um ServiceProvider igual ao da aplicação. Servidor: PERSONALIPONTO_TEST_DB ou Postgres local de dev.
/// </summary>
public sealed class BancoFixture : IAsyncLifetime
{
    public const string ServidorPadrao = "Host=localhost;Port=54329;Username=postgres;Password=devpass";
    private readonly string _servidor = Environment.GetEnvironmentVariable("PERSONALIPONTO_TEST_DB") ?? ServidorPadrao;
    private readonly string _banco = "pp_test_" + Guid.NewGuid().ToString("N")[..12];
    public string ConnectionString => new NpgsqlConnectionStringBuilder(_servidor) { Database = _banco }.ConnectionString;
    public ServiceProvider Services { get; private set; } = null!;
    public const string ChaveOwner = "$aact_hmlg_000000000000000000owner0001";
    public const string TokenOwner = "token-webhook-owner-0123456789abcdefghijklmnop";
    public FakeGateway Gateway { get; } = new();

    public async Task InitializeAsync()
    {
        await using (var c = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(_servidor) { Database = "postgres" }.ConnectionString))
        {
            await c.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE {_banco}", c);
            await cmd.ExecuteNonQueryAsync();
        }

        Environment.SetEnvironmentVariable("PERSONALIPONTO_DB", null);
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:PersonaliPonto"] = ConnectionString,
            ["Ntp:Habilitado"] = "false",
            ["RepP:NumeroRegistroInpi"] = "BR512026000123-4",
            ["RepP:IdentificadorDesenvolvedor"] = "12ABC34501DE35",
            ["RepP:RazaoSocialDesenvolvedor"] = "PERSONALIPONTO TECNOLOGIA",
            ["RepP:EmailDesenvolvedor"] = "contato@personaliponto.com.br",
            ["Storage:DiretorioLocal"] = Path.Combine(Path.GetTempPath(), _banco),
            ["Jwt:Chave"] = new string('k', 48),
            ["Asaas:Ambiente"] = "Sandbox",
            ["Asaas:UrlPublica"] = "https://painel.teste.local",
            ["Asaas:Owner:ChaveApi"] = ChaveOwner,
            ["Asaas:Owner:WebhookToken"] = TokenOwner
        }).Build();

        var sc = new ServiceCollection();
        sc.AddLogging(l => l.SetMinimumLevel(LogLevel.Warning));
        sc.AddSingleton<IConfiguration>(cfg);
        sc.AddPersonaliPonto(cfg, rotinasEmSegundoPlano: false);
        sc.AddSingleton(Gateway);
        sc.AddSingleton<Infrastructure.Pagamentos.IGatewayPagamento>(Gateway);
        Services = sc.BuildServiceProvider();

        await Infrastructure.Hosting.Inicializacao.MigrarAsync(Services); // inclui o schema de sistema (Data Protection)
    }

    public async Task DisposeAsync()
    {
        await Services.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var c = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(_servidor) { Database = "postgres" }.ConnectionString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_banco} WITH (FORCE)", c);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Escopo com contexto de tenant (como uma requisição autenticada).</summary>
    public AsyncServiceScope Escopo(Guid? tenantId = null, bool sistema = false, string papel = Roles.RH)
    {
        var s = Services.CreateAsyncScope();
        var ctx = s.ServiceProvider.GetRequiredService<RequestContext>();
        if (sistema) ctx.DefinirSistema();
        else if (tenantId is { } t) ctx.DefinirTenant(t);
        ctx.DefinirUsuario(Guid.NewGuid(), "Teste RH", "52998224725", papel, null);
        return s;
    }

    /// <summary>Escopo de um usuário de canal (revendedor/parceiro), com a carteira resolvida como na aplicação.</summary>
    public async Task<AsyncServiceScope> EscopoCanalAsync(Guid canalId, string papel)
    {
        var s = Services.CreateAsyncScope();
        var ctx = s.ServiceProvider.GetRequiredService<RequestContext>();
        ctx.DefinirUsuario(Guid.NewGuid(), "Usuário do canal", null, papel, null);
        var carteira = await s.ServiceProvider.GetRequiredService<ContextoCanalService>().CarteiraAsync(canalId, default)
            ?? throw new InvalidOperationException("Canal inexistente.");
        ctx.DefinirCanal(canalId, carteira.Canais, carteira.Tenants);
        ctx.DefinirSituacaoCanal(carteira.Canal.Status);
        return s;
    }

    public sealed record Cenario(Guid TenantId, Guid EmpregadorId, Guid EstabelecimentoId, Guid FuncionarioId, Guid JornadaId, string Pin);

    /// <summary>Cria um tenant completo: empresa, estabelecimento, jornada 5x2 e funcionário com PIN.</summary>
    public async Task<Cenario> CriarCenarioAsync(string nome, string cnpj, string cpf)
    {
        Guid tenantId;
        await using (var s = Escopo(sistema: true))
        {
            var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
            var t = new Tenant { Nome = nome, Slug = nome.ToLowerInvariant().Replace(' ', '-') + "-" + Guid.NewGuid().ToString("N")[..6], Status = TenantStatus.Ativo, CriadoEm = DateTimeOffset.UtcNow };
            db.Tenants.Add(t);
            await db.SaveChangesAsync();
            tenantId = t.Id;
        }

        await using var sc = Escopo(tenantId);
        var cad = sc.ServiceProvider.GetRequiredService<CadastroService>();
        var emp = await cad.SalvarEmpregadorAsync(new Empregador { RazaoSocial = nome + " LTDA", TipoIdentificador = TipoIdentificador.Cnpj, Identificador = cnpj }, default);
        var est = await cad.SalvarEstabelecimentoAsync(new Estabelecimento
        {
            EmpregadorId = emp.Id, Nome = "Matriz", TipoIdentificador = TipoIdentificador.Cnpj, Identificador = cnpj,
            LocalPrestacao = "Rua das Flores, 100 - Campina Grande/PB"
        }, default);
        var j = await cad.SalvarJornadaAsync(CadastroService.Modelo(TipoJornada.Semanal5x2, "COM", "Comercial 44h"), default);
        var f = await cad.SalvarFuncionarioAsync(new Funcionario
        {
            EstabelecimentoId = est.Id, Nome = "Funcionário " + nome, Cpf = cpf, Matricula = "001",
            DataAdmissao = new DateOnly(2026, 1, 1), Cargo = "Auxiliar", JornadaId = j.Id
        }, default);
        await cad.DefinirPinAsync(f.Id, "4821", default);
        return new Cenario(tenantId, emp.Id, est.Id, f.Id, j.Id, "4821");
    }
}

public static class Docs
{
    private static int _seq = Random.Shared.Next(1000, 9000);

    /// <summary>CNPJ válido e único por chamada.</summary>
    public static string Cnpj()
    {
        var baseNum = (Interlocked.Increment(ref _seq) * 7919L % 100000000).ToString("00000000") + "0001";
        return baseNum + Core.RepP.Formatacao.Documentos.DigitosCnpj(baseNum);
    }

    /// <summary>CPF válido e único por chamada.</summary>
    public static string Cpf()
    {
        var nove = (Interlocked.Increment(ref _seq) * 104729L % 1000000000).ToString("000000000");
        int Dv(string s, int peso) { var soma = 0; foreach (var c in s) soma += (c - '0') * peso--; var r = soma % 11; return r < 2 ? 0 : 11 - r; }
        var d1 = Dv(nove, 10);
        return nove + d1 + Dv(nove + d1, 11);
    }
}

[CollectionDefinition("banco")]
public sealed class BancoCollection : ICollectionFixture<BancoFixture>;
