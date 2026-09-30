using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PersonaliPonto.Core.RepP.Services;

namespace PersonaliPonto.Infrastructure.Documentos;

/// <summary>
/// Comprovante de Registro de Ponto do Trabalhador (art. 79). Formato A6 retrato, contendo todos os
/// itens obrigatórios: título, NSR, empregador, local, trabalhador, data/hora, registro INPI e hash SHA-256.
/// </summary>
public sealed class ComprovanteRenderer : IComprovanteRenderer
{
    public byte[] RenderizarPdf(ComprovanteDados d)
    {
        FontesEmbarcadas.Registrar();
        var doc = new PdfDocument();
        doc.Info.Title = "Comprovante de Registro de Ponto do Trabalhador";
        doc.Info.Author = "PersonaliPonto";
        doc.Info.Subject = $"NSR {d.Nsr:000000000}";
        var page = doc.AddPage();
        page.Width = XUnit.FromMillimeter(105);
        page.Height = XUnit.FromMillimeter(170);
        using var g = XGraphics.FromPdfPage(page);

        var w = page.Width.Point;
        const double m = 16;
        var titulo = new XFont("DejaVu", 9.5, XFontStyleEx.Bold);
        var rotulo = new XFont("DejaVu", 6.2, XFontStyleEx.Bold);
        var valor = new XFont("DejaVu", 8);
        var destaque = new XFont("DejaVu", 17, XFontStyleEx.Bold);
        var mono = new XFont("DejaVu Mono", 6.3);

        // Faixa superior marinho com logo.
        g.DrawRectangle(new XSolidBrush(Marca.Marinho), 0, 0, w, 62);
        g.DrawRectangle(new XSolidBrush(XColors.White), m, 12, 118, 40);
        using (var logo = PdfUtil.LogoImagem()) g.DrawImage(logo, m + 4, 14, 110, 110 * 365.0 / 970);
        g.DrawString("REP-P", new XFont("DejaVu", 7, XFontStyleEx.Bold), new XSolidBrush(Marca.Ciano), w - m, 30, XStringFormats.TopRight);
        g.DrawString("Portaria MTP 671/2021", new XFont("DejaVu", 5.5), XBrushes.White, w - m, 40, XStringFormats.TopRight);

        var y = 76.0;
        g.DrawString("Comprovante de Registro de Ponto do Trabalhador", titulo, new XSolidBrush(Marca.Marinho), new XRect(m, y, w - 2 * m, 14), XStringFormats.TopCenter);
        y += 22;

        // Bloco de data/hora em destaque.
        g.DrawRoundedRectangle(new XPen(Marca.Borda, 0.8), new XSolidBrush(Marca.FundoAzul), m, y, w - 2 * m, 50, 8, 8);
        g.DrawString(d.DataHoraMarcacao.ToString("dd/MM/yyyy"), new XFont("DejaVu", 8, XFontStyleEx.Bold), new XSolidBrush(Marca.Azul), new XRect(m, y + 7, w - 2 * m, 10), XStringFormats.TopCenter);
        g.DrawString(d.DataHoraMarcacao.ToString("HH:mm"), destaque, new XSolidBrush(Marca.Marinho), new XRect(m, y + 19, w - 2 * m, 20), XStringFormats.TopCenter);
        var fuso = d.DataHoraMarcacao.Offset;
        g.DrawString($"UTC{(fuso < TimeSpan.Zero ? "-" : "+")}{fuso:hh\\:mm}{(d.Offline ? " · registrada off-line" : "")}", new XFont("DejaVu", 5.8), new XSolidBrush(Marca.Texto),
            new XRect(m, y + 39, w - 2 * m, 8), XStringFormats.TopCenter);
        y += 60;

        void Campo(string r, string v, XFont? f = null)
        {
            g.DrawString(r.ToUpperInvariant(), rotulo, new XSolidBrush(Marca.Azul), m, y);
            y += 9;
            foreach (var linha in PdfUtil.Quebrar(g, v, f ?? valor, w - 2 * m))
            {
                g.DrawString(linha, f ?? valor, new XSolidBrush(Marca.Marinho), m, y);
                y += (f ?? valor).Size + 2.5;
            }
            y += 5;
        }

        Campo("NSR", d.Nsr.ToString("000000000"));
        Campo("Empregador", d.Empregador);
        Campo("CNPJ/CPF" + (string.IsNullOrEmpty(d.CnoCaepf) ? "" : " · CNO/CAEPF"),
            d.EmpregadorIdentificador + (string.IsNullOrEmpty(d.CnoCaepf) ? "" : " · " + d.CnoCaepf));
        Campo("Local de prestação de serviço", d.LocalPrestacao);
        Campo("Trabalhador", d.Trabalhador);
        Campo("CPF", d.Cpf);
        Campo("Registro no INPI (REP-P)", string.IsNullOrWhiteSpace(d.NumeroRegistroInpi) ? "não configurado" : d.NumeroRegistroInpi);
        Campo("Código hash (SHA-256)", d.Hash[..32] + " " + d.Hash[32..], mono);

        g.DrawLine(new XPen(Marca.Borda, 0.6), m, page.Height.Point - 30, w - m, page.Height.Point - 30);
        g.DrawString("Documento assinado eletronicamente · PERSONALIPONTO | Plataforma Inteligente de Gestão de Jornada",
            new XFont("DejaVu", 4.8), new XSolidBrush(Marca.Texto), new XRect(m, page.Height.Point - 26, w - 2 * m, 8), XStringFormats.TopCenter);

        return PdfUtil.Salvar(doc);
    }
}
