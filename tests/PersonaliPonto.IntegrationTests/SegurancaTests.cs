using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Infrastructure.Persistence;
using PersonaliPonto.Modules.RH.Domain;
using PersonaliPonto.Modules.RH.Services;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.IntegrationTests;

[Collection("banco")]
public sealed class SegurancaTests(BancoFixture fx)
{
    private static RegistrarMarcacaoRequest Req(string? aparelho, double? lat = null, double? lng = null) =>
        new(Guid.NewGuid(), false, null, null, lat, lng, aparelho);

    [Fact]
    public async Task Vinculo_de_aparelho_bloqueia_aparelho_nao_autorizado_ate_o_RH_liberar()
    {
        var c = await fx.CriarCenarioAsync("Aparelho", Docs.Cnpj(), Docs.Cpf());
        await using var s = fx.Escopo(c.TenantId);
        var sp = s.ServiceProvider;
        await sp.GetRequiredService<ConfiguracaoSegurancaService>().SalvarAsync(true, false, 90, false, default);
        var db = sp.GetRequiredService<PersonaliPontoDbContext>();
        var f = await db.Funcionarios.FirstAsync(x => x.Id == c.FuncionarioId);
        var svc = sp.GetRequiredService<MarcacaoService>();

        var ex = await Assert.ThrowsAsync<RegraNegocioException>(() => svc.RegistrarAsync(f, Req("cel-A"), Coletor.AplicativoMobile, null, null, default));
        Assert.Contains("autorização do RH", ex.Message);

        var d = await db.Dispositivos.SingleAsync(x => x.FuncionarioId == f.Id);
        Assert.Equal(StatusDispositivo.Pendente, d.Status);
        await sp.GetRequiredService<DispositivoService>().AlterarAsync(d.Id, StatusDispositivo.Autorizado, default);
        var ok = await svc.RegistrarAsync(f, Req("cel-A"), Coletor.AplicativoMobile, null, null, default);
        Assert.False(ok.Duplicada);

        // O terminal do estabelecimento não é afetado pelo vínculo de aparelho do app.
        await svc.RegistrarAsync(f, Req(null), Coletor.Browser, null, null, default);
    }

    [Fact]
    public async Task Antifraude_detecta_deslocamento_impossivel_e_aparelho_compartilhado()
    {
        var c = await fx.CriarCenarioAsync("Fraude", Docs.Cnpj(), Docs.Cpf());
        await using var s = fx.Escopo(c.TenantId);
        var sp = s.ServiceProvider;
        var db = sp.GetRequiredService<PersonaliPontoDbContext>();
        var cad = sp.GetRequiredService<CadastroService>();
        var outro = await cad.SalvarFuncionarioAsync(new Funcionario
        {
            EstabelecimentoId = c.EstabelecimentoId, Nome = "Colega", Cpf = Docs.Cpf(), Matricula = "002", DataAdmissao = new DateOnly(2026, 1, 1), Ativo = true, PermiteMarcacaoApp = true
        }, default);
        var f = await db.Funcionarios.FirstAsync(x => x.Id == c.FuncionarioId);
        var svc = sp.GetRequiredService<MarcacaoService>();

        await svc.RegistrarAsync(f, Req("cel-X", -7.2306, -35.8811), Coletor.AplicativoMobile, null, null, default); // Campina Grande
        await svc.RegistrarAsync(f, Req("cel-X", -8.0476, -34.8770), Coletor.AplicativoMobile, null, null, default); // Recife, segundos depois
        await svc.RegistrarAsync(await db.Funcionarios.FirstAsync(x => x.Id == outro.Id), Req("cel-X"), Coletor.AplicativoMobile, null, null, default);

        var alertas = await sp.GetRequiredService<AnaliseFraudeService>().AnalisarAsync(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1), default);
        Assert.Contains(alertas, a => a.Tipo == TipoAlerta.DeslocamentoImpossivel && a.FuncionarioId == f.Id);
        Assert.Contains(alertas, a => a.Tipo == TipoAlerta.AparelhoCompartilhado && a.FuncionarioId == outro.Id);
    }

    [Fact]
    public async Task Foto_exige_avaliacao_LGPD_e_e_excluida_ao_fim_da_retencao()
    {
        var c = await fx.CriarCenarioAsync("Foto", Docs.Cnpj(), Docs.Cpf());
        await using var s = fx.Escopo(c.TenantId);
        var sp = s.ServiceProvider;
        var cfg = sp.GetRequiredService<ConfiguracaoSegurancaService>();
        await Assert.ThrowsAsync<RegraNegocioException>(() => cfg.SalvarAsync(false, true, 30, false, default));
        await cfg.SalvarAsync(false, true, 1, true, default);

        var db = sp.GetRequiredService<PersonaliPontoDbContext>();
        var f = await db.Funcionarios.FirstAsync(x => x.Id == c.FuncionarioId);
        var m = await sp.GetRequiredService<MarcacaoService>().RegistrarAsync(f, Req("cel"), Coletor.AplicativoMobile, null, null, default);

        using var bmp = new SkiaSharp.SKBitmap(800, 600);
        using var img = SkiaSharp.SKImage.FromBitmap(bmp);
        var fotos = sp.GetRequiredService<FotoMarcacaoService>();
        await fotos.AnexarAsync(m.Id, f.Id, img.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 90).ToArray(), default);
        var foto = await db.FotosMarcacao.SingleAsync(x => x.RegistroRepId == m.Id);
        var arq = await db.Arquivos.SingleAsync(a => a.Id == foto.ArquivoId);
        Assert.Equal("image/webp", arq.ContentType);
        Assert.NotNull(await sp.GetRequiredService<IFileStorage>().ObterAsync(arq.Bucket, arq.Caminho, default));

        // Simula o fim da retenção e roda o expurgo.
        await db.Database.ExecuteSqlRawAsync("UPDATE personaliponto.fotos_marcacao SET expira_em = now() - interval '1 day' WHERE id = {0}", foto.Id);
        db.ChangeTracker.Clear();
        Assert.Equal(1, await fotos.ExpurgarAsync(default));
        Assert.Null(await sp.GetRequiredService<IFileStorage>().ObterAsync(arq.Bucket, arq.Caminho, default));
        Assert.NotNull((await db.FotosMarcacao.AsNoTracking().SingleAsync(x => x.Id == foto.Id)).ExcluidaEm);
        // A marcação original continua intacta.
        Assert.True(await db.RegistrosRep.AnyAsync(r => r.Id == m.Id));
    }
}
