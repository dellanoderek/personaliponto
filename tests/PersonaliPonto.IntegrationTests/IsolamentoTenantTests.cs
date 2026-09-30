using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Infrastructure.Persistence;

namespace PersonaliPonto.IntegrationTests;

/// <summary>
/// Critério de pronto da Fase 1: "query maliciosa/erro de código não retorna dado de outro tenant".
/// Cada teste ataca uma camada diferente (filtro EF, SQL cru, IgnoreQueryFilters, escrita cruzada).
/// </summary>
[Collection("banco")]
public sealed class IsolamentoTenantTests(BancoFixture fx) : IAsyncLifetime
{
    private BancoFixture.Cenario _a = null!;
    private BancoFixture.Cenario _b = null!;

    public async Task InitializeAsync()
    {
        _a = await fx.CriarCenarioAsync("Alfa", "11222333000181", "52998224725");
        _b = await fx.CriarCenarioAsync("Beta", "11444777000161", "11144477735");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Consulta_linq_so_retorna_o_proprio_tenant()
    {
        await using var s = fx.Escopo(_a.TenantId);
        var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
        var funcs = await db.Funcionarios.ToListAsync();
        Assert.All(funcs, f => Assert.Equal(_a.TenantId, f.TenantId));
        Assert.DoesNotContain(funcs, f => f.Id == _b.FuncionarioId);
        Assert.Null(await db.Funcionarios.FirstOrDefaultAsync(f => f.Id == _b.FuncionarioId));
    }

    [Fact]
    public async Task IgnoreQueryFilters_continua_bloqueado_pelo_RLS()
    {
        await using var s = fx.Escopo(_a.TenantId);
        var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
        var todos = await db.Funcionarios.IgnoreQueryFilters().ToListAsync();
        Assert.NotEmpty(todos);
        Assert.All(todos, f => Assert.Equal(_a.TenantId, f.TenantId));
        var registros = await db.RegistrosRep.IgnoreQueryFilters().ToListAsync();
        Assert.All(registros, r => Assert.Equal(_a.TenantId, r.TenantId));
    }

    [Fact]
    public async Task Sql_cru_malicioso_nao_vaza_outro_tenant()
    {
        await using var s = fx.Escopo(_a.TenantId);
        var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
        // Simula injeção/erro de código: consulta sem WHERE e com OR 1=1.
        var cpfs = await db.Database.SqlQueryRaw<string>("SELECT cpf AS \"Value\" FROM personaliponto.funcionarios WHERE 1=1 OR tenant_id IS NOT NULL").ToListAsync();
        Assert.Contains("52998224725", cpfs);
        Assert.DoesNotContain("11144477735", cpfs);

        var tenants = await db.Database.SqlQueryRaw<Guid>("SELECT id AS \"Value\" FROM personaliponto.tenants").ToListAsync();
        Assert.Equal([_a.TenantId], tenants);
    }

    [Fact]
    public async Task Tentativa_de_desligar_o_bypass_pela_sessao_nao_funciona_para_papel_da_aplicacao()
    {
        await using var s = fx.Escopo(_a.TenantId);
        var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
        await db.Database.OpenConnectionAsync();
        // Mesmo trocando o tenant_id na sessão, o próximo comando EF reaplica o contexto correto.
        await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.tenant_id', {0}, false)", _b.TenantId.ToString());
        var funcs = await db.Funcionarios.IgnoreQueryFilters().Select(f => f.TenantId).Distinct().ToListAsync();
        Assert.Equal([_a.TenantId], funcs);
    }

    [Fact]
    public async Task Sem_contexto_de_tenant_nada_e_retornado()
    {
        await using var s = fx.Services.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
        Assert.Empty(await db.Funcionarios.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.Tenants.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.RegistrosRep.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Escrita_em_outro_tenant_e_barrada_pelo_EF_e_pelo_RLS()
    {
        await using var s = fx.Escopo(_a.TenantId);
        var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
        db.Feriados.Add(new Modules.RH.Domain.Feriado { TenantId = _b.TenantId, Data = new DateOnly(2026, 12, 8), Descricao = "Invasão" });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        // Contornando o EF com SQL cru: o WITH CHECK da política rejeita.
        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO personaliponto.feriados (id, tenant_id, data, descricao, abrangencia) VALUES (gen_random_uuid(), {0}, DATE '2026-12-08', 'x', 4)",
            _b.TenantId));
        Assert.Equal("42501", ex.SqlState);

        // Alterar/excluir dados do outro tenant afeta zero linhas.
        var afetadas = await db.Database.ExecuteSqlRawAsync("UPDATE personaliponto.funcionarios SET nome = 'hack' WHERE id = {0}", _b.FuncionarioId);
        Assert.Equal(0, afetadas);
    }

    [Fact]
    public async Task Toda_tabela_com_tenant_id_tem_RLS_forcada_e_politica()
    {
        await using var c = new NpgsqlConnection(fx.ConnectionString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT c.relname
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'personaliponto' AND c.relkind = 'r' AND c.relname <> '__EFMigrationsHistory'
              AND (NOT c.relrowsecurity OR NOT c.relforcerowsecurity
                   OR NOT EXISTS (SELECT 1 FROM pg_policies p WHERE p.schemaname = 'personaliponto' AND p.tablename = c.relname))
            """, c);
        var semRls = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) semRls.Add(r.GetString(0));
        Assert.Empty(semRls);
    }

    [Fact]
    public async Task Papel_da_aplicacao_nao_tem_bypassrls()
    {
        await using var c = new NpgsqlConnection(fx.ConnectionString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT rolbypassrls OR rolsuper FROM pg_roles WHERE rolname = 'personaliponto_app'", c);
        Assert.False((bool)(await cmd.ExecuteScalarAsync())!);
    }
}
