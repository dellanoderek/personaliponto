using System.Text;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Modules.SaaS.WhiteLabel;

/// <summary>
/// Gera as variáveis CSS do painel (app.css) a partir dos tokens da marca. Cores derivadas (hover, bordas, realces,
/// texto sobre primária/barra lateral) são calculadas aqui para que o revendedor informe só as cores-base.
/// Os nomes antigos (--marinho, --azul, --ciano, --ouro...) continuam como aliases usados pelos componentes.
/// </summary>
public static class CssMarca
{
    private static Cor L(string? hex, string padrao) => Cor.TentarLer(hex, out var c) ? c : Cor.Ler(padrao);

    public static IReadOnlyList<(string Nome, string Valor)> Variaveis(TokensMarca t)
    {
        var padrao = TemasMarca.NeutroAzul().Cores;
        var c = t.Cores ?? padrao;
        var primaria = L(c.Primaria, padrao.Primaria);
        var secundaria = L(c.Secundaria, padrao.Secundaria);
        var destaque = L(c.Destaque, padrao.Destaque);
        var fundo = L(c.Fundo, padrao.Fundo);
        var texto = L(c.Texto, padrao.Texto);
        var sidebar = L(c.Sidebar, padrao.Sidebar);
        var sucesso = L(c.Sucesso, TemasMarca.CorSucesso);
        var alerta = L(c.Alerta, TemasMarca.CorAlerta);
        var erro = L(c.Erro, TemasMarca.CorErro);

        var primariaTexto = primaria.TextoSobre();
        var primariaHover = primariaTexto == Cor.Branco ? primaria.Misturar(Cor.Preto, 0.15) : primaria.Misturar(Cor.Branco, 0.25);
        var sidebarBase = sidebar.TextoSobre();
        var sidebarTexto = sidebar.Misturar(sidebarBase, 0.82);
        if (Cor.Contraste(sidebarTexto, sidebar) < ValidadorMarca.ContrasteTexto) sidebarTexto = sidebarBase;
        var sidebarTexto2 = sidebar.Misturar(sidebarBase, 0.55);
        // Títulos: a cor da barra lateral quando for bem escura sobre o fundo (padrão dos dois temas); senão o texto escurecido.
        var titulo = Cor.Contraste(sidebar, fundo) >= 7 && Cor.Contraste(sidebar, Cor.Branco) >= 7
            ? sidebar : texto.AjustarContraste(9, fundo, Cor.Branco) ?? texto;

        return
        [
            ("--primaria", primaria.Hex),
            ("--primaria-texto", primariaTexto.Hex),
            ("--primaria-hover", primariaHover.Hex),
            ("--secundaria", secundaria.Hex),
            ("--secundaria-escura", secundaria.Misturar(Cor.Preto, 0.25).Hex),
            ("--destaque", destaque.Hex),
            ("--fundo", fundo.Hex),
            ("--fundo-suave", secundaria.Misturar(Cor.Branco, 0.9).Hex),
            ("--fundo-linha", fundo.Misturar(Cor.Branco, 0.5).Hex),
            ("--texto", texto.Hex),
            ("--titulo", titulo.Hex),
            ("--cinza", texto.Misturar(fundo, 0.25).Hex),
            ("--borda", texto.Misturar(Cor.Branco, 0.8).Hex),
            ("--borda-2", texto.Misturar(Cor.Branco, 0.75).Hex),
            ("--sidebar", sidebar.Hex),
            ("--sidebar-2", sidebar.Misturar(sidebarBase, 0.08).Hex),
            ("--sidebar-texto", sidebarTexto.Hex),
            ("--sidebar-texto-2", sidebarTexto2.Hex),
            ("--sidebar-linha", sidebarBase.Rgba(0.08)),
            ("--destaque-suave", destaque.Rgba(0.14)),
            ("--foco", secundaria.Rgba(0.14)),
            ("--primaria-sombra", primaria.Rgba(0.35)),
            ("--verde", sucesso.Hex),
            ("--verde-fundo", sucesso.Misturar(Cor.Branco, 0.9).Hex),
            ("--amarelo", alerta.Hex),
            ("--amarelo-fundo", alerta.Misturar(Cor.Branco, 0.9).Hex),
            ("--vermelho", erro.Hex),
            ("--vermelho-fundo", erro.Misturar(Cor.Branco, 0.9).Hex),
            ("--sombra", $"0 1px 2px {sidebar.Rgba(0.06)}, 0 4px 16px {sidebar.Rgba(0.06)}"),
            ("--sombra-lg", $"0 10px 40px {sidebar.Rgba(0.18)}"),
            ("--terminal-fundo", $"linear-gradient(160deg, {sidebar.Hex} 0%, {sidebar.Misturar(secundaria, 0.35).Hex} 60%, {sidebar.Misturar(primaria, 0.55).Hex} 100%)"),
            ("--fonte", FontesMarca.ObterOuPadrao(t.Fonte).Pilha)
        ];
    }

    /// <summary>Bloco CSS com as variáveis para o seletor informado (":root" no painel; classe isolada na prévia).</summary>
    public static string Gerar(TokensMarca t, string seletor = ":root")
    {
        var sb = new StringBuilder(seletor).Append(" {");
        foreach (var (n, v) in Variaveis(t)) sb.Append(n).Append(':').Append(v).Append(';');
        return sb.Append('}').ToString();
    }

    /// <summary>Favicon padrão (SVG com a inicial da marca) para canais sem favicon próprio.</summary>
    public static string FaviconPadrao(TokensMarca t, string nome)
    {
        var fundo = L(t.Cores?.Sidebar, "#082352");
        var letra = System.Net.WebUtility.HtmlEncode((nome.FirstOrDefault(char.IsLetterOrDigit) is var ch && ch != default ? ch : 'P').ToString().ToUpperInvariant());
        var svg = $"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 64 64'><rect width='64' height='64' rx='14' fill='{fundo.Hex}'/>"
            + $"<text x='32' y='44' font-family='Arial,sans-serif' font-size='36' font-weight='700' text-anchor='middle' fill='{fundo.TextoSobre().Hex}'>{letra}</text></svg>";
        return "data:image/svg+xml," + Uri.EscapeDataString(svg);
    }
}
