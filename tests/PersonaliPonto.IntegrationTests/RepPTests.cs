using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PersonaliPonto.Core.RepP.Afd;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Formatacao;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Infrastructure.Persistence;
using PersonaliPonto.Infrastructure.Seguranca;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.IntegrationTests;

[Collection("banco")]
public sealed class RepPTests(BancoFixture fx)
{
    private static RegistrarMarcacaoRequest Online() => new(Guid.NewGuid(), false, null, null, null, null, "teste");

    private async Task<MarcacaoRegistradaDto> MarcarAsync(BancoFixture.Cenario c, RegistrarMarcacaoRequest? req = null, Coletor coletor = Coletor.Browser)
    {
        await using var s = fx.Escopo(c.TenantId);
        var svc = s.ServiceProvider.GetRequiredService<MarcacaoService>();
        var f = await svc.IdentificarPorPinAsync(c.EstabelecimentoId, "001", c.Pin, default);
        return await svc.RegistrarAsync(f, req ?? Online(), coletor, null, "127.0.0.1", default);
    }

    [Fact]
    public async Task Marcacao_gera_NSR_sequencial_e_hash_encadeado_sem_lacunas_sob_concorrencia()
    {
        var c = await fx.CriarCenarioAsync("Concorrencia", Docs.Cnpj(), Docs.Cpf());
        var tarefas = Enumerable.Range(0, 25).Select(_ => MarcarAsync(c)).ToList();
        var resultados = await Task.WhenAll(tarefas);

        await using var s = fx.Escopo(c.TenantId);
        var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
        var nsrs = await db.RegistrosRep.Where(r => r.EstabelecimentoId == c.EstabelecimentoId).OrderBy(r => r.Nsr).Select(r => r.Nsr).ToListAsync();
        // Tipos 2 (empresa) e 5 (empregado) + 25 marcações, NSR de 1..N sem lacunas.
        Assert.Equal(Enumerable.Range(1, nsrs.Count).Select(i => (long)i), nsrs);
        Assert.Equal(25, resultados.Select(r => r.Nsr).Distinct().Count());

        var afd = await s.ServiceProvider.GetRequiredService<ArquivosFiscaisService>()
            .GerarAfdAsync(c.EstabelecimentoId, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), default);
        Assert.Empty(AfdValidator.Validar(afd.Conteudo));
        Assert.True(AssinaturaDigital.VerificarCades(afd.Conteudo, afd.AssinaturaP7s));
        var linhas = AfdCampos.Latin1.GetString(afd.Conteudo).Split("\r\n");
        Assert.Equal(25, linhas.Count(l => l.Length == 137 && l[9] == '7'));
        Assert.Contains(linhas, l => l.Length == 331 && l[9] == '2');
        Assert.Contains(linhas, l => l.Length == 118 && l[9] == '5');
    }

    [Fact]
    public async Task Registro_original_e_fisicamente_imutavel_mesmo_com_acesso_de_sistema()
    {
        var c = await fx.CriarCenarioAsync("Imutavel", Docs.Cnpj(), Docs.Cpf());
        var m = await MarcarAsync(c);

        await using var s = fx.Escopo(sistema: true);
        var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();

        var reg = await db.RegistrosRep.FirstAsync(r => r.Id == m.Id);
        reg.DataHoraMarcacao = reg.DataHoraMarcacao!.Value.AddHours(-1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var up = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync("UPDATE personaliponto.registros_rep SET data_hora_marcacao = now() WHERE id = {0}", m.Id));
        Assert.Contains("imutável", up.MessageText);
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM personaliponto.registros_rep WHERE id = {0}", m.Id));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("TRUNCATE personaliponto.registros_rep CASCADE"));
        Assert.True(await db.RegistrosRep.AnyAsync(r => r.Id == m.Id));
    }

    [Fact]
    public async Task Mesmo_ClientId_nao_duplica_marcacao()
    {
        var c = await fx.CriarCenarioAsync("Idempotente", Docs.Cnpj(), Docs.Cpf());
        var req = Online();
        var r1 = await MarcarAsync(c, req);
        var r2 = await MarcarAsync(c, req);
        var paralelas = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => MarcarAsync(c, req)));
        Assert.False(r1.Duplicada);
        Assert.True(r2.Duplicada);
        Assert.All(paralelas.Append(r2), r => Assert.Equal(r1.Id, r.Id));

        await using var s = fx.Escopo(c.TenantId);
        var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
        Assert.Equal(1, await db.RegistrosRep.CountAsync(r => r.ClientId == req.ClientId));
    }

    [Fact]
    public async Task Offline_usa_ancora_do_servidor_e_rejeita_horario_forjado()
    {
        var c = await fx.CriarCenarioAsync("Offline", Docs.Cnpj(), Docs.Cpf());
        AncoraHoraDto ancora;
        await using (var s = fx.Escopo(c.TenantId))
            ancora = await s.ServiceProvider.GetRequiredService<MarcacaoService>().EmitirAncoraAsync(c.TenantId, c.EstabelecimentoId, null, null, default);

        await Task.Delay(1200);
        var ok = await MarcarAsync(c, new RegistrarMarcacaoRequest(Guid.NewGuid(), true, ancora.HoraServidor.AddSeconds(1), ancora.AncoraId, null, null, "x"), Coletor.AplicativoMobile);
        Assert.True(ok.Offline);
        Assert.Equal(ancora.HoraServidor.AddSeconds(1).ToUnixTimeMilliseconds(), ok.DataHoraMarcacao.ToUnixTimeMilliseconds());
        Assert.True(ok.DataHoraGravacao >= ok.DataHoraMarcacao);

        await Assert.ThrowsAsync<ConflitoMarcacaoException>(() =>
            MarcarAsync(c, new RegistrarMarcacaoRequest(Guid.NewGuid(), true, ancora.HoraServidor.AddHours(-2), ancora.AncoraId, null, null, "x")));
        await Assert.ThrowsAsync<ConflitoMarcacaoException>(() =>
            MarcarAsync(c, new RegistrarMarcacaoRequest(Guid.NewGuid(), true, DateTimeOffset.UtcNow.AddHours(3), ancora.AncoraId, null, null, "x")));
        await Assert.ThrowsAsync<ConflitoMarcacaoException>(() =>
            MarcarAsync(c, new RegistrarMarcacaoRequest(Guid.NewGuid(), true, DateTimeOffset.UtcNow, Guid.NewGuid(), null, null, "x")));
    }

    [Fact]
    public async Task Pin_errado_bloqueia_apos_tentativas()
    {
        var c = await fx.CriarCenarioAsync("Pin", Docs.Cnpj(), Docs.Cpf());
        await using var s = fx.Escopo(c.TenantId);
        var svc = s.ServiceProvider.GetRequiredService<MarcacaoService>();
        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAsync<AcessoNegadoException>(() => svc.IdentificarPorPinAsync(c.EstabelecimentoId, "001", "9999", default));
        var ex = await Assert.ThrowsAsync<AcessoNegadoException>(() => svc.IdentificarPorPinAsync(c.EstabelecimentoId, "001", c.Pin, default));
        Assert.True(ex.Bloqueado);

        var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
        var hash = await db.Funcionarios.Where(f => f.Id == c.FuncionarioId).Select(f => f.PinHash).FirstAsync();
        Assert.DoesNotContain(c.Pin, hash!);
    }

    [Fact]
    public async Task Tratamento_preserva_original_e_aparece_no_espelho_e_no_AEJ()
    {
        var cnpj = Docs.Cnpj();
        var c = await fx.CriarCenarioAsync("Tratamento", cnpj, Docs.Cpf());
        var m = await MarcarAsync(c);
        await using var s = fx.Escopo(c.TenantId);
        var trat = s.ServiceProvider.GetRequiredService<TratamentoService>();
        await trat.DesconsiderarAsync(m.Id, "Marcação em duplicidade", default);
        var incl = await trat.IncluirAsync(c.FuncionarioId, DateTimeOffset.UtcNow.AddMinutes(-30), "Esqueceu de marcar a entrada", null, default);

        var db = s.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
        var original = await db.RegistrosRep.AsNoTracking().FirstAsync(r => r.Id == m.Id);
        Assert.Equal(m.Hash, original.Hash);
        Assert.Equal(m.DataHoraMarcacao, original.DataHoraMarcacao);

        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var espelho = await s.ServiceProvider.GetRequiredService<EspelhoService>().GerarAsync(c.FuncionarioId, hoje.AddDays(-1), hoje.AddDays(1), default);
        var marcas = espelho.Dias.SelectMany(d => d.Marcacoes).ToList();
        Assert.Contains(marcas, x => x.Desconsiderada && x.Nsr == m.Nsr);
        Assert.Contains(marcas, x => x.Fonte == "Incluída");

        var aej = await s.ServiceProvider.GetRequiredService<ArquivosFiscaisService>().GerarAejAsync(c.EstabelecimentoId, hoje.AddDays(-1), hoje.AddDays(1), default);
        var texto = AfdCampos.Latin1.GetString(aej.Conteudo);
        Assert.Contains("|D|0|O||Marcação em duplicidade", texto);
        Assert.Contains("|I|", texto);
        Assert.StartsWith($"01|1|{cnpj}|", texto);
        Assert.True(AssinaturaDigital.VerificarCades(aej.Conteudo, aej.AssinaturaP7s));

        // Revogação mantém histórico.
        await trat.RevogarAsync(incl.Id, "Inclusão lançada por engano", default);
        Assert.Equal(3, await db.Tratamentos.CountAsync(t => t.FuncionarioId == c.FuncionarioId));
    }

    [Fact]
    public async Task Comprovante_pdf_assinado_com_dados_obrigatorios()
    {
        var cnpj = Docs.Cnpj();
        var c = await fx.CriarCenarioAsync("Comprovante", cnpj, Docs.Cpf());
        var m = await MarcarAsync(c);
        await using var s = fx.Escopo(c.TenantId);
        var comp = s.ServiceProvider.GetRequiredService<ComprovanteService>();
        var dados = await comp.DadosAsync(m.Id, c.FuncionarioId, default);
        Assert.Equal(m.Nsr, dados.Nsr);
        Assert.Equal(m.Hash, dados.Hash);
        Assert.Equal(Core.RepP.Formatacao.Documentos.FormatarCnpj(cnpj), dados.EmpregadorIdentificador);
        var (nome, pdf) = await comp.PdfAsync(m.Id, c.FuncionarioId, default);
        Assert.StartsWith("comprovante-nsr-", nome);
        var texto = System.Text.Encoding.Latin1.GetString(pdf);
        Assert.StartsWith("%PDF", texto);
        Assert.Contains("/ByteRange", texto);
        Assert.Contains("adbe.pkcs7.detached", texto);
        // Outro funcionário não acessa o comprovante.
        await Assert.ThrowsAsync<NaoEncontradoException>(() => comp.DadosAsync(m.Id, Guid.NewGuid(), default));
    }
}
