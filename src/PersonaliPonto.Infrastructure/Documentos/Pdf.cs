using System.Reflection;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace PersonaliPonto.Infrastructure.Documentos;

/// <summary>Paleta institucional extraída da apresentação comercial PersonaliPonto.</summary>
public static class Marca
{
    public static readonly XColor Marinho = XColor.FromArgb(0x08, 0x23, 0x52);
    public static readonly XColor Azul = XColor.FromArgb(0x11, 0x66, 0xD6);
    public static readonly XColor Ciano = XColor.FromArgb(0x21, 0xB8, 0xEF);
    public static readonly XColor Texto = XColor.FromArgb(0x59, 0x70, 0x8E);
    public static readonly XColor Fundo = XColor.FromArgb(0xF4, 0xF8, 0xFB);
    public static readonly XColor FundoAzul = XColor.FromArgb(0xE6, 0xF4, 0xFD);
    public static readonly XColor Borda = XColor.FromArgb(0xDB, 0xE6, 0xF0);
    public static readonly XColor Vermelho = XColor.FromArgb(0xC0, 0x39, 0x2B);
}

/// <summary>Resolve as fontes DejaVu embarcadas (mesma família da apresentação), independente do SO.</summary>
public sealed class FontesEmbarcadas : IFontResolver
{
    private static readonly Lazy<Dictionary<string, byte[]>> Fontes = new(() =>
    {
        var asm = Assembly.GetExecutingAssembly();
        byte[] Ler(string nome)
        {
            var res = asm.GetManifestResourceNames().First(n => n.EndsWith(nome, StringComparison.Ordinal));
            using var s = asm.GetManifestResourceStream(res)!;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
        return new Dictionary<string, byte[]>
        {
            ["DejaVu#R"] = Ler("DejaVuSans.ttf"),
            ["DejaVu#B"] = Ler("DejaVuSans-Bold.ttf"),
            ["DejaVuMono#R"] = Ler("DejaVuSansMono.ttf")
        };
    });

    private static int _registrado;

    public static void Registrar()
    {
        if (Interlocked.Exchange(ref _registrado, 1) == 1) return;
        GlobalFontSettings.FontResolver = new FontesEmbarcadas();
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        if (familyName.Contains("Mono", StringComparison.OrdinalIgnoreCase)) return new FontResolverInfo("DejaVuMono#R");
        return new FontResolverInfo(bold ? "DejaVu#B" : "DejaVu#R");
    }

    public byte[]? GetFont(string faceName) => Fontes.Value.GetValueOrDefault(faceName);
}

internal static class PdfUtil
{
    public static readonly Lazy<byte[]> Logo = new(() =>
    {
        var asm = Assembly.GetExecutingAssembly();
        var res = asm.GetManifestResourceNames().First(n => n.EndsWith("logo.png", StringComparison.Ordinal));
        using var s = asm.GetManifestResourceStream(res)!;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    });

    public static XImage LogoImagem() => XImage.FromStream(new MemoryStream(Logo.Value));

    public static byte[] Salvar(PdfDocument doc)
    {
        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }

    /// <summary>Quebra texto em linhas que cabem na largura.</summary>
    public static List<string> Quebrar(XGraphics g, string texto, XFont fonte, double largura)
    {
        var linhas = new List<string>();
        var atual = "";
        foreach (var palavra in texto.Split(' '))
        {
            var teste = atual.Length == 0 ? palavra : atual + " " + palavra;
            if (g.MeasureString(teste, fonte).Width <= largura) atual = teste;
            else
            {
                if (atual.Length > 0) linhas.Add(atual);
                atual = palavra;
                while (g.MeasureString(atual, fonte).Width > largura && atual.Length > 1)
                {
                    var n = atual.Length - 1;
                    while (n > 1 && g.MeasureString(atual[..n], fonte).Width > largura) n--;
                    linhas.Add(atual[..n]);
                    atual = atual[n..];
                }
            }
        }
        if (atual.Length > 0) linhas.Add(atual);
        return linhas;
    }
}
