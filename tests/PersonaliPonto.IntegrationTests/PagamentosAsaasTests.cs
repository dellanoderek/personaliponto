using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Infrastructure.Pagamentos;
using PersonaliPonto.Infrastructure.Persistence;
using PersonaliPonto.Infrastructure.Rotinas;
using PersonaliPonto.Infrastructure.Tenancy;
using PersonaliPonto.Modules.SaaS.Domain;
using PersonaliPonto.Modules.SaaS.Services;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.IntegrationTests;

/// <summary>
/// E3/E9 — Asaas da plataforma e do revendedor (gateway fake, Postgres real com RLS): emissão da cobrança via outbox,
/// webhook idempotente e tolerante a ordem, estorno, régua, cliente público sem cobrança, roteamento por conta e chave cifrada.
/// </summary>
[Collection("banco")]
public sealed class PagamentosAsaasTests(BancoFixture fx)
{
    private static NovoCanalRequest NovoCanal(string marca) =>
        new(marca + " LTDA", Docs.Cnpj(), marca, null, null, "Admin " + marca, $"admin.{Guid.NewGuid():N}@canal.local", null);

    private async Task<Guid> RevendedorAsync(string marca)
    {
        Guid id;
        await using (var s = fx.Escopo(sistema: true, papel: Roles.SuperAdmin))
            id = (await s.ServiceProvider.GetRequiredService<CanalService>().CriarAsync(NovoCanal(marca), default)).CanalId;
        await SqlAsync("UPDATE personaliponto.canais SET criado_em = now() - interval '3 months' WHERE id = {0}", id);
        return id;
    }

    private async Task SqlAsync(string sql, params object[] p)
    {
        await using var s = fx.Escopo(sistema: true);
        await s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>().Database.ExecuteSqlRawAsync(sql, p);
    }

    private async Task<Guid> AtivarPremiumAsync(Guid rev)
    {
        await using (var s = await fx.EscopoCanalAsync(rev, Roles.AdminRevendedor))
            await s.ServiceProvider.GetRequiredService<ComprasCanalService>().AtivarPremiumAsync(default);
        await using var q = fx.Escopo(sistema: true);
        return await q.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>().FaturasCanal
            .Where(f => f.CanalPagadorId == rev && f.Tipo == TipoFaturaCanal.Avulsa).Select(f => f.Id).SingleAsync();
    }

    private async Task ProcessarOutboxAsync()
    {
        var proc = fx.Services.GetRequiredService<OutboxProcessor>();
        for (var i = 0; i < 20 && await proc.ProcessarLoteAsync(default) > 0; i++) { }
    }

    private async Task<FaturaCanal> FaturaCanalAsync(Guid id)
    {
        await using var s = fx.Escopo(sistema: true);
        return await s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>().FaturasCanal.AsNoTracking().SingleAsync(f => f.Id == id);
    }

    private async Task<ResultadoWebhook> WebhookAsync(string token, string eventoId, string tipo, string cobrancaId, string quando, decimal valor = 149m)
    {
        var corpo = JsonSerializer.Serialize(new
        {
            id = eventoId, @event = tipo, dateCreated = quando,
            payment = new { id = cobrancaId, value = valor, netValue = valor - 1, billingType = "PIX", paymentDate = quando[..10], status = "RECEIVED" }
        });
        await using var s = fx.Services.CreateAsyncScope();
        return await s.ServiceProvider.GetRequiredService<WebhookAsaasService>().ReceberAsync(token, corpo, default);
    }

    private async Task<int> AuditoriasAsync(string acao, Guid entidade)
    {
        await using var s = fx.Escopo(sistema: true);
        var id = entidade.ToString();
        return await s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>().AuditLogs.CountAsync(a => a.Acao == acao && a.EntidadeId == id);
    }

    [Fact]
    public async Task Compra_do_Premium_gera_fatura_avulsa_e_cobranca_no_Asaas_da_plataforma_com_pix_boleto_e_link()
    {
        var rev = await RevendedorAsync("Asaas Premium");
        var id = await AtivarPremiumAsync(rev);
        var f = await FaturaCanalAsync(id);
        Assert.Equal(149m, f.Valor);
        Assert.Equal(StatusCobrancaGateway.Pendente, f.GatewayStatus);

        await ProcessarOutboxAsync();
        f = await FaturaCanalAsync(id);
        Assert.Equal(StatusCobrancaGateway.Emitida, f.GatewayStatus);
        Assert.NotNull(f.GatewayCobrancaId);
        Assert.Null(f.GatewayContaId); // conta da plataforma
        Assert.StartsWith("00020101", f.GatewayPixCopiaCola);
        Assert.NotNull(f.GatewayPixQrCode);
        Assert.NotNull(f.GatewayLinhaDigitavel);
        Assert.NotNull(f.GatewayLink);
        var (cob, chave) = fx.Gateway.Cobrancas[f.GatewayCobrancaId!];
        Assert.Equal(BancoFixture.ChaveOwner, chave);
        Assert.Equal($"canal:{f.Id}", cob.ReferenciaExterna);
        Assert.Equal(FormaCobranca.Indefinida, cob.Forma); // pagador escolhe Pix, boleto ou cartão
        Assert.Contains(fx.Gateway.Clientes, c => c.Value.ReferenciaExterna == $"canal:{rev}");

        // Reprocessar não cria outra cobrança; a fatura mensal da competência não repete o Premium.
        await using (var s = fx.Escopo(sistema: true))
            await s.ServiceProvider.GetRequiredService<CobrancaGatewayHandler>().EmitirAsync(new PedidoCobrancaGateway(PedidoCobrancaGateway.FaturaCanal, id), default);
        Assert.Single(fx.Gateway.Cobrancas, c => c.Value.Cobranca.ReferenciaExterna == $"canal:{id}");
    }

    [Fact]
    public async Task Webhook_da_plataforma_da_baixa_idempotente_e_reavalia_a_regua()
    {
        var rev = await RevendedorAsync("Asaas Regua");
        var id = await AtivarPremiumAsync(rev);
        await ProcessarOutboxAsync();
        var pay = (await FaturaCanalAsync(id)).GatewayCobrancaId!;
        // Fatura vencida há 20 dias → painel bloqueado.
        await SqlAsync("UPDATE personaliponto.faturas_canal SET vencimento = CURRENT_DATE - 20 WHERE id = {0}", id);
        await using (var s = fx.Escopo(sistema: true))
            await s.ServiceProvider.GetRequiredService<FaturamentoCanalService>().AvaliarCanalAsync(rev, default);
        await using (var s = fx.Escopo(sistema: true))
            Assert.Equal(StatusCanal.PainelBloqueado, (await s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>().Canais.SingleAsync(c => c.Id == rev)).Status);

        Assert.Equal(ResultadoWebhook.NaoAutorizado, await WebhookAsync("token-errado-0123456789012345678901234", "evt_x", "PAYMENT_RECEIVED", pay, "2026-09-30 10:00:00"));
        var evt = "evt_" + Guid.NewGuid().ToString("N");
        Assert.Equal(ResultadoWebhook.Recebido, await WebhookAsync(BancoFixture.TokenOwner, evt, "PAYMENT_RECEIVED", pay, "2026-09-30 10:00:00"));
        Assert.Equal(ResultadoWebhook.Duplicado, await WebhookAsync(BancoFixture.TokenOwner, evt, "PAYMENT_RECEIVED", pay, "2026-09-30 10:00:00"));
        // CONFIRMED depois de RECEIVED (cartão) não baixa de novo.
        Assert.Equal(ResultadoWebhook.Recebido, await WebhookAsync(BancoFixture.TokenOwner, evt + "b", "PAYMENT_CONFIRMED", pay, "2026-09-30 10:00:01"));

        var f = await FaturaCanalAsync(id);
        Assert.Equal(StatusFatura.Paga, f.Status);
        Assert.Equal(149m, f.ValorPago);
        Assert.Equal("Asaas · Pix", f.FormaPagamento);
        Assert.Equal(1, await AuditoriasAsync("fatura_canal.paga_gateway", id));
        await using (var s = fx.Escopo(sistema: true))
            Assert.Equal(StatusCanal.Ativo, (await s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>().Canais.SingleAsync(c => c.Id == rev)).Status);
    }

    [Fact]
    public async Task Estorno_reabre_a_fatura_e_evento_fora_de_ordem_e_ignorado()
    {
        var rev = await RevendedorAsync("Asaas Estorno");
        var id = await AtivarPremiumAsync(rev);
        await ProcessarOutboxAsync();
        var pay = (await FaturaCanalAsync(id)).GatewayCobrancaId!;

        await WebhookAsync(BancoFixture.TokenOwner, "evt_r_" + id, "PAYMENT_RECEIVED", pay, "2026-09-30 10:00:00");
        Assert.Equal(StatusFatura.Paga, (await FaturaCanalAsync(id)).Status);
        await WebhookAsync(BancoFixture.TokenOwner, "evt_e_" + id, "PAYMENT_REFUNDED", pay, "2026-09-30 12:00:00");
        var f = await FaturaCanalAsync(id);
        Assert.Equal(StatusFatura.Aberta, f.Status);
        Assert.Equal(StatusCobrancaGateway.Estornada, f.GatewayStatus);
        Assert.Equal(1, await AuditoriasAsync("fatura_canal.estornada_gateway", id));

        // Evento atrasado (anterior ao estorno) chega depois: não pode voltar a baixar a fatura.
        await WebhookAsync(BancoFixture.TokenOwner, "evt_c_" + id, "PAYMENT_CONFIRMED", pay, "2026-09-30 11:00:00");
        Assert.Equal(StatusFatura.Aberta, (await FaturaCanalAsync(id)).Status);
        await using var s = fx.Escopo(sistema: true);
        var ev = await s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>().EventosGateway.SingleAsync(e => e.EventoId == "evt_c_" + id);
        Assert.Equal("ignorado: fora de ordem", ev.Resultado);
    }

    [Fact]
    public async Task Falha_do_Asaas_nao_impede_a_fatura_e_fica_pendente_com_retentativa()
    {
        var rev = await RevendedorAsync("Asaas Falha");
        fx.Gateway.ChavesComFalha[BancoFixture.ChaveOwner] = 1;
        try
        {
            var id = await AtivarPremiumAsync(rev); // a fatura é gerada mesmo com o Asaas fora
            await ProcessarOutboxAsync();
            var f = await FaturaCanalAsync(id);
            Assert.Equal(StatusFatura.Aberta, f.Status);
            Assert.Equal(StatusCobrancaGateway.Falha, f.GatewayStatus);
            Assert.Contains("indisponível", f.GatewayErro);
            await using var s = fx.Escopo(sistema: true);
            var msg = (await s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>().Outbox.AsNoTracking()
                .Where(m => m.Tipo == PedidoCobrancaGateway.TipoMensagem).ToListAsync()).Single(m => m.Payload.Contains(id.ToString()));
            Assert.Null(msg.ProcessadoEm);
            Assert.Equal(1, msg.Tentativas);
            Assert.NotNull(msg.ProximaTentativa);

            // Asaas volta: a retentativa emite a cobrança.
            fx.Gateway.ChavesComFalha.Clear();
            await using (var h = fx.Escopo(sistema: true))
            {
                await h.ServiceProvider.GetRequiredService<CobrancaGatewayHandler>().EmitirAsync(new PedidoCobrancaGateway(PedidoCobrancaGateway.FaturaCanal, id), default);
                await h.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>().SaveChangesAsync();
            }
            Assert.Equal(StatusCobrancaGateway.Emitida, (await FaturaCanalAsync(id)).GatewayStatus);
        }
        finally { fx.Gateway.ChavesComFalha.Clear(); }
    }

    private const string ChaveRevA = "$aact_hmlg_revendedorA_000000000000000001";
    private const string ChaveRevB = "$aact_hmlg_revendedorB_000000000000000002";

    private async Task<string> ConectarAsync(Guid rev, string chave)
    {
        await using (var s = await fx.EscopoCanalAsync(rev, Roles.AdminRevendedor))
        {
            var r = await s.ServiceProvider.GetRequiredService<ContaGatewayService>().ConectarAsync(chave, default);
            Assert.True(r.Conectada);
            Assert.Equal(chave[^4..], r.ChaveFinal);
            Assert.True(r.Sandbox);
        }
        return fx.Gateway.Webhooks.Single(w => w.Value.Chave == chave).Value.Webhook.AuthToken;
    }

    private async Task<(Guid TenantId, Guid FaturaId)> ClienteComFaturaAsync(Guid rev, string nome, TipoEntidade tipo = TipoEntidade.Privada)
    {
        Guid tenant;
        await using (var s = await fx.EscopoCanalAsync(rev, Roles.AdminRevendedor))
        {
            var r = await s.ServiceProvider.GetRequiredService<ClienteService>().CadastrarAsync(new NovoClienteRequest(
                nome + " LTDA", Docs.Cnpj(), null, $"rh.{Guid.NewGuid():N}@cliente.local", "RH", null, 350m, 10, 0m, null, 10, null, "Rua A, 1", false,
                CanalDonoId: rev, TipoEntidade: tipo), default);
            tenant = r.TenantId;
        }
        await using (var s = fx.Escopo(sistema: true))
        {
            var fat = s.ServiceProvider.GetRequiredService<FaturamentoService>();
            await fat.GerarCompetenciaAsync(new DateOnly(fat.Hoje.Year, fat.Hoje.Month, 1), default);
            var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
            return (tenant, await db.Faturas.Where(f => f.TenantId == tenant).Select(f => f.Id).SingleAsync());
        }
    }

    private async Task<Fatura> FaturaClienteAsync(Guid id)
    {
        await using var s = fx.Escopo(sistema: true);
        return await s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>().Faturas.AsNoTracking().SingleAsync(f => f.Id == id);
    }

    [Fact]
    public async Task Revendedor_Premium_conecta_conta_propria_chave_cifrada_e_cobranca_roteada_pela_conta_dele()
    {
        var revA = await RevendedorAsync("Asaas Rev A");
        var revB = await RevendedorAsync("Asaas Rev B");

        // Sem Premium: não conecta.
        await using (var s = await fx.EscopoCanalAsync(revA, Roles.AdminRevendedor))
            await Assert.ThrowsAsync<RegraNegocioException>(() => s.ServiceProvider.GetRequiredService<ContaGatewayService>().ConectarAsync(ChaveRevA, default));
        // Suporte do revendedor não conecta.
        await using (var s = await fx.EscopoCanalAsync(revA, Roles.SuporteRevendedor))
            await Assert.ThrowsAsync<AcessoNegadoException>(() => s.ServiceProvider.GetRequiredService<ContaGatewayService>().ConectarAsync(ChaveRevA, default));

        await AtivarPremiumAsync(revA);
        await AtivarPremiumAsync(revB);
        var tokenA = await ConectarAsync(revA, ChaveRevA);
        var tokenB = await ConectarAsync(revB, ChaveRevB);
        var wh = fx.Gateway.Webhooks.Single(w => w.Value.Chave == ChaveRevA).Value.Webhook;
        Assert.Equal("https://painel.teste.local" + AsaasOptions.CaminhoWebhook, wh.Url);
        Assert.Contains("PAYMENT_RECEIVED", wh.Eventos);
        Assert.InRange(tokenA.Length, 32, 255);

        // Chave guardada cifrada (propósito dedicado), só os 4 finais em claro; token só como hash.
        await using (var s = fx.Escopo(sistema: true))
        {
            var conta = await s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>().ContasGateway.SingleAsync(c => c.CanalId == revA && c.Ativa);
            Assert.DoesNotContain(ChaveRevA, conta.ChaveCifrada);
            Assert.Equal(ChaveRevA[^4..], conta.ChaveFinal);
            Assert.Equal(ChaveRevA, s.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector(CofreChavesGateway.Proposito).Unprotect(conta.ChaveCifrada));
            Assert.Throws<System.Security.Cryptography.CryptographicException>(() =>
                s.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("outro-proposito").Unprotect(conta.ChaveCifrada));
            Assert.NotEqual(tokenA, conta.WebhookTokenHash);
            Assert.Equal(CofreChavesGateway.Hash(tokenA), conta.WebhookTokenHash);
            var idConta = conta.Id.ToString();
            var log = await s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>().AuditLogs.SingleAsync(a => a.Acao == "gateway.conta_conectada" && a.EntidadeId == idConta);
            Assert.DoesNotContain(ChaveRevA, log.Dados);
            Assert.Equal(revA, log.CanalId);
        }

        // RLS: o revendedor B não enxerga a conta do A; eventos só no modo sistema.
        await using (var s = await fx.EscopoCanalAsync(revB, Roles.AdminRevendedor))
        {
            var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
            Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM personaliponto.contas_gateway WHERE canal_id = {0}", revA).SingleAsync());
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM personaliponto.contas_gateway WHERE canal_id = {0}", revB).SingleAsync());
            Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM personaliponto.eventos_gateway").SingleAsync());
        }

        // Fatura do cliente do revendedor A é cobrada com a chave do A.
        var (_, faturaId) = await ClienteComFaturaAsync(revA, "Cliente Asaas A");
        await ProcessarOutboxAsync();
        var fa = await FaturaClienteAsync(faturaId);
        Assert.Equal(StatusCobrancaGateway.Emitida, fa.GatewayStatus);
        Assert.NotNull(fa.GatewayContaId);
        Assert.Equal(ChaveRevA, fx.Gateway.Cobrancas[fa.GatewayCobrancaId!].Chave);

        // Evento com o token da plataforma ou do revendedor B não baixa a fatura do A.
        await WebhookAsync(BancoFixture.TokenOwner, "evt_o_" + faturaId, "PAYMENT_RECEIVED", fa.GatewayCobrancaId!, "2026-09-30 10:00:00", 350m);
        await WebhookAsync(tokenB, "evt_b_" + faturaId, "PAYMENT_RECEIVED", fa.GatewayCobrancaId!, "2026-09-30 10:00:00", 350m);
        Assert.Equal(StatusFatura.Aberta, (await FaturaClienteAsync(faturaId)).Status);
        // Com o token do A: baixa automática.
        Assert.Equal(ResultadoWebhook.Recebido, await WebhookAsync(tokenA, "evt_a_" + faturaId, "PAYMENT_RECEIVED", fa.GatewayCobrancaId!, "2026-09-30 10:00:00", 350m));
        fa = await FaturaClienteAsync(faturaId);
        Assert.Equal(StatusFatura.Paga, fa.Status);
        Assert.Equal(350m, fa.ValorPago);
        Assert.Equal(1, await AuditoriasAsync("fatura.paga_gateway", faturaId));

        // Desconectar remove o webhook na conta dele e o token deixa de valer.
        await using (var s = await fx.EscopoCanalAsync(revA, Roles.AdminRevendedor))
            await s.ServiceProvider.GetRequiredService<ContaGatewayService>().DesconectarAsync(default);
        Assert.DoesNotContain(fx.Gateway.Webhooks, w => w.Value.Chave == ChaveRevA);
        Assert.Equal(ResultadoWebhook.NaoAutorizado, await WebhookAsync(tokenA, "evt_a2_" + faturaId, "PAYMENT_REFUNDED", fa.GatewayCobrancaId!, "2026-09-30 11:00:00"));
        await using (var s = fx.Escopo(sistema: true))
        {
            var conta = await s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>().ContasGateway.SingleAsync(c => c.CanalId == revA);
            Assert.False(conta.Ativa);
            Assert.Equal("", conta.ChaveCifrada);
        }

        // Sem conta: nova fatura de cliente do A fica manual.
        var (_, outra) = await ClienteComFaturaAsync(revA, "Cliente Asaas A2");
        await ProcessarOutboxAsync();
        Assert.Equal(StatusCobrancaGateway.Nenhuma, (await FaturaClienteAsync(outra)).GatewayStatus);
    }

    [Fact]
    public async Task Cliente_publico_nunca_gera_cobranca_automatica()
    {
        var rev = await RevendedorAsync("Asaas Publico");
        await AtivarPremiumAsync(rev);
        await ConectarAsync(rev, "$aact_hmlg_revendedorP_000000000000000003");
        var antes = fx.Gateway.Cobrancas.Count;
        var (_, faturaId) = await ClienteComFaturaAsync(rev, "Prefeitura Asaas", TipoEntidade.Publica);
        await ProcessarOutboxAsync();
        var f = await FaturaClienteAsync(faturaId);
        Assert.Equal(StatusFatura.Aberta, f.Status);
        Assert.Equal(StatusCobrancaGateway.Nenhuma, f.GatewayStatus);
        Assert.Null(f.GatewayCobrancaId);
        Assert.DoesNotContain(fx.Gateway.Cobrancas, c => c.Value.Cobranca.ReferenciaExterna == $"cliente:{faturaId}");
        await using var s = fx.Escopo(sistema: true);
        var msgs = await s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>().Outbox.AsNoTracking().Where(m => m.Tipo == PedidoCobrancaGateway.TipoMensagem).ToListAsync();
        Assert.DoesNotContain(msgs, m => m.Payload.Contains(faturaId.ToString()));
    }

    [Fact]
    public async Task Chave_invalida_nao_conecta_e_nada_e_gravado()
    {
        var rev = await RevendedorAsync("Asaas Invalida");
        await AtivarPremiumAsync(rev);
        const string chave = "$aact_hmlg_invalida_0000000000000000000009";
        fx.Gateway.ChavesInvalidas[chave] = true;
        await using (var s = await fx.EscopoCanalAsync(rev, Roles.AdminRevendedor))
        {
            var ex = await Assert.ThrowsAsync<RegraNegocioException>(() => s.ServiceProvider.GetRequiredService<ContaGatewayService>().ConectarAsync(chave, default));
            Assert.DoesNotContain(chave, ex.Message);
        }
        await using var q = fx.Escopo(sistema: true);
        Assert.False(await q.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>().ContasGateway.AnyAsync(c => c.CanalId == rev));
    }
}
