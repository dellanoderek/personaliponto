using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Infrastructure.Persistence;
using PersonaliPonto.Infrastructure.Seguranca;
using PersonaliPonto.Modules.RH.Domain;
using PersonaliPonto.Modules.RH.Services;
using PersonaliPonto.Modules.SaaS.Domain;
using PersonaliPonto.Modules.SaaS.Services;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.IntegrationTests;

[Collection("banco")]
public sealed class RhSaasTests(BancoFixture fx)
{
    [Fact]
    public async Task Solicitacao_aprovada_gera_tratamento_e_recusa_nao()
    {
        var c = await fx.CriarCenarioAsync("Solicita", Docs.Cnpj(), Docs.Cpf());
        await using var s = fx.Escopo(c.TenantId);
        var svc = s.ServiceProvider.GetRequiredService<SolicitacaoService>();
        var ontem = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var s1 = await svc.SolicitarAsync(c.FuncionarioId, TipoSolicitacao.Inclusao, ontem, new TimeOnly(8, 0), null, "Esqueci de marcar a entrada", default);
        var s2 = await svc.SolicitarAsync(c.FuncionarioId, TipoSolicitacao.Inclusao, ontem, new TimeOnly(12, 0), null, "Esqueci de marcar a saída", default);
        await svc.AprovarAsync(s1.Id, "Ok", default);
        await svc.RecusarAsync(s2.Id, "Sem comprovação", default);
        await Assert.ThrowsAsync<RegraNegocioException>(() => svc.AprovarAsync(s1.Id, null, default));

        var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
        var t = await db.Tratamentos.SingleAsync(x => x.SolicitacaoId == s1.Id);
        Assert.Equal(TipoTratamento.Inclusao, t.Tipo);
        Assert.Equal(2, await db.AuditLogs.CountAsync(a => a.Acao == "solicitacao.criada" && a.TenantId == c.TenantId));
    }

    [Fact]
    public async Task Fechamento_lanca_banco_de_horas_bloqueia_periodo_e_reabertura_estorna()
    {
        var c = await fx.CriarCenarioAsync("Fechamento", Docs.Cnpj(), Docs.Cpf());
        await using var s = fx.Escopo(c.TenantId);
        var trat = s.ServiceProvider.GetRequiredService<TratamentoService>();
        var fech = s.ServiceProvider.GetRequiredService<FechamentoService>();
        var bh = s.ServiceProvider.GetRequiredService<BancoHorasService>();
        var mes = DateTime.UtcNow.AddMonths(-1);
        var fuso = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        // Um dia útil com 2h extras: 08:00-12:00 / 13:00-19:48.
        var dia = new DateOnly(mes.Year, mes.Month, 1);
        while (dia.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) dia = dia.AddDays(1);
        foreach (var (h, m) in new[] { (8, 0), (12, 0), (13, 0), (19, 48) })
        {
            var local = dia.ToDateTime(new TimeOnly(h, m));
            await trat.IncluirAsync(c.FuncionarioId, new DateTimeOffset(local, fuso.GetUtcOffset(local)), "Registro manual em contingência", null, default);
        }

        var f = await fech.FecharAsync(c.FuncionarioId, mes.Year, mes.Month, default);
        Assert.Equal(120, f.MinutosExtras);
        Assert.True(f.MinutosFaltas > 0); // demais dias úteis sem marcação
        var saldo = await bh.SaldoAsync(c.FuncionarioId, null, default);
        Assert.Equal(f.MinutosExtras - f.MinutosFaltas - f.MinutosAtrasos, saldo);

        await Assert.ThrowsAsync<RegraNegocioException>(() => s.ServiceProvider.GetRequiredService<SolicitacaoService>()
            .SolicitarAsync(c.FuncionarioId, TipoSolicitacao.Inclusao, dia, new TimeOnly(7, 0), null, "Tentativa após fechamento", default));

        await fech.ReabrirAsync(f.Id, "Correção de lançamento", default);
        Assert.Equal(0, await bh.SaldoAsync(c.FuncionarioId, null, default));
    }

    [Fact]
    public async Task Atestado_aprovado_abona_a_falta()
    {
        var c = await fx.CriarCenarioAsync("Atestado", Docs.Cnpj(), Docs.Cpf());
        await using var s = fx.Escopo(c.TenantId);
        var png = CriarPng();
        var at = s.ServiceProvider.GetRequiredService<AtestadoService>();
        var dia = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7));
        while (dia.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) dia = dia.AddDays(-1);
        var a = await at.EnviarAsync(c.FuncionarioId, TipoAtestado.AtestadoMedico, dia, dia, null, "Consulta", "atestado.png", png, default);
        var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
        var arq = await db.Arquivos.SingleAsync(x => x.Id == a.ArquivoId);
        Assert.Equal("image/webp", arq.ContentType);

        var esp = s.ServiceProvider.GetRequiredService<EspelhoService>();
        Assert.True((await esp.GerarAsync(c.FuncionarioId, dia, dia, default)).Dias[0].MinutosFalta > 0);
        await at.AnalisarAsync(a.Id, true, "Válido", default);
        var d = (await esp.GerarAsync(c.FuncionarioId, dia, dia, default)).Dias[0];
        Assert.Equal(0, d.MinutosFalta);
        Assert.Equal("Atestado médico", d.Ocorrencia);
    }

    [Fact]
    public async Task Geofence_em_modo_bloqueio_impede_marcacao_distante()
    {
        var c = await fx.CriarCenarioAsync("Cerca", Docs.Cnpj(), Docs.Cpf());
        await using (var s0 = fx.Escopo(c.TenantId))
        {
            var db0 = s0.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
            var e = await db0.Estabelecimentos.FirstAsync(x => x.Id == c.EstabelecimentoId);
            e.Latitude = -7.2172; e.Longitude = -35.8811; e.RaioCercaMetros = 150; e.ModoCerca = ModoCerca.Bloquear;
            await db0.SaveChangesAsync();
        }
        await using var s = fx.Escopo(c.TenantId);
        var svc = s.ServiceProvider.GetRequiredService<MarcacaoService>();
        var f = await s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>().Funcionarios.FirstAsync(x => x.Id == c.FuncionarioId);
        var longe = new RegistrarMarcacaoRequest(Guid.NewGuid(), false, null, null, -7.1195, -34.8450, "cel");
        var ex = await Assert.ThrowsAsync<RegraNegocioException>(() => svc.RegistrarAsync(f, longe, Coletor.AplicativoMobile, null, null, default));
        Assert.Contains("local de trabalho", ex.Message);
        var perto = new RegistrarMarcacaoRequest(Guid.NewGuid(), false, null, null, -7.2175, -35.8812, "cel");
        var ok = await svc.RegistrarAsync(f, perto, Coletor.AplicativoMobile, null, null, default);
        Assert.False(ok.Duplicada);
    }

    [Fact]
    public async Task Cliente_inadimplente_e_suspenso_automaticamente_mantendo_dados_e_liberado_ao_pagar()
    {
        Guid tenantId;
        await using (var s = fx.Escopo(sistema: true, papel: Roles.SuperAdmin))
        {
            var cli = s.ServiceProvider.GetRequiredService<ClienteService>();
            var criado = await cli.CadastrarAsync(new NovoClienteRequest("CLIENTE ATRASADO LTDA", Docs.Cnpj(), "83 99999-0000",
                "admin@atrasado.com.br", "Admin", "52998224725", 139.9m, 16, 30.3m, "EZPOINT", 10, null, "Rua A, 1", false), default);
            tenantId = criado.TenantId;
            Assert.NotNull(criado.SenhaTemporaria);

            var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
            db.Faturas.Add(new Fatura
            {
                TenantId = tenantId, Competencia = new DateOnly(2026, 7, 1), Valor = 139.9m, Vencimento = new DateOnly(2026, 7, 10),
                Status = StatusFatura.Aberta, CriadaEm = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            var fat = s.ServiceProvider.GetRequiredService<FaturamentoService>();
            await fat.AvaliarTenantAsync(tenantId, default);
            Assert.Equal(TenantStatus.Suspenso, (await db.Tenants.FirstAsync(t => t.Id == tenantId)).Status);
        }

        // Suspenso: uso bloqueado, consulta liberada.
        await using (var s = fx.Escopo(tenantId))
        {
            var pol = s.ServiceProvider.GetRequiredService<TenantUsoPolicy>();
            Assert.False((await pol.PodeUsarAsync(tenantId, default)).Pode);
            var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
            Assert.NotEmpty(await db.Estabelecimentos.ToListAsync());
            Assert.NotEmpty(await db.RegistrosRep.ToListAsync());
        }

        await using (var s = fx.Escopo(sistema: true, papel: Roles.SuperAdmin))
        {
            var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
            var f = await db.Faturas.FirstAsync(x => x.TenantId == tenantId);
            await s.ServiceProvider.GetRequiredService<FaturamentoService>().RegistrarPagamentoAsync(f.Id, new DateOnly(2026, 9, 20), 139.9m, "PIX", null, default);
            Assert.Equal(TenantStatus.Ativo, (await db.Tenants.FirstAsync(t => t.Id == tenantId)).Status);
        }
    }

    [Fact]
    public async Task Cliente_novo_nao_nasce_com_fatura_vencida_e_teste_nao_e_faturado()
    {
        await using var s = fx.Escopo(sistema: true, papel: Roles.SuperAdmin);
        var cli = s.ServiceProvider.GetRequiredService<ClienteService>();
        var fat = s.ServiceProvider.GetRequiredService<FaturamentoService>();
        var ativo = await cli.CadastrarAsync(new NovoClienteRequest("NOVO ATIVO LTDA", Docs.Cnpj(), null, "", "", null, 99m, 5, 20m, null, 1, null, "Rua D", false), default);
        var teste = await cli.CadastrarAsync(new NovoClienteRequest("NOVO TESTE LTDA", Docs.Cnpj(), null, "", "", null, 99m, 5, 20m, null, 1, null, "Rua E", true), default);
        await fat.GerarCompetenciaAsync(new DateOnly(fat.Hoje.Year, fat.Hoje.Month, 1), default);
        await fat.ProcessarInadimplenciaAsync(default);

        var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
        var f = await db.Faturas.SingleAsync(x => x.TenantId == ativo.TenantId);
        Assert.True(f.Vencimento >= fat.Hoje.AddDays(7));
        Assert.Equal(TenantStatus.Ativo, (await db.Tenants.FirstAsync(t => t.Id == ativo.TenantId)).Status);
        Assert.False(await db.Faturas.AnyAsync(x => x.TenantId == teste.TenantId));
    }

    [Fact]
    public async Task Suspensao_manual_nao_e_desfeita_pela_regua()
    {
        await using var s = fx.Escopo(sistema: true, papel: Roles.SuperAdmin);
        var cli = s.ServiceProvider.GetRequiredService<ClienteService>();
        var c = await cli.CadastrarAsync(new NovoClienteRequest("SUSPENSO MANUAL LTDA", Docs.Cnpj(), null, "", "", null, 79.9m, 5, 22m, null, 10, null, "Rua B", false), default);
        await cli.SuspenderAsync(c.TenantId, "Solicitação do cliente", default);
        await s.ServiceProvider.GetRequiredService<FaturamentoService>().AvaliarTenantAsync(c.TenantId, default);
        var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
        Assert.Equal(TenantStatus.Suspenso, (await db.Tenants.FirstAsync(t => t.Id == c.TenantId)).Status);
    }

    [Fact]
    public async Task Importacao_da_planilha_de_clientes()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Plan1");
        ws.Cell(2, 3).Value = "TELEFONE"; ws.Cell(2, 4).Value = "RASÃO SOCIAL"; ws.Cell(2, 5).Value = "CNPJ";
        ws.Cell(2, 6).Value = "VALOR"; ws.Cell(2, 7).Value = "FUNCIONÁRIOS";
        for (var m = 0; m < 12; m++) ws.Cell(2, 8 + m).Value = new DateTime(2026, m + 1, 1);
        ws.Cell(2, 21).Value = "CUSTO";
        void Linha(int r, string razao, string cnpj, double valor, int func, string[] meses, double custo, string forn, bool cancelado = false)
        {
            ws.Cell(r, 3).Value = "83 9 0000-0000"; ws.Cell(r, 4).Value = razao; ws.Cell(r, 5).Value = cnpj;
            ws.Cell(r, 6).Value = valor; ws.Cell(r, 7).Value = func; ws.Cell(r, 21).Value = custo; ws.Cell(r, 24).Value = forn;
            for (var i = 0; i < meses.Length; i++) if (meses[i] != "") ws.Cell(r, 8 + i).Value = meses[i];
            if (cancelado) ws.Cell(r, 22).Value = "cancelado";
        }
        Linha(3, "IMPORTADA UM LTDA", Docs.Cnpj(), 250, 200, ["PAGO", "PAGO", "PAGO"], 60.6, "EZPOINT");
        Linha(4, "IMPORTADA DOIS LTDA", "12.ABC.345/01DE-35", 180, 7, ["XXXXXX", "XXXXX", "PAGO"], 22, "COTIPONTO");
        Linha(5, "CANCELADA LTDA", Docs.Cnpj(), 79.9, 4, ["PAGO", "PAGO"], 22, "EZPOINT", cancelado: true);
        Linha(6, "CNPJ RUIM", "00.000.000/0000-01", 10, 1, ["PAGO"], 1, "EZPOINT");
        using var ms = new MemoryStream();
        wb.SaveAs(ms);

        var linhas = ImportacaoPlanilhaService.Ler(new MemoryStream(ms.ToArray()), new DateOnly(2026, 9, 15));
        Assert.Equal(4, linhas.Count);
        var um = linhas[0];
        Assert.Equal("EZPOINT", um.Fornecedor);
        Assert.Equal(SituacaoMesImportado.Pago, um.Meses[new DateOnly(2026, 3, 1)]);
        Assert.Equal(SituacaoMesImportado.EmAberto, um.Meses[new DateOnly(2026, 9, 1)]);
        Assert.False(um.Meses.ContainsKey(new DateOnly(2026, 10, 1)));
        Assert.Equal(SituacaoMesImportado.NaoCobrado, linhas[1].Meses[new DateOnly(2026, 1, 1)]);
        Assert.True(linhas[2].Cancelado);
        Assert.False(linhas[2].Meses.ContainsKey(new DateOnly(2026, 3, 1)));
        Assert.False(linhas[3].CnpjValido);

        await using var s = fx.Escopo(sistema: true, papel: Roles.SuperAdmin);
        var imp = s.ServiceProvider.GetRequiredService<ImportacaoPlanilhaService>();
        var r = await imp.ImportarAsync(linhas, default);
        Assert.Single(r.Erros);
        var painel = s.ServiceProvider.GetRequiredService<PainelPlataformaService>();
        var grade = await painel.GradeAsync(2026, "IMPORTADA", null, default);
        Assert.Contains(grade, g => g.RazaoSocial == "IMPORTADA UM LTDA" && g.Meses[new DateOnly(2026, 1, 1)].Estado == CelulaFatura.Paga);
        var ind = await painel.IndicadoresAsync(default);
        Assert.True(ind.ReceitaRecorrente > 0);
    }

    [Fact]
    public async Task Login_com_bloqueio_refresh_rotativo_e_deteccao_de_reuso()
    {
        await using var s = fx.Escopo(sistema: true, papel: Roles.SuperAdmin);
        var cli = s.ServiceProvider.GetRequiredService<ClienteService>();
        var criado = await cli.CadastrarAsync(new NovoClienteRequest("LOGIN TESTE LTDA", Docs.Cnpj(), null, "rh@login.com.br", "RH", null, 100, 10, 20, null, 10, null, "Rua C", false), default);
        var auth = s.ServiceProvider.GetRequiredService<AuthService>();

        var u = await auth.ValidarCredenciaisAsync("rh@login.com.br", criado.SenhaTemporaria!, null, default);
        Assert.Contains(u.Claims, x => x.Type == PersonaliPontoClaims.TenantId && x.Value == criado.TenantId.ToString());
        Assert.Contains(u.Claims, x => x.Type == "trocar_senha");
        var t1 = await auth.EmitirTokensAsync(u.Usuario, default);
        var t2 = await auth.RenovarAsync(t1.RefreshToken, default);
        Assert.NotEqual(t1.RefreshToken, t2.RefreshToken);
        await Assert.ThrowsAsync<AcessoNegadoException>(() => auth.RenovarAsync(t1.RefreshToken, default)); // reuso
        await Assert.ThrowsAsync<AcessoNegadoException>(() => auth.RenovarAsync(t2.RefreshToken, default)); // cadeia revogada

        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAsync<AcessoNegadoException>(() => auth.ValidarCredenciaisAsync("rh@login.com.br", "errada", null, default));
        var bloqueado = await Assert.ThrowsAsync<AcessoNegadoException>(() => auth.ValidarCredenciaisAsync("rh@login.com.br", criado.SenhaTemporaria!, null, default));
        Assert.True(bloqueado.Bloqueado);
    }

    [Fact]
    public void Totp_confere_com_vetor_da_RFC_6238()
    {
        // RFC 6238, SHA-1, T = 59s => 94287082 (8 dígitos) => 287082 (6 dígitos).
        var segredo = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ"; // "12345678901234567890" em base32
        Assert.Equal("287082", Totp.Codigo(segredo, 59 / 30));
    }

    private static byte[] CriarPng()
    {
        using var bmp = new SkiaSharp.SKBitmap(2400, 1800);
        using (var c = new SkiaSharp.SKCanvas(bmp)) c.Clear(SkiaSharp.SKColors.White);
        using var img = SkiaSharp.SKImage.FromBitmap(bmp);
        return img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100).ToArray();
    }
}
