using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Aej;
using PersonaliPonto.Core.RepP.Afd;
using PersonaliPonto.Core.RepP.Domain;

namespace PersonaliPonto.Core.RepP.Services;

public sealed record ArquivoFiscal(string Nome, byte[] Conteudo, byte[] AssinaturaP7s)
{
    /// <summary>Pacote .zip com o arquivo e sua assinatura destacada "&lt;nome&gt;.p7s" (FAQ MTE nº 28/29).</summary>
    public byte[] Zip()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            using (var s = zip.CreateEntry(Nome, CompressionLevel.Optimal).Open()) s.Write(Conteudo);
            using (var s = zip.CreateEntry(Nome + ".p7s", CompressionLevel.Optimal).Open()) s.Write(AssinaturaP7s);
        }
        return ms.ToArray();
    }
}

/// <summary>Geração de AFD (REP-P) e AEJ (PTRP), assinados em CAdES destacado.</summary>
public sealed class ArquivosFiscaisService(
    IRepPDbContext db,
    EspelhoService espelho,
    IAssinaturaDigital assinatura,
    IClock clock,
    AuditService audit,
    IOptions<RepPOptions> options)
{
    private readonly RepPOptions _opt = options.Value;

    public async Task<ArquivoFiscal> GerarAfdAsync(Guid estabelecimentoId, DateOnly inicio, DateOnly fim, CancellationToken ct)
    {
        if (fim < inicio) throw new RegraNegocioException("Período inválido.");
        var e = await Estabelecimento(estabelecimentoId, ct);
        var fuso = TimeZoneInfo.FindSystemTimeZoneById(e.FusoHorario);
        var (de, ate) = Intervalo(inicio, fim, fuso);

        var registros = await db.RegistrosRep.AsNoTracking()
            .Where(r => r.EstabelecimentoId == e.Id && r.DataHoraGravacao >= de && r.DataHoraGravacao < ate)
            .OrderBy(r => r.Nsr)
            .ToListAsync(ct);

        var agora = clock.UtcNow;
        var arquivo = AfdGenerator.Gerar(new AfdCabecalho(
            e.TipoIdentificador, e.Identificador, e.CnoCaepf, e.Empregador!.RazaoSocial,
            _opt.NumeroRegistroInpi, inicio, fim, agora, RegistroRepService.Offset(fuso, agora),
            _opt.TipoIdentificadorDesenvolvedor, _opt.IdentificadorDesenvolvedor), registros);

        audit.Registrar("afd.gerado", nameof(Estabelecimento), e.Id, new { inicio, fim, arquivo.TotalRegistros });
        await db.SaveChangesAsync(ct);
        return new ArquivoFiscal(arquivo.NomeArquivo, arquivo.Conteudo, assinatura.AssinarCadesDestacado(arquivo.Conteudo));
    }

    public async Task<ArquivoFiscal> GerarAejAsync(Guid estabelecimentoId, DateOnly inicio, DateOnly fim, CancellationToken ct)
    {
        if (fim < inicio) throw new RegraNegocioException("Período inválido.");
        var e = await Estabelecimento(estabelecimentoId, ct);
        var fuso = TimeZoneInfo.FindSystemTimeZoneById(e.FusoHorario);

        var ids = await db.Funcionarios.AsNoTracking()
            .Where(f => f.EstabelecimentoId == e.Id && f.DataAdmissao <= fim && (f.DataDemissao == null || f.DataDemissao >= inicio))
            .OrderBy(f => f.Nome).Select(f => f.Id).ToListAsync(ct);

        var vinculos = new List<AejVinculo>();
        foreach (var id in ids)
        {
            var a = await espelho.ApurarAsync(id, inicio, fim, ct);
            var aus = new List<AejAusencia>();
            foreach (var d in a.Dias)
            {
                if (d.Data < a.Funcionario.DataAdmissao || (a.Funcionario.DataDemissao is { } dm && d.Data > dm)) continue;
                if (d.MinutosFalta > 0 && d.Ocorrencia?.StartsWith("Falta") == true)
                    aus.Add(new AejAusencia(d.Data, TipoAusencia.FaltaNaoJustificada));
                else if (d.MinutosPrevistos == 0 && d.Validas.Count == 0 && d.Data.DayOfWeek == DayOfWeek.Sunday)
                    aus.Add(new AejAusencia(d.Data, TipoAusencia.Dsr));
            }
            aus.AddRange(a.Complementos.MovimentosBancoHoras);
            vinculos.Add(new AejVinculo(a.Funcionario.Cpf, a.Funcionario.Nome, a.Funcionario.MatriculaEsocial,
                a.Funcionario.Jornada, a.Dias, aus, fuso));
        }

        var agora = clock.UtcNow;
        var caepfCno = e.CnoCaepf;
        var arquivo = AejGenerator.Gerar(
            new AejCabecalho(e.TipoIdentificador, e.Identificador,
                caepfCno?.Length == 14 ? caepfCno : null,
                caepfCno?.Length == 12 ? caepfCno : null,
                e.Empregador!.RazaoSocial, inicio, fim, agora, RegistroRepService.Offset(fuso, agora), _opt.NumeroRegistroInpi),
            new AejPtrp(_opt.NomePrograma, _opt.VersaoPrograma, _opt.TipoIdentificadorDesenvolvedor,
                _opt.IdentificadorDesenvolvedor, _opt.RazaoSocialDesenvolvedor, _opt.EmailDesenvolvedor),
            vinculos);

        audit.Registrar("aej.gerado", nameof(Estabelecimento), e.Id, new { inicio, fim, Vinculos = vinculos.Count });
        await db.SaveChangesAsync(ct);
        return new ArquivoFiscal(arquivo.NomeArquivo, arquivo.Conteudo, assinatura.AssinarCadesDestacado(arquivo.Conteudo));
    }

    private async Task<Estabelecimento> Estabelecimento(Guid id, CancellationToken ct) =>
        await db.Estabelecimentos.AsNoTracking().Include(x => x.Empregador).FirstOrDefaultAsync(x => x.Id == id, ct)
        ?? throw new NaoEncontradoException("Estabelecimento não encontrado.");

    public static (DateTimeOffset De, DateTimeOffset Ate) Intervalo(DateOnly inicio, DateOnly fim, TimeZoneInfo fuso)
    {
        var ini = inicio.ToDateTime(TimeOnly.MinValue);
        var fimEx = fim.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return (new DateTimeOffset(ini, fuso.GetUtcOffset(ini)).ToUniversalTime(),
                new DateTimeOffset(fimEx, fuso.GetUtcOffset(fimEx)).ToUniversalTime());
    }
}
