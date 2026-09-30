using System.Globalization;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Infrastructure.Documentos;

/// <summary>Relatório Espelho de Ponto Eletrônico (art. 84) em PDF, A4 paisagem.</summary>
public sealed class EspelhoPdfRenderer
{
    private static readonly CultureInfo Br = new("pt-BR");

    public byte[] Renderizar(EspelhoDto e)
    {
        FontesEmbarcadas.Registrar();
        var doc = new PdfDocument();
        doc.Info.Title = $"Espelho de Ponto - {e.Funcionario} - {e.Inicio:MM/yyyy}";
        doc.Info.Author = "PersonaliPonto";

        var f7 = new XFont("DejaVu", 7);
        var f7b = new XFont("DejaVu", 7, XFontStyleEx.Bold);
        var f6 = new XFont("DejaVu", 6);
        var f6b = new XFont("DejaVu", 6, XFontStyleEx.Bold);
        var titulo = new XFont("DejaVu", 13, XFontStyleEx.Bold);
        var marinho = new XSolidBrush(Marca.Marinho);
        var texto = new XSolidBrush(Marca.Texto);
        var azul = new XSolidBrush(Marca.Azul);

        // Colunas: Data, Dia, Contratual, Marcações, Trabalhado, Noturno, Extras, Atraso, Falta, Ocorrência
        double[] larg = [44, 26, 118, 250, 44, 40, 40, 40, 40, 150];
        string[] cab = ["Data", "Dia", "Horário contratual", "Marcações", "Trabalhado", "Noturno", "Extras", "Atraso", "Falta", "Ocorrências"];
        const double m = 28;

        PdfPage page = null!;
        XGraphics g = null!;
        double y = 0;
        var pagina = 0;

        void NovaPagina()
        {
            g?.Dispose();
            page = doc.AddPage();
            page.Size = PageSize.A4;
            page.Orientation = PageOrientation.Landscape;
            g = XGraphics.FromPdfPage(page);
            pagina++;
            var w = page.Width.Point;
            using (var logo = PdfUtil.LogoImagem()) g.DrawImage(logo, m, 18, 120, 120 * 365.0 / 970);
            g.DrawString("ESPELHO DE PONTO ELETRÔNICO", titulo, marinho, w - m, 26, XStringFormats.TopRight);
            g.DrawString($"Período {e.Inicio:dd/MM/yyyy} a {e.Fim:dd/MM/yyyy} · Emitido em {e.EmitidoEm:dd/MM/yyyy HH:mm}", f7, texto, w - m, 44, XStringFormats.TopRight);
            y = 70;
            g.DrawRoundedRectangle(new XPen(Marca.Borda, 0.7), new XSolidBrush(Marca.Fundo), m, y, w - 2 * m, 40, 6, 6);
            void Par(double x, double yy, string r, string v)
            {
                g.DrawString(r.ToUpperInvariant(), f6b, azul, x, yy);
                g.DrawString(v, f7, marinho, x, yy + 10);
            }
            Par(m + 10, y + 10, "Empregador", e.Empregador);
            Par(m + 250, y + 10, "CNPJ/CPF", e.EmpregadorIdentificador);
            Par(m + 420, y + 10, "Trabalhador", e.Funcionario);
            Par(m + 620, y + 10, "CPF", e.Cpf);
            y += 46;
            g.DrawRoundedRectangle(new XPen(Marca.Borda, 0.7), new XSolidBrush(Marca.Fundo), m, y, w - 2 * m, 30, 6, 6);
            Par(m + 10, y + 6, "Admissão", e.DataAdmissao?.ToString("dd/MM/yyyy") ?? "-");
            Par(m + 100, y + 6, "Cargo/função", e.Cargo ?? "-");
            Par(m + 250, y + 6, "Jornada contratual", e.Jornada ?? "-");
            Par(m + 620, y + 6, "Página", pagina.ToString());
            y += 40;
            // Cabeçalho da tabela
            g.DrawRectangle(new XSolidBrush(Marca.Marinho), m, y, larg.Sum(), 16);
            var x = m;
            for (var i = 0; i < cab.Length; i++)
            {
                g.DrawString(cab[i], f6b, XBrushes.White, new XRect(x + 3, y + 4, larg[i] - 6, 10), XStringFormats.TopLeft);
                x += larg[i];
            }
            y += 16;
        }

        NovaPagina();
        var zebra = false;
        foreach (var d in e.Dias)
        {
            var marcas = string.Join("  ", d.Marcacoes.Select(mc =>
            {
                var s = mc.DataHora.ToString("HH:mm");
                if (mc.Desconsiderada) return $"[{s} D]";
                return mc.Fonte switch { "Incluída" => s + "(I)", "Pré-assinalada" => s + "(P)", _ => s };
            }));
            var linhasMarc = PdfUtil.Quebrar(g, marcas.Length == 0 ? "-" : marcas, f7, larg[3] - 6);
            var linhasOc = PdfUtil.Quebrar(g, d.Ocorrencia ?? "", f6, larg[9] - 6);
            var altura = Math.Max(14, 5 + 9 * Math.Max(linhasMarc.Count, Math.Max(1, linhasOc.Count)));
            if (y + altura > page.Height.Point - 110) NovaPagina();

            if (zebra) g.DrawRectangle(new XSolidBrush(Marca.Fundo), m, y, larg.Sum(), altura);
            zebra = !zebra;
            var x = m;
            void Cel(int i, string v, XFont f, XBrush? b = null)
            {
                g.DrawString(v, f, b ?? marinho, new XRect(x + 3, y + 4, larg[i] - 6, 10), XStringFormats.TopLeft);
                x += larg[i];
            }
            Cel(0, d.Data.ToString("dd/MM"), f7b);
            Cel(1, Br.DateTimeFormat.GetAbbreviatedDayName(d.Data.DayOfWeek).TrimEnd('.'), f7);
            Cel(2, d.HorarioContratual ?? "-", f6);
            var xm = x;
            for (var k = 0; k < linhasMarc.Count; k++)
                g.DrawString(linhasMarc[k], f7, marinho, new XRect(xm + 3, y + 4 + 9 * k, larg[3] - 6, 10), XStringFormats.TopLeft);
            x += larg[3];
            Cel(4, EspelhoService.Hhmm(d.MinutosTrabalhados), f7);
            Cel(5, d.MinutosNoturnos > 0 ? EspelhoService.Hhmm(d.MinutosNoturnos) : "", f7);
            Cel(6, d.MinutosExtras > 0 ? EspelhoService.Hhmm(d.MinutosExtras) : "", f7, azul);
            Cel(7, d.MinutosAtraso > 0 ? EspelhoService.Hhmm(d.MinutosAtraso) : "", f7, new XSolidBrush(Marca.Vermelho));
            Cel(8, d.MinutosFalta > 0 ? EspelhoService.Hhmm(d.MinutosFalta) : "", f7, new XSolidBrush(Marca.Vermelho));
            for (var k = 0; k < linhasOc.Count; k++)
                g.DrawString(linhasOc[k], f6, d.Inconsistente ? new XSolidBrush(Marca.Vermelho) : texto, new XRect(x + 3, y + 4 + 9 * k, larg[9] - 6, 10), XStringFormats.TopLeft);
            g.DrawLine(new XPen(Marca.Borda, 0.4), m, y + altura, m + larg.Sum(), y + altura);
            y += altura;
        }

        // Totais
        y += 8;
        var totais = $"Totais do período — Trabalhado: {EspelhoService.Hhmm(e.TotalTrabalhado)}   Extras: {EspelhoService.Hhmm(e.TotalExtras)}   " +
                     $"Atrasos: {EspelhoService.Hhmm(e.TotalAtrasos)}   Faltas: {EspelhoService.Hhmm(e.TotalFaltas)}   Saldo banco de horas: {EspelhoService.Hhmm(e.SaldoBancoHoras)}";
        g.DrawRoundedRectangle(new XSolidBrush(Marca.FundoAzul), m, y, larg.Sum(), 20, 6, 6);
        g.DrawString(totais, f7b, marinho, m + 10, y + 13);
        y += 30;
        g.DrawString("Legenda: (I) marcação incluída no tratamento · (P) pré-assinalada · [D] desconsiderada (a marcação original é preservada). " +
                     "Horas noturnas consideram a hora reduzida (52min30s).", f6, texto, m, y);

        var yAss = page.Height.Point - 50;
        var w2 = page.Width.Point;
        g.DrawLine(new XPen(Marca.Marinho, 0.6), m, yAss, m + 260, yAss);
        g.DrawLine(new XPen(Marca.Marinho, 0.6), w2 - m - 260, yAss, w2 - m, yAss);
        g.DrawString("Assinatura do trabalhador", f6, texto, new XRect(m, yAss + 4, 260, 10), XStringFormats.TopCenter);
        g.DrawString("Assinatura do empregador", f6, texto, new XRect(w2 - m - 260, yAss + 4, 260, 10), XStringFormats.TopCenter);
        g.DrawString("PERSONALIPONTO | Plataforma Inteligente de Gestão de Jornada", f6, texto, new XRect(m, page.Height.Point - 22, w2 - 2 * m, 10), XStringFormats.TopCenter);
        g.Dispose();
        return PdfUtil.Salvar(doc);
    }
}
