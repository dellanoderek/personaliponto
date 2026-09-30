using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PersonaliPonto.Core.RepP.Afd;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Infrastructure.Persistence;
using PersonaliPonto.Infrastructure.Seguranca;
using PersonaliPonto.Infrastructure.Tenancy;
using PersonaliPonto.Modules.SaaS.Domain;
using PersonaliPonto.Modules.SaaS.Services;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.IntegrationTests;

/// <summary>
/// Etapa E2 — apuração de uso, faturas de canal, Premium, régua de canal e RLS v3 (Postgres com personaliponto_app).
/// Inclui a prova de que canal suspenso nunca bloqueia marcação, comprovante, espelho, AFD e AEJ.
/// </summary>
[Collection("banco")]
public sealed class FaturamentoCanalTests(BancoFixture fx)
{
    private static NovoCanalRequest NovoCanal(string marca) =>
        new(marca + " LTDA", Docs.Cnpj(), marca, null, null, "Admin " + marca, $"admin.{Guid.NewGuid():N}@canal.local", null);

    private async Task<Guid> CriarRevendedorAsync(string marca, bool entrouHaMeses = true)
    {
        await using var s = fx.Escopo(sistema: true, papel: Roles.SuperAdmin);
        var id = (await s.ServiceProvider.GetRequiredService<CanalService>().CriarAsync(NovoCanal(marca), default)).CanalId;
        if (entrouHaMeses) await RetroagirEntradaAsync(id);
        return id;
    }

    private async Task<Guid> CriarParceiroAsync(Guid revendedor, string marca, bool entrouHaMeses = true)
    {
        Guid id;
        await using (var s = await fx.EscopoCanalAsync(revendedor, Roles.AdminRevendedor))
            id = (await s.ServiceProvider.GetRequiredService<CanalService>().CriarAsync(NovoCanal(marca), default)).CanalId;
        if (entrouHaMeses) await RetroagirEntradaAsync(id);
        return id;
    }

    private async Task RetroagirEntradaAsync(Guid canalId)
    {
        await using var s = fx.Escopo(sistema: true);
        await s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>().Database
            .ExecuteSqlRawAsync("UPDATE personaliponto.canais SET criado_em = now() - interval '3 months' WHERE id = {0}", canalId);
    }

    /// <summary>Cliente completo (com funcionário e PIN) movido para o canal informado.</summary>
    private async Task<BancoFixture.Cenario> ClienteDoCanalAsync(Guid canalId, string nome, string cpf, TipoEntidade tipo = TipoEntidade.Privada)
    {
        var c = await fx.CriarCenarioAsync(nome, Docs.Cnpj(), cpf);
        await using var s = fx.Escopo(sistema: true);
        await s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>().Database
            .ExecuteSqlRawAsync("UPDATE personaliponto.tenants SET canal_dono_id = {0}, tipo_entidade = {1} WHERE id = {2}", canalId, (int)tipo, c.TenantId);
        return c;
    }

    private async Task<MarcacaoRegistradaDto> MarcarAsync(BancoFixture.Cenario c)
    {
        await using var s = fx.Escopo(c.TenantId);
        var svc = s.ServiceProvider.GetRequiredService<MarcacaoService>();
        var f = await svc.IdentificarPorPinAsync(c.EstabelecimentoId, "001", c.Pin, default);
        return await svc.RegistrarAsync(f, new RegistrarMarcacaoRequest(Guid.NewGuid(), false, null, null, null, null, "teste"), Coletor.Browser, null, "127.0.0.1", default);
    }

    private DateOnly CompetenciaAtual()
    {
        using var s = fx.Escopo(sistema: true);
        var hoje = s.ServiceProvider.GetRequiredService<FaturamentoCanalService>().Hoje;
        return new DateOnly(hoje.Year, hoje.Month, 1);
    }

    private async Task<(int Apuracoes, int Faturas)> ApurarEFaturarAsync(DateOnly comp)
    {
        await using var s = fx.Escopo(sistema: true, papel: Roles.SuperAdmin);
        var a = await s.ServiceProvider.GetRequiredService<ApuracaoCanalService>().ApurarCompetenciaAsync(comp, default);
        var f = await s.ServiceProvider.GetRequiredService<FaturamentoCanalService>().GerarFaturasAsync(comp, default);
        return (a, f);
    }

    [Fact]
    public async Task Apuracao_conta_CPF_por_tenant_inclui_parceiros_e_e_idempotente_e_imutavel()
    {
        var rev = await CriarRevendedorAsync("Apura Rev");
        var par = await CriarParceiroAsync(rev, "Apura Par");
        var cpf = Docs.Cpf();
        var direto = await ClienteDoCanalAsync(rev, "Apura Direto", cpf);
        var doParceiro = await ClienteDoCanalAsync(par, "Apura Parceiro", cpf); // mesmo CPF em outro cliente conta de novo
        await ClienteDoCanalAsync(rev, "Apura Sem Marcacao", Docs.Cpf());
        var emTeste = await ClienteDoCanalAsync(rev, "Apura Em Teste", Docs.Cpf());
        await MarcarAsync(direto);
        await MarcarAsync(direto); // mesmo CPF no mesmo cliente conta 1
        await MarcarAsync(doParceiro);
        await MarcarAsync(emTeste);
        await using (var s = fx.Escopo(sistema: true))
            await s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>().Database
                .ExecuteSqlRawAsync("UPDATE personaliponto.tenants SET status = 0 WHERE id = {0}", emTeste.TenantId);

        var comp = CompetenciaAtual();
        var (apuradas, _) = await ApurarEFaturarAsync(comp);
        Assert.True(apuradas >= 1);

        await using (var s = fx.Escopo(sistema: true))
        {
            var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
            var ap = await db.ApuracoesUsoCanal.SingleAsync(a => a.CanalId == rev && a.Competencia == comp);
            Assert.Equal(2, ap.EmpresasAtivas);
            Assert.Equal(2, ap.FuncionariosAtivos);
            var det = await db.ApuracoesUsoTenant.Where(d => d.ApuracaoId == ap.Id).ToListAsync();
            Assert.Equal(4, det.Count);
            Assert.True(det.Single(d => d.ClienteId == doParceiro.TenantId) is { EmpresaAtiva: true, FuncionariosAtivos: 1 } d1 && d1.CanalDonoId == par);
            Assert.False(det.Single(d => d.ClienteId == emTeste.TenantId).EmpresaAtiva);

            // Snapshot imutável, inclusive em modo sistema.
            var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                "UPDATE personaliponto.apuracoes_uso_canal SET funcionarios_ativos = 999 WHERE id = {0}", ap.Id));
            Assert.Contains("imutável", ex.MessageText);
        }

        // Idempotente: reprocessar não cria outro snapshot para o canal.
        await ApurarEFaturarAsync(comp);
        await using (var s = fx.Escopo(sistema: true))
            Assert.Equal(1, await s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>().ApuracoesUsoCanal.CountAsync(a => a.CanalId == rev && a.Competencia == comp));
    }

    private sealed record RedeFaturada(Guid Rev, Guid Par, Guid ParIsento, Guid RevNovo, Guid FaturaRev, Guid FaturaPar, Guid FaturaParIsento);

    private static readonly SemaphoreSlim Trava = new(1, 1);
    private static RedeFaturada? _rede;

    /// <summary>Revendedor com Premium e app próprio, parceiro cobrado com mínimo, parceiro isento e revendedor novo.</summary>
    private async Task<RedeFaturada> RedeAsync()
    {
        await Trava.WaitAsync();
        try
        {
            if (_rede is not null) return _rede;
            var rev = await CriarRevendedorAsync("Fatura Rev");
            var revNovo = await CriarRevendedorAsync("Fatura Rev Novo", entrouHaMeses: false);
            var par = await CriarParceiroAsync(rev, "Fatura Par");
            var parIsento = await CriarParceiroAsync(rev, "Fatura Par Isento");
            await MarcarAsync(await ClienteDoCanalAsync(rev, "Fatura Direto", Docs.Cpf()));
            await MarcarAsync(await ClienteDoCanalAsync(par, "Fatura Do Parceiro", Docs.Cpf()));
            await MarcarAsync(await ClienteDoCanalAsync(parIsento, "Prefeitura Isenta", Docs.Cpf(), TipoEntidade.Publica));
            await MarcarAsync(await ClienteDoCanalAsync(revNovo, "Fatura Rev Novo Cliente", Docs.Cpf()));

            await using (var s = await fx.EscopoCanalAsync(rev, Roles.AdminRevendedor))
            {
                var fat = s.ServiceProvider.GetRequiredService<FaturamentoCanalService>();
                await fat.AtivarPremiumAsync(default);
                await fat.DefinirPrecoParceiroAsync(par, 1.50m, 50m, null, default);
                await fat.DefinirPrecoParceiroAsync(parIsento, 2m, null, 1, default);
            }
            await using (var s = fx.Escopo(sistema: true, papel: Roles.SuperAdmin))
            {
                var fat = s.ServiceProvider.GetRequiredService<FaturamentoCanalService>();
                await fat.DefinirCondicoesAsync(rev, new CondicoesCanal(null, true, fat.Hoje), default);
            }

            var comp = CompetenciaAtual();
            await ApurarEFaturarAsync(comp);
            await using var q = fx.Escopo(sistema: true);
            var db = q.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
            Guid F(Guid pagador) => db.FaturasCanal.Where(f => f.CanalPagadorId == pagador && f.Competencia == comp && f.Tipo != TipoFaturaCanal.Avulsa).Select(f => f.Id).Single();
            return _rede = new RedeFaturada(rev, par, parIsento, revNovo, F(rev), F(par), F(parIsento));
        }
        finally { Trava.Release(); }
    }

    [Fact]
    public async Task Faturas_do_Owner_e_do_parceiro_com_minimo_premium_avulsos_isencao_e_sem_pro_rata()
    {
        var r = await RedeAsync();
        await using var s = fx.Escopo(sistema: true);
        var fat = s.ServiceProvider.GetRequiredService<FaturamentoCanalService>();

        var fr = (await fat.FaturaAsync(r.FaturaRev, default))!;
        Assert.Equal(TipoFaturaCanal.Plataforma, fr.Tipo);
        Assert.Equal(Canal.OwnerRaizId, fr.CanalRecebedorId);
        // 3 empresas ativas (direto + 2 de parceiros) × 12 + 3 funcionários × 0,30 = 36,90 → mínimo 290; + Premium 149 + app 200/mês.
        // A implantação do app próprio (1.500) é cobrada na hora da contratação, em fatura avulsa (E3).
        Assert.Equal(290m + 149m + 200m, fr.Valor);
        Assert.Equal(StatusFatura.Aberta, fr.Status);
        Assert.Contains(fr.Itens, i => i.Tipo == TipoItemFaturaCanal.AjusteMinimo && i.Valor == 290m - 36.90m);
        Assert.Contains(fr.Itens, i => i.Tipo == TipoItemFaturaCanal.Premium && i.Valor == 149m);
        Assert.DoesNotContain(fr.Itens, i => i.Tipo == TipoItemFaturaCanal.AppProprioImplantacao);
        var avulsa = await s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>().FaturasCanal.Include(f => f.Itens)
            .SingleAsync(f => f.CanalPagadorId == r.Rev && f.Tipo == TipoFaturaCanal.Avulsa);
        Assert.Equal(1500m, avulsa.Valor);
        Assert.Contains(avulsa.Itens, i => i.Tipo == TipoItemFaturaCanal.AppProprioImplantacao);
        Assert.Equal(FaturamentoCanalService.VencimentoDe(fr.Competencia), fr.Vencimento);

        var fp = (await fat.FaturaAsync(r.FaturaPar, default))!;
        Assert.Equal(r.Rev, fp.CanalRecebedorId);
        Assert.Equal(50m, fp.Valor); // 1 × 1,50 + complemento ao mínimo de 50
        var fi = (await fat.FaturaAsync(r.FaturaParIsento, default))!;
        Assert.Equal(StatusFatura.NaoCobrada, fi.Status);
        Assert.Equal(0m, fi.Valor);
        Assert.Contains(fi.Itens, i => i.Tipo == TipoItemFaturaCanal.Isencao);

        // Revendedor que entrou na competência não é cobrado (primeira cobrança no mês seguinte).
        var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
        Assert.False(await db.FaturasCanal.AnyAsync(f => f.CanalPagadorId == r.RevNovo));
        // Idempotente, e a implantação do app próprio é cobrada uma única vez.
        s.ServiceProvider.GetRequiredService<RequestContext>().DefinirUsuario(Guid.NewGuid(), "Owner", null, Roles.SuperAdmin, null);
        Assert.Equal(0, await fat.GerarFaturasAsync(fr.Competencia, default));
        Assert.True((await db.Canais.SingleAsync(c => c.Id == r.Rev)).AppProprioImplantacaoCobrada);
    }

    [Fact]
    public async Task Premium_isento_ate_data_sai_com_valor_zero()
    {
        var rev = await CriarRevendedorAsync("Fundador");
        await MarcarAsync(await ClienteDoCanalAsync(rev, "Fundador Cliente", Docs.Cpf()));
        await using (var s = await fx.EscopoCanalAsync(rev, Roles.AdminRevendedor))
        {
            var fat = s.ServiceProvider.GetRequiredService<FaturamentoCanalService>();
            await Assert.ThrowsAsync<AcessoNegadoException>(() => fat.DefinirCondicoesAsync(rev, new CondicoesCanal(new DateOnly(2099, 1, 1), false, null), default));
            await fat.AtivarPremiumAsync(default);
            Assert.True(await fat.PremiumAtivoAsync(rev, default));
            await Assert.ThrowsAsync<RegraNegocioException>(() => fat.AtivarPremiumAsync(default));
        }
        await using (var s = fx.Escopo(sistema: true, papel: Roles.SuperAdmin))
        {
            var fat = s.ServiceProvider.GetRequiredService<FaturamentoCanalService>();
            await fat.DefinirCondicoesAsync(rev, new CondicoesCanal(fat.Hoje.AddMonths(24), false, null), default);
        }
        var comp = CompetenciaAtual();
        await ApurarEFaturarAsync(comp);
        await using (var s = fx.Escopo(sistema: true))
        {
            var f = await s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>().FaturasCanal.Include(x => x.Itens)
                .SingleAsync(x => x.CanalPagadorId == rev && x.Competencia == comp);
            Assert.Equal(290m, f.Valor);
            Assert.Contains(f.Itens, i => i.Tipo == TipoItemFaturaCanal.PremiumIsento && i.Valor == 0);
            Assert.DoesNotContain(f.Itens, i => i.Tipo == TipoItemFaturaCanal.Premium);
        }
        await using (var s = await fx.EscopoCanalAsync(rev, Roles.AdminRevendedor))
        {
            var fat = s.ServiceProvider.GetRequiredService<FaturamentoCanalService>();
            await fat.CancelarPremiumAsync(default);
            Assert.False(await fat.PremiumAtivoAsync(rev, default));
        }
    }

    [Fact]
    public async Task RLS_faturas_de_canal_visiveis_ao_pagador_e_recebedor_parceiro_nao_ve_a_do_revendedor()
    {
        var r = await RedeAsync();
        await using (var s = await fx.EscopoCanalAsync(r.Par, Roles.AdminParceiro))
        {
            var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
            Assert.Equal([r.FaturaPar], await db.FaturasCanal.Select(f => f.Id).ToListAsync());
            Assert.Equal([r.FaturaPar], await db.Database.SqlQueryRaw<Guid>("SELECT id AS \"Value\" FROM personaliponto.faturas_canal").ToListAsync());
            Assert.Empty(await db.Database.SqlQueryRaw<Guid>("SELECT id AS \"Value\" FROM personaliponto.itens_fatura_canal WHERE fatura_canal_id = {0}", r.FaturaRev).ToListAsync());
            Assert.Empty(await db.Database.SqlQueryRaw<Guid>("SELECT id AS \"Value\" FROM personaliponto.apuracoes_uso_canal").ToListAsync());
            Assert.Empty(await db.Database.SqlQueryRaw<Guid>("SELECT id AS \"Value\" FROM personaliponto.assinaturas_premium").ToListAsync());

            // Parceiro não dá baixa na própria fatura (serviço, RLS e trigger).
            var fat = s.ServiceProvider.GetRequiredService<FaturamentoCanalService>();
            await Assert.ThrowsAsync<AcessoNegadoException>(() => fat.RegistrarPagamentoAsync(r.FaturaPar, fat.Hoje, 50m, "PIX", null, default));
            Assert.Equal(0, await db.Database.ExecuteSqlRawAsync("UPDATE personaliponto.faturas_canal SET status = 1 WHERE id = {0}", r.FaturaPar));
            Assert.Equal(0, await db.Database.ExecuteSqlRawAsync("DELETE FROM personaliponto.faturas_canal WHERE id = {0}", r.FaturaPar));
            // Nem altera o próprio preço.
            var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                "UPDATE personaliponto.canais SET preco_funcionario_parceiro = 0 WHERE id = {0}", r.Par));
            Assert.Equal("42501", ex.SqlState);
        }

        await using (var s = await fx.EscopoCanalAsync(r.Rev, Roles.AdminRevendedor))
        {
            var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
            var visiveis = await db.Database.SqlQueryRaw<Guid>("SELECT id AS \"Value\" FROM personaliponto.faturas_canal WHERE tipo <> 3").ToListAsync();
            Assert.Equal(new[] { r.FaturaRev, r.FaturaPar, r.FaturaParIsento }.Order(), visiveis.Order());
            Assert.NotEmpty(await db.ItensFaturaCanal.Where(i => i.FaturaCanalId == r.FaturaRev).ToListAsync());

            // Revendedor não marca a própria fatura com o Owner como paga nem cria faturas.
            Assert.Equal(0, await db.Database.ExecuteSqlRawAsync("UPDATE personaliponto.faturas_canal SET status = 1 WHERE id = {0}", r.FaturaRev));
            var ins = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                "INSERT INTO personaliponto.faturas_canal (id, tipo, canal_pagador_id, canal_recebedor_id, competencia, valor, vencimento, status, criada_em) " +
                "VALUES (gen_random_uuid(), 2, {0}, {1}, DATE '2020-01-01', 1, DATE '2020-02-10', 0, now())", r.Par, r.Rev));
            Assert.Equal("42501", ins.SqlState);
            // Recebedor só altera campos de baixa.
            var val = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                "UPDATE personaliponto.faturas_canal SET valor = 1 WHERE id = {0}", r.FaturaPar));
            Assert.Equal("42501", val.SqlState);
            // Nem concede isenção de Premium a si mesmo.
            var isencao = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                "UPDATE personaliponto.canais SET premium_isento_ate = DATE '2099-01-01' WHERE id = {0}", r.Rev));
            Assert.Equal("42501", isencao.SqlState);
            await Assert.ThrowsAsync<AcessoNegadoException>(() => s.ServiceProvider.GetRequiredService<FaturamentoCanalService>()
                .RegistrarPagamentoAsync(r.FaturaRev, DateOnly.FromDateTime(DateTime.Today), 1m, null, null, default));
        }

        // Outro revendedor não vê nada.
        var outro = await CriarRevendedorAsync("Fatura Outro");
        await using (var s = await fx.EscopoCanalAsync(outro, Roles.AdminRevendedor))
        {
            var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
            Assert.Empty(await db.Database.SqlQueryRaw<Guid>("SELECT id AS \"Value\" FROM personaliponto.faturas_canal WHERE canal_pagador_id IN ({0}, {1})", r.Rev, r.Par).ToListAsync());
        }
    }

    [Fact]
    public async Task Revendedor_da_baixa_na_fatura_do_parceiro()
    {
        var r = await RedeAsync();
        await using var s = await fx.EscopoCanalAsync(r.Rev, Roles.AdminRevendedor);
        var fat = s.ServiceProvider.GetRequiredService<FaturamentoCanalService>();
        await fat.RegistrarPagamentoAsync(r.FaturaPar, fat.Hoje, 50m, "PIX", "ok", default);
        Assert.Equal(StatusFatura.Paga, (await fat.FaturaAsync(r.FaturaPar, default))!.Status);
        await fat.EstornarAsync(r.FaturaPar, "teste de estorno", default);
        Assert.Equal(StatusFatura.Aberta, (await fat.FaturaAsync(r.FaturaPar, default))!.Status);
    }

    private async Task VencerAsync(Guid faturaId, int dias)
    {
        await using var s = fx.Escopo(sistema: true);
        await s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>().Database.ExecuteSqlRawAsync(
            "UPDATE personaliponto.faturas_canal SET vencimento = (now() AT TIME ZONE 'America/Sao_Paulo')::date - {0} WHERE id = {1}", dias, faturaId);
    }

    private async Task<StatusCanal> AvaliarAsync(Guid canal)
    {
        await using var s = fx.Escopo(sistema: true, papel: Roles.SuperAdmin);
        await s.ServiceProvider.GetRequiredService<FaturamentoCanalService>().AvaliarCanalAsync(canal, default);
        return await s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>().Canais.Where(c => c.Id == canal).Select(c => c.Status).SingleAsync();
    }

    [Fact]
    public async Task Regua_de_canal_D5_D15_D30_e_volta_a_ativo_ao_regularizar_sem_desfazer_suspensao_manual()
    {
        var rev = await CriarRevendedorAsync("Regua Rev");
        await MarcarAsync(await ClienteDoCanalAsync(rev, "Regua Cliente", Docs.Cpf()));
        await ApurarEFaturarAsync(CompetenciaAtual());
        Guid fatura;
        await using (var s = fx.Escopo(sistema: true))
            fatura = await s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>().FaturasCanal.Where(f => f.CanalPagadorId == rev).Select(f => f.Id).SingleAsync();

        await VencerAsync(fatura, 4);
        Assert.Equal(StatusCanal.Ativo, await AvaliarAsync(rev));
        await VencerAsync(fatura, 5);
        Assert.Equal(StatusCanal.Aviso, await AvaliarAsync(rev));
        await VencerAsync(fatura, 15);
        Assert.Equal(StatusCanal.PainelBloqueado, await AvaliarAsync(rev));
        await VencerAsync(fatura, 30);
        Assert.Equal(StatusCanal.Suspenso, await AvaliarAsync(rev));

        // Painel bloqueado/suspenso: usuário de canal não grava nada, mas consulta as faturas.
        await using (var s = await fx.EscopoCanalAsync(rev, Roles.AdminRevendedor))
        {
            Assert.True(s.ServiceProvider.GetRequiredService<RequestContext>().PainelCanalBloqueado);
            Assert.Single(await s.ServiceProvider.GetRequiredService<FaturamentoCanalService>().FaturasPagasPorAsync(rev, default));
            await Assert.ThrowsAsync<AcessoNegadoException>(() => s.ServiceProvider.GetRequiredService<CanalService>().CriarAsync(NovoCanal("Bloqueado Par"), default));
        }
        await using (var s = await fx.EscopoCanalAsync(rev, Roles.AdminRevendedor))
            await Assert.ThrowsAsync<AcessoNegadoException>(() => s.ServiceProvider.GetRequiredService<FaturamentoCanalService>().AtivarPremiumAsync(default));

        // Regularizou → volta a Ativo automaticamente.
        await using (var s = fx.Escopo(sistema: true, papel: Roles.SuperAdmin))
        {
            var fat = s.ServiceProvider.GetRequiredService<FaturamentoCanalService>();
            await fat.RegistrarPagamentoAsync(fatura, fat.Hoje, 290m, "PIX", null, default);
        }
        Assert.Equal(StatusCanal.Ativo, await AvaliarAsync(rev));

        // Suspensão manual pela plataforma não é desfeita pela régua.
        await using (var s = fx.Escopo(sistema: true, papel: Roles.SuperAdmin))
            await s.ServiceProvider.GetRequiredService<CanalService>().AlterarStatusAsync(rev, StatusCanal.Suspenso, default);
        Assert.Equal(StatusCanal.Suspenso, await AvaliarAsync(rev));
        await using (var s = fx.Escopo(sistema: true, papel: Roles.SuperAdmin))
            Assert.True(await s.ServiceProvider.GetRequiredService<FaturamentoCanalService>().ProcessarReguaAsync(default) >= 0);
        Assert.Equal(StatusCanal.Suspenso, await AvaliarAsync(rev));
    }

    [Fact]
    public async Task Canal_suspenso_nunca_bloqueia_marcacao_comprovante_espelho_AFD_e_AEJ()
    {
        var rev = await CriarRevendedorAsync("Suspenso Rev");
        var par = await CriarParceiroAsync(rev, "Suspenso Par");
        var direto = await ClienteDoCanalAsync(rev, "Suspenso Direto", Docs.Cpf());
        var doParceiro = await ClienteDoCanalAsync(par, "Suspenso Do Parceiro", Docs.Cpf());
        await using (var s = fx.Escopo(sistema: true, papel: Roles.SuperAdmin))
        {
            var canais = s.ServiceProvider.GetRequiredService<CanalService>();
            await canais.AlterarStatusAsync(rev, StatusCanal.Suspenso, default);
            await canais.AlterarStatusAsync(par, StatusCanal.Suspenso, default);
        }

        foreach (var c in new[] { direto, doParceiro })
        {
            var m = await MarcarAsync(c);
            Assert.False(m.Duplicada);
            await using var s = fx.Escopo(c.TenantId);
            var sp = s.ServiceProvider;
            Assert.True((await sp.GetRequiredService<TenantUsoPolicy>().PodeUsarAsync(c.TenantId, default)).Pode);
            var (_, pdf) = await sp.GetRequiredService<ComprovanteService>().PdfAsync(m.Id, c.FuncionarioId, default);
            Assert.StartsWith("%PDF", System.Text.Encoding.Latin1.GetString(pdf, 0, 4));
            var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
            var espelho = await sp.GetRequiredService<EspelhoService>().GerarAsync(c.FuncionarioId, hoje.AddDays(-1), hoje.AddDays(1), default);
            Assert.Contains(espelho.Dias.SelectMany(d => d.Marcacoes), x => x.Nsr == m.Nsr);
            var arquivos = sp.GetRequiredService<ArquivosFiscaisService>();
            var afd = await arquivos.GerarAfdAsync(c.EstabelecimentoId, hoje.AddDays(-1), hoje.AddDays(1), default);
            Assert.Empty(AfdValidator.Validar(afd.Conteudo));
            var aej = await arquivos.GerarAejAsync(c.EstabelecimentoId, hoje.AddDays(-1), hoje.AddDays(1), default);
            Assert.NotEmpty(aej.Conteudo);
            // Cliente recebe o aviso no painel de RH (canal acima dele suspenso).
            Assert.True(await sp.GetRequiredService<SituacaoCanalCliente>().CanalSuspensoAsync(c.TenantId, default));
        }

        // A régua de canais nunca altera a situação dos clientes.
        await using (var s = fx.Escopo(sistema: true, papel: Roles.SuperAdmin))
        {
            await s.ServiceProvider.GetRequiredService<FaturamentoCanalService>().ProcessarReguaAsync(default);
            var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
            Assert.All(await db.Tenants.Where(t => t.Id == direto.TenantId || t.Id == doParceiro.TenantId).ToListAsync(), t => Assert.Equal(TenantStatus.Ativo, t.Status));
        }

        // Cliente de canal ativo não recebe aviso.
        var ativo = await ClienteDoCanalAsync(await CriarRevendedorAsync("Ativo Rev"), "Cliente Canal Ativo", Docs.Cpf());
        await using (var s = fx.Escopo(ativo.TenantId))
            Assert.False(await s.ServiceProvider.GetRequiredService<SituacaoCanalCliente>().CanalSuspensoAsync(ativo.TenantId, default));
    }

    // ---------------- Correções da revisão de segurança da E1 ----------------

    private static ClaimsPrincipal Plataforma(string papel) => new(new ClaimsIdentity(
    [
        new Claim(PersonaliPontoClaims.UserId, Guid.NewGuid().ToString()),
        new Claim(ClaimTypes.Name, "Equipe plataforma"),
        new Claim(ClaimTypes.Role, papel)
    ], "teste", ClaimTypes.Name, ClaimTypes.Role));

    [Theory]
    [InlineData(Roles.SuperAdmin, Roles.AdminRevendedor)]
    [InlineData(Roles.Suporte, Roles.SuporteRevendedor)]
    public async Task Suporte_da_plataforma_no_canal_recebe_papel_conforme_origem(string papel, string papelNoCanal)
    {
        var rev = await CriarRevendedorAsync("Suporte Papel " + papel);
        await using var s = fx.Escopo(sistema: true, papel: papel);
        var claims = await s.ServiceProvider.GetRequiredService<SuporteService>().IniciarCanalAsync(Plataforma(papel), rev, "Chamado 11 - configuração", default);
        var papeis = claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList();
        Assert.Equal([papelNoCanal, papel], papeis);
        Assert.Equal(Roles.SuporteParceiro, SuporteService.PapelNoCanal(Roles.Suporte, TipoCanal.Parceiro));
        Assert.Equal(Roles.AdminParceiro, SuporteService.PapelNoCanal(Roles.SuperAdmin, TipoCanal.Parceiro));
    }

    [Fact]
    public async Task Acessos_de_suporte_da_plataforma_nao_sao_visiveis_ao_canal_nem_ao_cliente()
    {
        var rev = await CriarRevendedorAsync("Suporte Visib");
        var cliente = await ClienteDoCanalAsync(rev, "Suporte Visib Cliente", Docs.Cpf());
        await using (var s = fx.Escopo(sistema: true, papel: Roles.SuperAdmin))
        {
            var sup = s.ServiceProvider.GetRequiredService<SuporteService>();
            await sup.IniciarClienteAsync(Plataforma(Roles.SuperAdmin), cliente.TenantId, "Chamado 12 - conferência", default);
            await sup.IniciarCanalAsync(Plataforma(Roles.SuperAdmin), rev, "Chamado 13 - painel do canal", default);
            var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
            var registros = await db.AcessosSuporte.Where(a => a.TenantId == cliente.TenantId || a.CanalAlvoId == rev).ToListAsync();
            Assert.Equal(2, registros.Count);
            Assert.All(registros, a => Assert.Equal(Canal.OwnerRaizId, a.CanalId));
        }
        // Revendedor iniciando suporte registra o próprio canal (visível a ele).
        await using (var s = await fx.EscopoCanalAsync(rev, Roles.AdminRevendedor))
        {
            var u = new ClaimsPrincipal(new ClaimsIdentity([new Claim(PersonaliPontoClaims.UserId, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, Roles.AdminRevendedor),
                new Claim(PersonaliPontoClaims.CanalId, rev.ToString())], "t", ClaimTypes.Name, ClaimTypes.Role));
            await s.ServiceProvider.GetRequiredService<SuporteService>().IniciarClienteAsync(u, cliente.TenantId, "Chamado 14 - revendedor", default);
        }
        await using (var s = await fx.EscopoCanalAsync(rev, Roles.AdminRevendedor))
        {
            var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
            var motivos = await db.Database.SqlQueryRaw<string>("SELECT motivo AS \"Value\" FROM personaliponto.acessos_suporte").ToListAsync();
            Assert.Equal(["Chamado 14 - revendedor"], motivos);
            // Canal não altera nem apaga registros de acesso (só SELECT/INSERT fora do modo sistema).
            Assert.Equal(0, await db.Database.ExecuteSqlRawAsync("UPDATE personaliponto.acessos_suporte SET motivo = 'x'"));
            Assert.Equal(0, await db.Database.ExecuteSqlRawAsync("DELETE FROM personaliponto.acessos_suporte"));
            Assert.Equal(0, await db.Database.ExecuteSqlRawAsync("DELETE FROM personaliponto.audit_logs"));
        }
        await using (var s = fx.Escopo(cliente.TenantId))
        {
            var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
            Assert.Empty(await db.Database.SqlQueryRaw<Guid>("SELECT id AS \"Value\" FROM personaliponto.acessos_suporte").ToListAsync());
        }
    }

    [Fact]
    public async Task Cliente_nao_altera_o_proprio_financeiro_e_canal_nao_apaga_faturas()
    {
        var rev = await CriarRevendedorAsync("Financeiro Rev");
        Guid tenant;
        await using (var s = await fx.EscopoCanalAsync(rev, Roles.AdminRevendedor))
        {
            tenant = (await s.ServiceProvider.GetRequiredService<ClienteService>().CadastrarAsync(new NovoClienteRequest(
                "FINANCEIRO CLIENTE", Docs.Cnpj(), null, null, "RH", null, 100m, 10, 0m, null, 10, null, "Rua A, 1", false), default)).TenantId;
            await s.ServiceProvider.GetRequiredService<FaturamentoService>().GerarCompetenciaAsync(CompetenciaAtual(), default);
        }
        await using (var s = fx.Escopo(tenant, papel: Roles.AdminEmpresa))
        {
            var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
            Assert.NotEmpty(await db.Faturas.ToListAsync());
            Assert.Equal(0, await db.Database.ExecuteSqlRawAsync("UPDATE personaliponto.faturas SET status = 1"));
            Assert.Equal(0, await db.Database.ExecuteSqlRawAsync("UPDATE personaliponto.assinaturas SET valor_mensal = 0"));
            Assert.Equal(0, await db.Database.ExecuteSqlRawAsync("DELETE FROM personaliponto.faturas"));
        }
        await using (var s = await fx.EscopoCanalAsync(rev, Roles.AdminRevendedor))
        {
            var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
            Assert.Equal(0, await db.Database.ExecuteSqlRawAsync("DELETE FROM personaliponto.faturas WHERE tenant_id = {0}", tenant));
            // O revendedor continua registrando o pagamento dos próprios clientes.
            var fatura = await db.Faturas.FirstAsync(f => f.TenantId == tenant);
            await s.ServiceProvider.GetRequiredService<FaturamentoService>().RegistrarPagamentoAsync(fatura.Id, DateOnly.FromDateTime(DateTime.Today), 100m, "PIX", null, default);
            Assert.Equal(StatusFatura.Paga, (await db.Faturas.AsNoTracking().FirstAsync(f => f.Id == fatura.Id)).Status);
        }
    }

    [Fact]
    public async Task Interceptor_reaplica_contexto_em_comandos_escalares()
    {
        var c = await fx.CriarCenarioAsync("Escalar", Docs.Cnpj(), Docs.Cpf());
        await using var s = fx.Escopo(sistema: true);
        var ctx = s.ServiceProvider.GetRequiredService<RequestContext>();
        var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
        await db.Database.OpenConnectionAsync();
        try
        {
            Assert.True(await db.Tenants.CountAsync() > 1); // conexão já com bypass de sistema
            ctx.DefinirTenant(c.TenantId);
            var cmd = db.GetService<IRelationalCommandBuilderFactory>().Create().Append("SELECT count(*) FROM personaliponto.tenants").Build();
            var total = await cmd.ExecuteScalarAsync(new RelationalCommandParameterObject(db.GetService<IRelationalConnection>(), null, null, db,
                db.GetService<Microsoft.EntityFrameworkCore.Diagnostics.IRelationalCommandDiagnosticsLogger>()));
            Assert.Equal(1L, Convert.ToInt64(total));
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}
