using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Infrastructure.Persistence;
using PersonaliPonto.Infrastructure.Seguranca;
using PersonaliPonto.Infrastructure.Tenancy;
using PersonaliPonto.Modules.SaaS.Domain;
using PersonaliPonto.Modules.SaaS.Services;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.IntegrationTests;

/// <summary>
/// Etapa E1 — isolamento entre canais no PostgreSQL com o papel personaliponto_app (sem BYPASSRLS).
/// Rede: Owner → Revendedor A (cliente direto CA) → Parceiros A1 (cliente C1) e A2 (cliente C2); Revendedor B (cliente CB).
/// Cada verificação ataca o EF (filtros) e o SQL cru com IgnoreQueryFilters (RLS).
/// </summary>
[Collection("banco")]
public sealed class IsolamentoCanalTests(BancoFixture fx) : IAsyncLifetime
{
    public sealed record Rede(Guid RevA, Guid RevB, Guid ParA1, Guid ParA2, Guid ClienteA, Guid ClienteA1, Guid ClienteA2, Guid ClienteB);

    private static readonly SemaphoreSlim Trava = new(1, 1);
    private static Rede? _rede;
    private Rede R => _rede!;

    public async Task InitializeAsync()
    {
        await Trava.WaitAsync();
        try { _rede ??= await CriarRedeAsync(); }
        finally { Trava.Release(); }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static NovoCanalRequest NovoCanal(string marca) =>
        new(marca + " LTDA", Docs.Cnpj(), marca, null, null, "Admin " + marca, $"admin.{Guid.NewGuid():N}@canal.local", null);

    private static NovoClienteRequest NovoCliente(string nome) =>
        new(nome, Docs.Cnpj(), null, $"rh.{Guid.NewGuid():N}@cliente.local", "RH " + nome, null, 100m, 10, 0m, null, 10, null, "Rua A, 1", false);

    private async Task<Rede> CriarRedeAsync()
    {
        Guid revA, revB;
        await using (var s = fx.Escopo(sistema: true))
        {
            var canais = s.ServiceProvider.GetRequiredService<CanalService>();
            revA = (await canais.CriarAsync(NovoCanal("Tempo Certo A"), default)).CanalId;
            revB = (await canais.CriarAsync(NovoCanal("Revenda B"), default)).CanalId;
        }

        Guid parA1, parA2, clienteA;
        await using (var s = await fx.EscopoCanalAsync(revA, Roles.AdminRevendedor))
        {
            var canais = s.ServiceProvider.GetRequiredService<CanalService>();
            parA1 = (await canais.CriarAsync(NovoCanal("Parceiro A1"), default)).CanalId;
            parA2 = (await canais.CriarAsync(NovoCanal("Parceiro A2"), default)).CanalId;
            clienteA = (await s.ServiceProvider.GetRequiredService<ClienteService>().CadastrarAsync(NovoCliente("CLIENTE DIRETO A"), default)).TenantId;
        }

        async Task<Guid> ClienteDe(Guid canal, string papel, string nome)
        {
            await using var s = await fx.EscopoCanalAsync(canal, papel);
            return (await s.ServiceProvider.GetRequiredService<ClienteService>().CadastrarAsync(NovoCliente(nome), default)).TenantId;
        }

        return new Rede(revA, revB, parA1, parA2, clienteA,
            await ClienteDe(parA1, Roles.AdminParceiro, "CLIENTE PARCEIRO A1"),
            await ClienteDe(parA2, Roles.AdminParceiro, "CLIENTE PARCEIRO A2"),
            await ClienteDe(revB, Roles.AdminRevendedor, "CLIENTE REVENDA B"));
    }

    private static async Task<(List<Guid> Ef, List<Guid> Sql)> TenantsVisiveisAsync(AsyncServiceScope s)
    {
        var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
        var ef = await db.Tenants.Select(t => t.Id).ToListAsync();
        var sql = await db.Database.SqlQueryRaw<Guid>("SELECT id AS \"Value\" FROM personaliponto.tenants WHERE 1=1").ToListAsync();
        return (ef, sql);
    }

    [Fact]
    public async Task Revendedor_A_nao_ve_tenants_do_revendedor_B()
    {
        await using var s = await fx.EscopoCanalAsync(R.RevA, Roles.AdminRevendedor);
        var (ef, sql) = await TenantsVisiveisAsync(s);
        foreach (var lista in new[] { ef, sql })
        {
            Assert.DoesNotContain(R.ClienteB, lista);
            Assert.Contains(R.ClienteA, lista);
        }
        var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
        Assert.False(await db.Assinaturas.IgnoreQueryFilters().AnyAsync(a => a.TenantId == R.ClienteB));
        Assert.Null(await db.Canais.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == R.RevB));
        Assert.Empty(await db.Usuarios.IgnoreQueryFilters().Where(u => u.CanalId == R.RevB).ToListAsync());
    }

    [Fact]
    public async Task Revendedor_ve_tenants_dos_seus_parceiros_e_os_parceiros()
    {
        await using var s = await fx.EscopoCanalAsync(R.RevA, Roles.SuporteRevendedor);
        var (ef, sql) = await TenantsVisiveisAsync(s);
        foreach (var lista in new[] { ef, sql })
            Assert.Equal(new[] { R.ClienteA, R.ClienteA1, R.ClienteA2 }.Order(), lista.Order());
        var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
        Assert.Equal(3, await db.Assinaturas.CountAsync());
        var canais = await db.Canais.Select(c => c.Id).ToListAsync();
        Assert.Equal(new[] { R.RevA, R.ParA1, R.ParA2 }.Order(), canais.Order());
    }

    [Fact]
    public async Task Parceiro_nao_ve_tenant_do_irmao_nem_clientes_diretos_do_revendedor()
    {
        await using var s = await fx.EscopoCanalAsync(R.ParA1, Roles.AdminParceiro);
        var (ef, sql) = await TenantsVisiveisAsync(s);
        foreach (var lista in new[] { ef, sql })
            Assert.Equal([R.ClienteA1], lista);
        var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
        var faturasOuAssinaturas = await db.Database.SqlQueryRaw<Guid>("SELECT tenant_id AS \"Value\" FROM personaliponto.assinaturas").ToListAsync();
        Assert.Equal([R.ClienteA1], faturasOuAssinaturas);
        // Não enxerga o canal pai nem o irmão.
        Assert.Equal([R.ParA1], await db.Canais.IgnoreQueryFilters().Select(c => c.Id).ToListAsync());
    }

    [Fact]
    public async Task Parceiro_ve_cadastro_e_financeiro_mas_nao_le_ponto_sem_modo_suporte()
    {
        await using (var s = await fx.EscopoCanalAsync(R.ParA1, Roles.AdminParceiro))
        {
            var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
            Assert.NotNull(await db.Assinaturas.FirstOrDefaultAsync(a => a.TenantId == R.ClienteA1));
            Assert.Empty(await db.Estabelecimentos.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await db.Empregadores.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await db.RegistrosRep.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await db.Database.SqlQueryRaw<Guid>("SELECT id AS \"Value\" FROM personaliponto.registros_rep").ToListAsync());
            Assert.Empty(await db.Usuarios.IgnoreQueryFilters().Where(u => u.TenantId == R.ClienteA1).ToListAsync());
            // Escrita em tabela de ponto do cliente é rejeitada pelo RLS.
            var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                "INSERT INTO personaliponto.feriados (id, tenant_id, data, descricao, abrangencia) VALUES (gen_random_uuid(), {0}, DATE '2026-12-08', 'x', 4)",
                R.ClienteA1));
            Assert.Equal("42501", ex.SqlState);
        }

        // Com modo suporte auditado, o parceiro enxerga os dados de ponto do próprio cliente.
        await using (var s = await fx.EscopoCanalAsync(R.ParA1, Roles.AdminParceiro))
        {
            var sp = s.ServiceProvider;
            var claims = await sp.GetRequiredService<SuporteService>().IniciarClienteAsync(Usuario(Roles.AdminParceiro, R.ParA1), R.ClienteA1,
                "Chamado 42 - conferência de marcações", default);
            Assert.Contains(claims, c => c.Type == PersonaliPontoClaims.TenantId && c.Value == R.ClienteA1.ToString());
            sp.GetRequiredService<RequestContext>().DefinirTenant(R.ClienteA1);
            var db = sp.GetRequiredService<PersonaliPontoDbContext>();
            Assert.NotEmpty(await db.Estabelecimentos.ToListAsync());
            var acesso = await db.AcessosSuporte.SingleAsync(a => a.TenantId == R.ClienteA1 && a.CanalId == R.ParA1);
            Assert.Equal(NivelSuporte.Parceiro, acesso.NivelOrigem);
        }
    }

    [Fact]
    public async Task Modo_suporte_exige_cliente_na_carteira()
    {
        await using var s = await fx.EscopoCanalAsync(R.ParA1, Roles.AdminParceiro);
        var suporte = s.ServiceProvider.GetRequiredService<SuporteService>();
        await Assert.ThrowsAsync<NaoEncontradoException>(() => suporte.IniciarClienteAsync(Usuario(Roles.AdminParceiro, R.ParA1), R.ClienteA2,
            "Tentativa em cliente do irmão", default));
        await Assert.ThrowsAsync<NaoEncontradoException>(() => suporte.IniciarClienteAsync(Usuario(Roles.AdminParceiro, R.ParA1), R.ClienteA,
            "Tentativa em cliente do revendedor", default));
        await Assert.ThrowsAsync<RegraNegocioException>(() => suporte.IniciarClienteAsync(Usuario(Roles.AdminParceiro, R.ParA1), R.ClienteA1,
            "curto", default));
        // Canal nunca vira sistema pelo contexto: ComoTenant fora da carteira é recusado.
        Assert.Throws<UnauthorizedAccessException>(() => s.ServiceProvider.GetRequiredService<RequestContext>().ComoTenant(R.ClienteB));
    }

    [Fact]
    public async Task Owner_ve_tudo()
    {
        await using var s = fx.Escopo(sistema: true);
        var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
        var ids = await db.Tenants.Select(t => t.Id).ToListAsync();
        Assert.Superset(new HashSet<Guid> { R.ClienteA, R.ClienteA1, R.ClienteA2, R.ClienteB }, ids.ToHashSet());
        var canais = await db.Canais.Select(c => c.Id).ToListAsync();
        Assert.Superset(new HashSet<Guid> { Canal.OwnerRaizId, R.RevA, R.RevB, R.ParA1, R.ParA2 }, canais.ToHashSet());
        Assert.NotEmpty(await db.Estabelecimentos.Where(e => e.TenantId == R.ClienteB).ToListAsync());
    }

    [Fact]
    public async Task Tentativa_de_criar_quarto_nivel_falha_na_aplicacao_e_no_banco()
    {
        await using (var s = await fx.EscopoCanalAsync(R.ParA1, Roles.AdminParceiro))
        {
            var canais = s.ServiceProvider.GetRequiredService<CanalService>();
            await Assert.ThrowsAsync<RegraNegocioException>(() => canais.CriarAsync(NovoCanal("Sub parceiro"), default));
        }

        // Mesmo como Owner e contornando o serviço, o banco recusa o 4º nível.
        await using (var s = fx.Escopo(sistema: true))
        {
            var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
            var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                "INSERT INTO personaliponto.canais (id, tipo, canal_pai_id, caminho, nivel, slug, razao_social, cnpj, nome_marca, status, criado_em) " +
                "VALUES (gen_random_uuid(), 3, {0}, '', 3, {1}, 'X', '', 'X', 0, now())", R.ParA1, "quarto-" + Guid.NewGuid().ToString("N")[..8]));
            Assert.Equal("23514", ex.SqlState);
        }

        // Revendedor não cria canal sob canal de outra hierarquia (pai invisível pelo RLS).
        await using (var s = await fx.EscopoCanalAsync(R.RevA, Roles.AdminRevendedor))
        {
            var canais = s.ServiceProvider.GetRequiredService<CanalService>();
            await Assert.ThrowsAsync<NaoEncontradoException>(() => canais.CriarAsync(NovoCanal("Intruso") with { CanalPaiId = R.RevB }, default));
        }
    }

    [Fact]
    public async Task Canal_nao_altera_a_propria_situacao_nem_transfere_cliente_para_fora()
    {
        await using var s = await fx.EscopoCanalAsync(R.ParA1, Roles.AdminParceiro);
        var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE personaliponto.canais SET status = 3 WHERE id = {0}", R.ParA1));
        Assert.Equal("42501", ex.SqlState);
        var ex2 = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE personaliponto.tenants SET canal_dono_id = {0} WHERE id = {1}", R.RevB, R.ClienteA1));
        Assert.Equal("42501", ex2.SqlState);
        // Inserir cliente em canal de outra hierarquia é barrado pelo WITH CHECK.
        var ex3 = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO personaliponto.tenants (id, nome, slug, status, criado_em, canal_dono_id, tipo_entidade) VALUES (gen_random_uuid(), 'x', {0}, 1, now(), {1}, 0)",
            "x-" + Guid.NewGuid().ToString("N")[..8], R.RevB));
        Assert.Equal("42501", ex3.SqlState);
    }

    [Fact]
    public async Task Revendedor_altera_situacao_do_parceiro_e_fornecedor_padrao_e_a_marca_do_canal()
    {
        await using (var s = await fx.EscopoCanalAsync(R.RevA, Roles.AdminRevendedor))
        {
            var canais = s.ServiceProvider.GetRequiredService<CanalService>();
            await canais.AlterarStatusAsync(R.ParA2, StatusCanal.Aviso, default);
            await Assert.ThrowsAsync<AcessoNegadoException>(() => canais.AlterarStatusAsync(R.RevA, StatusCanal.Ativo, default));
            await canais.AlterarStatusAsync(R.ParA2, StatusCanal.Ativo, default);
            var filhos = await canais.ListarFilhosAsync(null, default);
            Assert.Equal(2, filhos.Count);
            Assert.All(filhos, f => Assert.Equal(1, f.Clientes));
        }

        await using (var s = fx.Escopo(sistema: true))
        {
            var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
            Assert.Equal("PARCEIRO A1", (await db.Assinaturas.SingleAsync(a => a.TenantId == R.ClienteA1)).Fornecedor);
            Assert.Equal("TEMPO CERTO A", (await db.Assinaturas.SingleAsync(a => a.TenantId == R.ClienteA)).Fornecedor);
            Assert.Equal(R.ParA1, (await db.Tenants.SingleAsync(t => t.Id == R.ClienteA1)).CanalDonoId);
            var parceiro = await db.Canais.SingleAsync(c => c.Id == R.ParA1);
            Assert.Equal(3, parceiro.Nivel);
            Assert.Equal($"/{Canal.OwnerRaizId}/{R.RevA}/{R.ParA1}/", parceiro.Caminho);
        }
    }

    [Fact]
    public async Task Municipios_importados_sao_globais_e_so_a_plataforma_escreve()
    {
        await using (var s = fx.Escopo(sistema: true))
        {
            var n = await s.ServiceProvider.GetRequiredService<MunicipioService>().ImportarCsvAsync(
                new StringReader("codigo;nome;uf\n2504009;Campina Grande;PB\n2507507;João Pessoa;pb\nlixo\n"), default);
            Assert.Equal(2, n);
        }
        await using (var s = await fx.EscopoCanalAsync(R.ParA1, Roles.AdminParceiro))
        {
            var svc = s.ServiceProvider.GetRequiredService<MunicipioService>();
            Assert.Single(await svc.BuscarAsync("PB", "campina", default));
            await Assert.ThrowsAsync<AcessoNegadoException>(() => svc.ImportarCsvAsync(new StringReader("2504009;X;PB"), default));
            var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
            var afetadas = await db.Database.ExecuteSqlRawAsync("UPDATE personaliponto.municipios SET nome = 'x'");
            Assert.Equal(0, afetadas);
        }
    }

    private static ClaimsPrincipal Usuario(string papel, Guid canalId) => new(new ClaimsIdentity(
    [
        new Claim(PersonaliPontoClaims.UserId, Guid.NewGuid().ToString()),
        new Claim(ClaimTypes.Name, "Usuário do canal"),
        new Claim(ClaimTypes.Role, papel),
        new Claim(PersonaliPontoClaims.CanalId, canalId.ToString())
    ], "teste", ClaimTypes.Name, ClaimTypes.Role));
}
