namespace PersonaliPonto.Core.RepP.Abstractions;

/// <summary>Armazenamento de arquivos (Supabase Storage em produção; disco local em desenvolvimento).</summary>
public interface IFileStorage
{
    Task SalvarAsync(string bucket, string caminho, byte[] conteudo, string contentType, CancellationToken ct);
    Task<byte[]?> ObterAsync(string bucket, string caminho, CancellationToken ct);
    Task ExcluirAsync(string bucket, string caminho, CancellationToken ct);
}

/// <summary>Compressão de imagens antes de persistir (decisão 7: WebP, redimensionada, comprimida ao máximo).</summary>
public interface IImageProcessor
{
    /// <summary>Converte para WebP limitando o maior lado. Retorna nulo se o conteúdo não for imagem.</summary>
    byte[]? ParaWebp(byte[] imagem, int ladoMaximo = 1600, int qualidade = 70);
}
