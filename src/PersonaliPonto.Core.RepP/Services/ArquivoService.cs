using System.Security.Cryptography;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Domain;

namespace PersonaliPonto.Core.RepP.Services;

/// <summary>
/// Upload padronizado: imagens são convertidas para WebP e redimensionadas antes de persistir;
/// PDFs são aceitos como estão. Qualquer outro tipo é recusado.
/// </summary>
public sealed class ArquivoService(IRepPDbContext db, IFileStorage storage, IImageProcessor imagens, ITenantContext tenant, ICurrentUser user, IClock clock)
{
    public const long TamanhoMaximo = 10 * 1024 * 1024;

    public async Task<ArquivoArmazenado> EnviarAsync(string bucket, string nomeOriginal, byte[] conteudo, CancellationToken ct)
    {
        if (conteudo.Length == 0) throw new RegraNegocioException("Arquivo vazio.");
        if (conteudo.Length > TamanhoMaximo) throw new RegraNegocioException("Arquivo acima de 10 MB.");
        var tenantId = tenant.TenantId ?? throw new RegraNegocioException("Operação exige um tenant.");

        byte[] final;
        string contentType, ext;
        if (EhPdf(conteudo))
        {
            final = conteudo;
            contentType = "application/pdf";
            ext = "pdf";
        }
        else
        {
            final = imagens.ParaWebp(conteudo) ?? throw new RegraNegocioException("Formato não suportado. Envie imagem (JPG, PNG, WebP) ou PDF.");
            contentType = "image/webp";
            ext = "webp";
        }

        var id = Guid.CreateVersion7();
        var caminho = $"{tenantId}/{clock.UtcNow:yyyy/MM}/{id}.{ext}";
        await storage.SalvarAsync(bucket, caminho, final, contentType, ct);

        var arq = new ArquivoArmazenado
        {
            Id = id,
            TenantId = tenantId,
            Bucket = bucket,
            Caminho = caminho,
            ContentType = contentType,
            Tamanho = final.Length,
            NomeOriginal = Path.GetFileName(nomeOriginal),
            Sha256 = Convert.ToHexString(SHA256.HashData(final)),
            CriadoEm = clock.UtcNow,
            CriadoPor = user.UserId
        };
        db.Arquivos.Add(arq);
        await db.SaveChangesAsync(ct);
        return arq;
    }

    public async Task<(ArquivoArmazenado Info, byte[] Conteudo)?> ObterAsync(Guid id, CancellationToken ct)
    {
        var a = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(db.Arquivos, x => x.Id == id, ct);
        if (a is null) return null;
        var bytes = await storage.ObterAsync(a.Bucket, a.Caminho, ct);
        return bytes is null ? null : (a, bytes);
    }

    private static bool EhPdf(byte[] b) => b.Length > 4 && b[0] == '%' && b[1] == 'P' && b[2] == 'D' && b[3] == 'F';
}
