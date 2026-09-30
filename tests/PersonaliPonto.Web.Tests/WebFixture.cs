using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Infrastructure.Persistence;
using PersonaliPonto.Infrastructure.Tenancy;
using PersonaliPonto.Modules.SaaS.Services;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Web.Tests;

/// <summary>Sobe o painel Web completo contra um banco descartável e prepara usuários de teste.</summary>
public sealed partial class WebFixture : IAsyncLifetime
{
    private readonly string _servidor = Environment.GetEnvironmentVariable("PERSONALIPONTO_TEST_DB") ?? "Host=localhost;Port=54329;Username=postgres;Password=devpass";
    private readonly string _banco = "tc_web_" + Guid.NewGuid().ToString("N")[..12];
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public const string SenhaTeste = "SenhaDeTeste2026";
    public const string TokenWebhookOwner = "token-webhook-owner-web-0123456789abcdefghij";
    public const string EmailAdmin = "plataforma@teste.local";
    public const string EmailRh = "rh@empresa-teste.local";
    public string CpfFuncionario { get; private set; } = "";
    public Guid TenantId { get; private set; }
    public Guid EstabelecimentoId { get; private set; }
    public Guid FuncionarioId { get; private set; }

    // Canal (E1): Revendedor "Tempo Certo Web" → Parceiro "Info Web"; cada um com um cliente.
    public const string EmailRevendedor = "admin@revenda-teste.local";
    public const string EmailParceiro = "admin@parceiro-teste.local";
    public const string MarcaRevendedor = "Tempo Certo Web";
    public const string MarcaParceiro = "Info Web";
    public const string ClienteRevendedor = "CLIENTE DIRETO REVENDA LTDA";
    public const string ClienteParceiro = "CLIENTE DO PARCEIRO LTDA";
    public Guid RevendedorId { get; private set; }
    public Guid ParceiroId { get; private set; }
    public Guid TenantRevendedor { get; private set; }
    public Guid TenantParceiro { get; private set; }

    public async Task InitializeAsync()
    {
        await using (var c = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(_servidor) { Database = "postgres" }.ConnectionString))
        {
            await c.OpenAsync();
            await new NpgsqlCommand($"CREATE DATABASE {_banco}", c).ExecuteNonQueryAsync();
        }
        var cs = new NpgsqlConnectionStringBuilder(_servidor) { Database = _banco }.ConnectionString;
        Environment.SetEnvironmentVariable("PERSONALIPONTO_DB", null);
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:PersonaliPonto", cs);
            b.UseSetting("Banco:MigrarNaInicializacao", "true");
            b.UseSetting("Rotinas:Desabilitar", "true");
            b.UseSetting("SuperAdmin:Email", EmailAdmin);
            b.UseSetting("SuperAdmin:Senha", SenhaTeste);
            b.UseSetting("Storage:DiretorioLocal", Path.Combine(Path.GetTempPath(), _banco));
            b.UseSetting("RepP:NumeroRegistroInpi", "BR512026000123-4");
            b.UseSetting("RepP:IdentificadorDesenvolvedor", "12ABC34501DE35");
            b.UseSetting("Asaas:Owner:WebhookToken", TokenWebhookOwner);
            b.UseSetting("Asaas:WebhookLimitePorMinuto", "30");
        });
        _ = Factory.Server; // inicializa (migrations + super admin)

        using var scope = Factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var ctx = sp.GetRequiredService<RequestContext>();
        ctx.DefinirSistema();
        var db = sp.GetRequiredService<PersonaliPontoDbContext>();
        var admin = await db.Usuarios.FirstAsync(u => u.Email == EmailAdmin);
        admin.DeveTrocarSenha = false;
        await db.SaveChangesAsync();

        var cliente = await sp.GetRequiredService<ClienteService>().CadastrarAsync(new NovoClienteRequest(
            "EMPRESA TESTE WEB LTDA", "11.222.333/0001-81", null, EmailRh, "RH Teste", "52998224725", 100, 10, 20, null, 10, null, "Rua Teste, 1", false), default);
        TenantId = cliente.TenantId;
        var rh = await db.Usuarios.FirstAsync(u => u.Email == EmailRh);
        rh.SenhaHash = SecretHasher.Hash(SenhaTeste);
        rh.DeveTrocarSenha = false;
        await db.SaveChangesAsync();

        ctx.DefinirTenant(TenantId);
        ctx.DefinirUsuario(rh.Id, rh.Nome, "52998224725", Roles.RH, null);
        var cad = sp.GetRequiredService<CadastroService>();
        EstabelecimentoId = (await db.Estabelecimentos.FirstAsync()).Id;
        var j = await cad.SalvarJornadaAsync(CadastroService.Modelo(TipoJornada.Semanal5x2, "COM", "Comercial"), default);
        CpfFuncionario = "11144477735";
        var f = await cad.SalvarFuncionarioAsync(new Funcionario
        {
            EstabelecimentoId = EstabelecimentoId, Nome = "Funcionária Teste", Cpf = CpfFuncionario, Matricula = "501",
            DataAdmissao = new DateOnly(2026, 1, 1), JornadaId = j.Id, Ativo = true, PermiteMarcacaoApp = true
        }, default);
        FuncionarioId = f.Id;
        await cad.DefinirPinAsync(f.Id, "4821", default);
        var acesso = await sp.GetRequiredService<Infrastructure.Seguranca.UsuarioService>().CriarAcessoFuncionarioAsync(f.Id, default);
        ctx.DefinirSistema();
        var uf = await db.Usuarios.FirstAsync(u => u.Id == acesso.UsuarioId);
        uf.SenhaHash = SecretHasher.Hash(SenhaTeste);
        uf.DeveTrocarSenha = false;
        await db.SaveChangesAsync();

        await CriarCanaisAsync();
    }

    private async Task CriarCanaisAsync()
    {
        static NovoCanalRequest Canal(string marca, string cnpj, string email) =>
            new(marca + " LTDA", cnpj, marca, null, null, "Admin " + marca, email, null);
        static NovoClienteRequest Cliente(string nome, string cnpj) =>
            new(nome, cnpj, null, $"rh.{Guid.NewGuid():N}@cliente.local", "RH", null, 150, 5, 0, null, 10, null, "Rua B, 2", false);

        using (var scope = Factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<RequestContext>().DefinirSistema();
            RevendedorId = (await sp.GetRequiredService<CanalService>().CriarAsync(Canal(MarcaRevendedor, "11444777000161", EmailRevendedor), default)).CanalId;
        }
        using (var scope = Factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            await EntrarNoCanalAsync(sp, RevendedorId, Roles.AdminRevendedor);
            ParceiroId = (await sp.GetRequiredService<CanalService>().CriarAsync(Canal(MarcaParceiro, "45723174000110", EmailParceiro), default)).CanalId;
            TenantRevendedor = (await sp.GetRequiredService<ClienteService>().CadastrarAsync(Cliente(ClienteRevendedor, "04252011000110"), default)).TenantId;
        }
        using (var scope = Factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            await EntrarNoCanalAsync(sp, ParceiroId, Roles.AdminParceiro);
            TenantParceiro = (await sp.GetRequiredService<ClienteService>().CadastrarAsync(Cliente(ClienteParceiro, "33000167000101"), default)).TenantId;
        }
        using (var scope = Factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<RequestContext>().DefinirSistema();
            var db = sp.GetRequiredService<PersonaliPontoDbContext>();
            foreach (var u in await db.Usuarios.Where(u => u.Email == EmailRevendedor || u.Email == EmailParceiro).ToListAsync())
            {
                u.SenhaHash = SecretHasher.Hash(SenhaTeste);
                u.DeveTrocarSenha = false;
            }
            await db.SaveChangesAsync();
        }
    }

    private static async Task EntrarNoCanalAsync(IServiceProvider sp, Guid canalId, string papel)
    {
        var ctx = sp.GetRequiredService<RequestContext>();
        ctx.DefinirUsuario(Guid.NewGuid(), "Admin canal", null, papel, null);
        var carteira = await sp.GetRequiredService<ContextoCanalService>().CarteiraAsync(canalId, default);
        ctx.DefinirCanal(canalId, carteira!.Canais, carteira.Tenants);
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var c = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(_servidor) { Database = "postgres" }.ConnectionString);
        await c.OpenAsync();
        await new NpgsqlCommand($"DROP DATABASE IF EXISTS {_banco} WITH (FORCE)", c).ExecuteNonQueryAsync();
    }

    public HttpClient Cliente() => Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenRegex();

    public static async Task<string> TokenAsync(HttpClient c, string pagina)
    {
        var html = await c.GetStringAsync(pagina);
        var m = TokenRegex().Match(html);
        if (!m.Success) throw new InvalidOperationException($"Antiforgery não encontrado em {pagina}.");
        return WebUtility.HtmlDecode(m.Groups[1].Value);
    }

    /// <summary>Faz login pelo formulário real (antiforgery + cookie) e retorna o redirecionamento.</summary>
    public static async Task<string?> EntrarAsync(HttpClient c, string login, string senha)
    {
        var token = await TokenAsync(c, "/entrar");
        var r = await c.PostAsync("/conta/entrar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["email"] = login, ["senha"] = senha
        }));
        return r.Headers.Location?.ToString();
    }
}

[CollectionDefinition("web")]
public sealed class WebCollection : ICollectionFixture<WebFixture>;
