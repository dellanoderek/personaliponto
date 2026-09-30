using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Formatacao;

namespace PersonaliPonto.Core.RepP.Services;

/// <summary>Dados mínimos do Comprovante de Registro de Ponto do Trabalhador (art. 79, I a VIII).</summary>
public sealed record ComprovanteDados(
    Guid RegistroId,
    long Nsr,
    string Empregador,
    string EmpregadorIdentificador,
    string? CnoCaepf,
    string LocalPrestacao,
    string Trabalhador,
    string Cpf,
    DateTimeOffset DataHoraMarcacao,
    string NumeroRegistroInpi,
    string Hash,
    bool Offline,
    string Coletor);

public interface IComprovanteRenderer
{
    byte[] RenderizarPdf(ComprovanteDados dados);
}

public sealed class ComprovanteService(
    IRepPDbContext db,
    IComprovanteRenderer renderer,
    IAssinaturaDigital assinatura,
    IOptions<RepPOptions> options)
{
    public async Task<ComprovanteDados> DadosAsync(Guid registroId, Guid? somenteFuncionarioId, CancellationToken ct)
    {
        var r = await db.RegistrosRep.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == registroId && x.Tipo == TipoRegistroRep.MarcacaoRepP, ct)
                ?? throw new NaoEncontradoException("Comprovante não encontrado.");
        if (somenteFuncionarioId is not null && r.FuncionarioId != somenteFuncionarioId)
            throw new NaoEncontradoException("Comprovante não encontrado.");

        var e = await db.Estabelecimentos.AsNoTracking().Include(x => x.Empregador).FirstAsync(x => x.Id == r.EstabelecimentoId, ct);
        var nome = await db.Funcionarios.AsNoTracking().Where(f => f.Id == r.FuncionarioId).Select(f => f.Nome).FirstAsync(ct);

        return new ComprovanteDados(
            r.Id, r.Nsr,
            e.Empregador!.RazaoSocial,
            e.TipoIdentificador == TipoIdentificador.Cnpj ? Documentos.FormatarCnpj(e.Identificador) : Documentos.FormatarCpf(e.Identificador),
            e.CnoCaepf,
            e.LocalPrestacao,
            nome,
            Documentos.FormatarCpf(r.Cpf!),
            r.DataHoraMarcacao!.Value.ToOffset(TimeSpan.FromMinutes(r.OffsetMarcacaoMinutos ?? 0)),
            options.Value.NumeroRegistroInpi,
            r.Hash!,
            r.Offline == true,
            r.Coletor?.ToString() ?? "");
    }

    /// <summary>PDF assinado (PAdES) — art. 80, parágrafo único, I; FAQ MTE nº 30.</summary>
    public async Task<(string Nome, byte[] Pdf)> PdfAsync(Guid registroId, Guid? somenteFuncionarioId, CancellationToken ct)
    {
        var dados = await DadosAsync(registroId, somenteFuncionarioId, ct);
        var pdf = renderer.RenderizarPdf(dados);
        var assinado = assinatura.AssinarPdf(pdf, "Comprovante de Registro de Ponto do Trabalhador");
        return ($"comprovante-nsr-{dados.Nsr:000000000}.pdf", assinado);
    }
}
