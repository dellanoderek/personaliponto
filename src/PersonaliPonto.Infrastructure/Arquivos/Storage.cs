using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using SkiaSharp;
using PersonaliPonto.Core.RepP.Abstractions;

namespace PersonaliPonto.Infrastructure.Arquivos;

public sealed class StorageOptions
{
    /// <summary>"supabase" ou "local".</summary>
    public string Provedor { get; set; } = "local";
    public string? SupabaseUrl { get; set; }
    /// <summary>Chave service_role (somente no servidor; nunca exposta ao navegador/app).</summary>
    public string? SupabaseServiceKey { get; set; }
    public string DiretorioLocal { get; set; } = ".local/storage";
}

/// <summary>Supabase Storage via API REST (buckets privados).</summary>
public sealed class SupabaseStorage(HttpClient http, IOptions<StorageOptions> options) : IFileStorage
{
    private readonly StorageOptions _o = options.Value;
    private readonly HashSet<string> _bucketsVerificados = [];

    private HttpRequestMessage Req(HttpMethod m, string url)
    {
        var r = new HttpRequestMessage(m, $"{_o.SupabaseUrl!.TrimEnd('/')}/storage/v1/{url}");
        r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _o.SupabaseServiceKey);
        r.Headers.Add("apikey", _o.SupabaseServiceKey);
        return r;
    }

    private async Task GarantirBucketAsync(string bucket, CancellationToken ct)
    {
        lock (_bucketsVerificados) if (_bucketsVerificados.Contains(bucket)) return;
        using var get = await http.SendAsync(Req(HttpMethod.Get, $"bucket/{bucket}"), ct);
        if (get.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            var criar = Req(HttpMethod.Post, "bucket");
            criar.Content = System.Net.Http.Json.JsonContent.Create(new { id = bucket, name = bucket, @public = false });
            using var resp = await http.SendAsync(criar, ct);
            if (!resp.IsSuccessStatusCode && resp.StatusCode != HttpStatusCode.Conflict)
                throw new InvalidOperationException($"Falha ao criar bucket {bucket}: {await resp.Content.ReadAsStringAsync(ct)}");
        }
        lock (_bucketsVerificados) _bucketsVerificados.Add(bucket);
    }

    public async Task SalvarAsync(string bucket, string caminho, byte[] conteudo, string contentType, CancellationToken ct)
    {
        await GarantirBucketAsync(bucket, ct);
        var req = Req(HttpMethod.Post, $"object/{bucket}/{caminho}");
        req.Content = new ByteArrayContent(conteudo);
        req.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        req.Headers.Add("x-upsert", "false");
        req.Headers.Add("cache-control", "max-age=31536000");
        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Falha no upload ({(int)resp.StatusCode}): {await resp.Content.ReadAsStringAsync(ct)}");
    }

    public async Task<byte[]?> ObterAsync(string bucket, string caminho, CancellationToken ct)
    {
        using var resp = await http.SendAsync(Req(HttpMethod.Get, $"object/{bucket}/{caminho}"), ct);
        if (resp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task ExcluirAsync(string bucket, string caminho, CancellationToken ct)
    {
        var req = Req(HttpMethod.Delete, $"object/{bucket}");
        req.Content = System.Net.Http.Json.JsonContent.Create(new { prefixes = new[] { caminho } });
        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode && resp.StatusCode != HttpStatusCode.NotFound)
            throw new InvalidOperationException($"Falha ao excluir arquivo ({(int)resp.StatusCode}).");
    }
}

/// <summary>Armazenamento em disco para desenvolvimento e testes.</summary>
public sealed class LocalFileStorage(IOptions<StorageOptions> options) : IFileStorage
{
    private string Caminho(string bucket, string caminho)
    {
        var raiz = Path.GetFullPath(options.Value.DiretorioLocal);
        var completo = Path.GetFullPath(Path.Combine(raiz, bucket, caminho));
        if (!completo.StartsWith(raiz, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Caminho inválido.");
        return completo;
    }

    public async Task SalvarAsync(string bucket, string caminho, byte[] conteudo, string contentType, CancellationToken ct)
    {
        var p = Caminho(bucket, caminho);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        await File.WriteAllBytesAsync(p, conteudo, ct);
    }

    public async Task<byte[]?> ObterAsync(string bucket, string caminho, CancellationToken ct)
    {
        var p = Caminho(bucket, caminho);
        return File.Exists(p) ? await File.ReadAllBytesAsync(p, ct) : null;
    }

    public Task ExcluirAsync(string bucket, string caminho, CancellationToken ct)
    {
        var p = Caminho(bucket, caminho);
        if (File.Exists(p)) File.Delete(p);
        return Task.CompletedTask;
    }
}

/// <summary>Conversão para WebP com redimensionamento (decisão 7 — reduz consumo de storage e banda).</summary>
public sealed class SkiaImageProcessor : IImageProcessor
{
    public byte[]? ParaWebp(byte[] imagem, int ladoMaximo = 1600, int qualidade = 70)
    {
        using var codec = SKCodec.Create(new MemoryStream(imagem));
        if (codec is null) return null;
        using var original = SKBitmap.Decode(codec);
        if (original is null) return null;

        // Corrige orientação EXIF (fotos de celular).
        using var orientado = Orientar(original, codec.EncodedOrigin);
        var escala = Math.Min(1.0, (double)ladoMaximo / Math.Max(orientado.Width, orientado.Height));
        var w = Math.Max(1, (int)Math.Round(orientado.Width * escala));
        var h = Math.Max(1, (int)Math.Round(orientado.Height * escala));
        using var redim = escala < 1.0 ? orientado.Resize(new SKImageInfo(w, h), new SKSamplingOptions(SKCubicResampler.Mitchell)) : orientado.Copy();
        using var img = SKImage.FromBitmap(redim);
        using var data = img.Encode(SKEncodedImageFormat.Webp, qualidade);
        return data.ToArray();
    }

    private static SKBitmap Orientar(SKBitmap bmp, SKEncodedOrigin origem)
    {
        if (origem == SKEncodedOrigin.TopLeft) return bmp.Copy();
        var rot = origem is SKEncodedOrigin.RightTop or SKEncodedOrigin.LeftBottom;
        var r = new SKBitmap(rot ? bmp.Height : bmp.Width, rot ? bmp.Width : bmp.Height);
        using var c = new SKCanvas(r);
        switch (origem)
        {
            case SKEncodedOrigin.BottomRight: c.RotateDegrees(180, bmp.Width / 2f, bmp.Height / 2f); break;
            case SKEncodedOrigin.RightTop: c.Translate(r.Width, 0); c.RotateDegrees(90); break;
            case SKEncodedOrigin.LeftBottom: c.Translate(0, r.Height); c.RotateDegrees(270); break;
        }
        c.DrawBitmap(bmp, 0, 0);
        return r;
    }
}
