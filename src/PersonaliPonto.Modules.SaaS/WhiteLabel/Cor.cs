using System.Globalization;

namespace PersonaliPonto.Modules.SaaS.WhiteLabel;

/// <summary>Cor sRGB com as medidas usadas na validação da marca (luminância relativa WCAG 2.x, contraste, HSL).</summary>
public readonly record struct Cor(byte R, byte G, byte B)
{
    public static readonly Cor Branco = new(255, 255, 255);
    public static readonly Cor Preto = new(0, 0, 0);
    /// <summary>"Quase preto" usado como texto sobre cores claras.</summary>
    public static readonly Cor TextoEscuro = new(0x11, 0x14, 0x1a);

    public static bool TentarLer(string? hex, out Cor cor)
    {
        cor = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var s = hex.Trim();
        if (s.StartsWith('#')) s = s[1..];
        if (s.Length == 3) s = string.Concat(s.Select(c => new string(c, 2)));
        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        cor = new Cor((byte)(v >> 16), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF));
        return true;
    }

    public static Cor Ler(string hex) => TentarLer(hex, out var c) ? c : throw new FormatException($"Cor inválida: {hex}");

    public string Hex => $"#{R:x2}{G:x2}{B:x2}";

    public override string ToString() => Hex;

    public string Rgba(double alfa) => $"rgba({R}, {G}, {B}, {alfa.ToString("0.##", CultureInfo.InvariantCulture)})";

    private static double Linear(byte c)
    {
        var s = c / 255.0;
        return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }

    /// <summary>Luminância relativa (WCAG 2.x), 0 = preto, 1 = branco.</summary>
    public double Luminancia => 0.2126 * Linear(R) + 0.7152 * Linear(G) + 0.0722 * Linear(B);

    /// <summary>Razão de contraste WCAG entre duas cores (1 a 21).</summary>
    public static double Contraste(Cor a, Cor b)
    {
        var (l1, l2) = (a.Luminancia, b.Luminancia);
        if (l1 < l2) (l1, l2) = (l2, l1);
        return (l1 + 0.05) / (l2 + 0.05);
    }

    /// <summary>Matiz (0–360°), saturação e luminosidade (0–1) no modelo HSL.</summary>
    public (double H, double S, double L) Hsl
    {
        get
        {
            double r = R / 255.0, g = G / 255.0, b = B / 255.0;
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var l = (max + min) / 2;
            if (max - min < 1e-9) return (0, 0, l);
            var d = max - min;
            var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            double h;
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            return (h * 60, s, l);
        }
    }

    /// <summary>Mistura linear em sRGB: t = 0 mantém esta cor, t = 1 resulta em <paramref name="outra"/>.</summary>
    public Cor Misturar(Cor outra, double t)
    {
        t = Math.Clamp(t, 0, 1);
        byte M(byte a, byte b) => (byte)Math.Round(a + (b - a) * t);
        return new Cor(M(R, outra.R), M(G, outra.G), M(B, outra.B));
    }

    /// <summary>Texto (branco ou quase preto) com maior contraste sobre esta cor.</summary>
    public Cor TextoSobre() => Contraste(Branco, this) >= Contraste(TextoEscuro, this) ? Branco : TextoEscuro;

    /// <summary>
    /// Ajusta esta cor (escurecendo ou clareando, preservando o matiz) até atingir <paramref name="alvo"/> de contraste
    /// sobre todos os <paramref name="fundos"/>. Retorna a menor alteração possível, ou nulo se não houver.
    /// </summary>
    public Cor? AjustarContraste(double alvo, params Cor[] fundos)
    {
        bool Ok(Cor c) => fundos.All(f => Contraste(c, f) >= alvo);
        if (Ok(this)) return this;
        Cor? melhor = null;
        var melhorT = double.MaxValue;
        foreach (var destino in new[] { Preto, Branco })
        {
            for (var i = 1; i <= 100; i++)
            {
                var t = i / 100.0;
                var c = Misturar(destino, t);
                if (!Ok(c)) continue;
                if (t < melhorT) { melhorT = t; melhor = c; }
                break;
            }
        }
        return melhor;
    }
}
