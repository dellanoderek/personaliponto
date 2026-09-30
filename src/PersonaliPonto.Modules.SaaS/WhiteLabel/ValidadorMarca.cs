using System.Text.RegularExpressions;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Modules.SaaS.WhiteLabel;

/// <summary>Problema encontrado na marca. <see cref="Sugestao"/> traz uma cor que resolve o contraste, quando houver.</summary>
public sealed record ProblemaMarca(string Campo, string Mensagem, string? Sugestao = null);

/// <summary>
/// Validação dos tokens de marca de um canal (revendedor/parceiro):
/// <list type="bullet">
/// <item>Formato: cores obrigatórias em hex, fonte da lista curada, textos de chaves conhecidas (até 80 caracteres),
/// itens de menu/painel existentes e itens obrigatórios nunca ocultos.</item>
/// <item>Contraste WCAG 2.x nível AA (4,5:1 para texto): texto sobre o fundo e sobre cartões brancos; cor secundária
/// (links) sobre branco; destaque sobre a barra lateral; texto automático (branco ou quase preto) sobre a primária e a
/// barra lateral. Cores semânticas opcionais: 3:1 sobre branco (componentes gráficos e rótulos em negrito).</item>
/// <item>Exclusividade da marca PersonaliPonto: recusa a combinação preto + dourado (ver <see cref="EhPretoGrafite"/>,
/// <see cref="EhDourado"/> e <see cref="PretoDourado"/>).</item>
/// </list>
/// </summary>
public static partial class ValidadorMarca
{
    public const double ContrasteTexto = 4.5;
    public const double ContrasteGrafico = 3.0;

    /// <summary>
    /// "Preto/grafite": luminância relativa ≤ 0,03 (mais escuro que ~#303030) e cor neutra — saturação HSL ≤ 0,25 ou
    /// luminosidade HSL ≤ 0,10 (qualquer matiz tão escuro que se lê como preto). Azul-marinho saturado (ex.: #082352,
    /// L = 0,18) não é preto.
    /// </summary>
    public static bool EhPretoGrafite(Cor c)
    {
        var (_, s, l) = c.Hsl;
        return c.Luminancia <= 0.03 && (s <= 0.25 || l <= 0.10);
    }

    /// <summary>
    /// "Dourado": matiz HSL entre 35° e 50°, saturação ≥ 0,35 e luminosidade entre 0,25 e 0,80
    /// (cobre #9c7a2c, #7a5c1e, #c9a54c, #e6c77a e também âmbar/mostarda — que só é recusado junto com preto).
    /// </summary>
    public static bool EhDourado(Cor c)
    {
        var (h, s, l) = c.Hsl;
        return h >= 35 && h <= 50 && s >= 0.35 && l >= 0.25 && l <= 0.80;
    }

    /// <summary>
    /// Regra objetiva da exclusividade: há superfície predominante preta/grafite (barra lateral, fundo ou primária)
    /// E um destaque dourado (primária, secundária ou destaque).
    /// </summary>
    public static bool PretoDourado(CoresMarca cores)
    {
        Cor? L(string? hex) => Cor.TentarLer(hex, out var c) ? c : null;
        var superficies = new[] { L(cores.Sidebar), L(cores.Fundo), L(cores.Primaria) };
        var destaques = new[] { L(cores.Primaria), L(cores.Secundaria), L(cores.Destaque) };
        return superficies.Any(c => c is { } x && EhPretoGrafite(x)) && destaques.Any(c => c is { } x && EhDourado(x));
    }

    [GeneratedRegex(@"^[\p{L}][\p{L}\p{M} '\-]{1,29}$")]
    private static partial Regex NomeAssistenteRegex();

    public static List<ProblemaMarca> Validar(TokensMarca t)
    {
        var p = new List<ProblemaMarca>();
        var c = t.Cores ?? new CoresMarca();

        Cor? Req(string campo, string? hex, string nome)
        {
            if (Cor.TentarLer(hex, out var cor)) return cor;
            p.Add(new ProblemaMarca(campo, $"{nome}: informe uma cor hexadecimal (ex.: #1166d6)."));
            return null;
        }
        Cor? Opc(string campo, string? hex, string nome)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            if (Cor.TentarLer(hex, out var cor)) return cor;
            p.Add(new ProblemaMarca(campo, $"{nome}: cor hexadecimal inválida."));
            return null;
        }

        var primaria = Req("cores.primaria", c.Primaria, "Cor primária");
        var secundaria = Req("cores.secundaria", c.Secundaria, "Cor secundária");
        var destaque = Req("cores.destaque", c.Destaque, "Cor de destaque");
        var fundo = Req("cores.fundo", c.Fundo, "Cor de fundo");
        var texto = Req("cores.texto", c.Texto, "Cor do texto");
        var sidebar = Req("cores.sidebar", c.Sidebar, "Cor da barra lateral");
        var sucesso = Opc("cores.sucesso", c.Sucesso, "Cor de sucesso");
        var alerta = Opc("cores.alerta", c.Alerta, "Cor de alerta");
        var erro = Opc("cores.erro", c.Erro, "Cor de erro");

        if (PretoDourado(c))
            p.Add(new ProblemaMarca("cores", "A combinação preto + dourado é exclusiva da marca PersonaliPonto. Escolha outra cor para a superfície escura ou para o destaque."));

        string Fmt(double v) => v.ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

        if (texto is { } tx && fundo is { } fd)
        {
            var pior = Math.Min(Cor.Contraste(tx, fd), Cor.Contraste(tx, Cor.Branco));
            if (pior < ContrasteTexto)
                p.Add(new ProblemaMarca("cores.texto", $"Contraste do texto insuficiente ({Fmt(pior)}:1; mínimo 4,5:1 sobre o fundo e os cartões brancos).",
                    tx.AjustarContraste(ContrasteTexto, fd, Cor.Branco)?.Hex));
        }
        if (secundaria is { } sc && Cor.Contraste(sc, Cor.Branco) is var cs && cs < ContrasteTexto)
            p.Add(new ProblemaMarca("cores.secundaria", $"A cor secundária é usada em links e precisa de 4,5:1 sobre branco (atual {Fmt(cs)}:1).",
                sc.AjustarContraste(ContrasteTexto, Cor.Branco)?.Hex));
        if (primaria is { } pr && Cor.Contraste(pr.TextoSobre(), pr) is var cp && cp < ContrasteTexto)
            p.Add(new ProblemaMarca("cores.primaria", $"Nenhuma cor de texto atinge 4,5:1 sobre a primária (máximo {Fmt(cp)}:1). Escureça ou clareie a cor.",
                pr.AjustarContraste(ContrasteTexto, Cor.Branco)?.Hex));
        if (sidebar is { } sb)
        {
            if (Cor.Contraste(sb.TextoSobre(), sb) is var csb && csb < ContrasteTexto)
                p.Add(new ProblemaMarca("cores.sidebar", $"Texto sem contraste sobre a barra lateral ({Fmt(csb)}:1).", sb.AjustarContraste(ContrasteTexto, Cor.Branco)?.Hex));
            if (destaque is { } dt && Cor.Contraste(dt, sb) is var cd && cd < ContrasteTexto)
                p.Add(new ProblemaMarca("cores.destaque", $"O destaque aparece sobre a barra lateral e precisa de 4,5:1 (atual {Fmt(cd)}:1).",
                    dt.AjustarContraste(ContrasteTexto, sb)?.Hex));
        }
        foreach (var (campo, cor, nome) in new[] { ("cores.sucesso", sucesso, "sucesso"), ("cores.alerta", alerta, "alerta"), ("cores.erro", erro, "erro") })
            if (cor is { } x && Cor.Contraste(x, Cor.Branco) is var cx && cx < ContrasteGrafico)
                p.Add(new ProblemaMarca(campo, $"A cor de {nome} precisa de 3:1 sobre branco (atual {Fmt(cx)}:1).", x.AjustarContraste(ContrasteGrafico, Cor.Branco)?.Hex));

        if (FontesMarca.Obter(t.Fonte) is null)
            p.Add(new ProblemaMarca("fonte", "Fonte fora da lista permitida."));
        if (!Enum.IsDefined(t.Menu))
            p.Add(new ProblemaMarca("menu", "Estilo de menu inválido."));

        var personalizaveis = RegistroMenu.Personalizaveis.Select(i => i.Id).ToHashSet();
        foreach (var id in (t.OrdemMenu ?? []).Concat(t.MenuOculto ?? []).Distinct())
            if (!personalizaveis.Contains(id)) p.Add(new ProblemaMarca("menu", $"Item de menu desconhecido: {id}."));
        foreach (var id in t.MenuOculto ?? [])
            if (RegistroMenu.Obter(id) is { Obrigatorio: true } item)
                p.Add(new ProblemaMarca("menu", $"\"{item.Rotulo}\" é obrigatório (acesso legal/financeiro) e não pode ser ocultado."));

        var painel = RegistroPainel.Itens.ToDictionary(i => i.Id);
        foreach (var id in (t.OrdemKpis ?? []).Concat(t.PainelOculto ?? []).Distinct())
            if (!painel.ContainsKey(id)) p.Add(new ProblemaMarca("painel", $"Item do painel desconhecido: {id}."));
        foreach (var id in t.PainelOculto ?? [])
            if (painel.TryGetValue(id, out var ip) && ip.Obrigatorio)
                p.Add(new ProblemaMarca("painel", $"\"{ip.Rotulo}\" é obrigatório e não pode ser ocultado."));

        foreach (var (chave, valor) in t.Textos ?? [])
        {
            if (!TextosMarca.Padroes.ContainsKey(chave)) p.Add(new ProblemaMarca("textos", $"Chave de texto desconhecida: {chave}."));
            else if ((valor ?? "").Trim().Length > TextosMarca.TamanhoMaximo)
                p.Add(new ProblemaMarca("textos", $"Texto \"{chave}\" excede {TextosMarca.TamanhoMaximo} caracteres."));
        }

        if (t.NomeExibicao is { } nome)
        {
            if (nome.Trim().Length is < 2 or > 60) p.Add(new ProblemaMarca("nome", "O nome da marca deve ter de 2 a 60 caracteres."));
            if (ContemPersonaliPonto(nome)) p.Add(new ProblemaMarca("nome", "O nome \"PersonaliPonto\" é exclusivo da plataforma."));
        }
        if (!NomeAssistenteRegex().IsMatch((t.NomeAssistente ?? "").Trim()))
            p.Add(new ProblemaMarca("assistente", "Nome da assistente: 2 a 30 letras (espaços, hífen e apóstrofo permitidos)."));
        else if (!string.Equals(t.NomeAssistente?.Trim(), "Nali", StringComparison.OrdinalIgnoreCase) && ContemPersonaliPonto(t.NomeAssistente!))
            p.Add(new ProblemaMarca("assistente", "O nome \"PersonaliPonto\" é exclusivo da plataforma."));
        return p;
    }

    public static bool ContemPersonaliPonto(string s) =>
        new string(s.Where(char.IsLetter).ToArray()).Contains("personaliponto", StringComparison.OrdinalIgnoreCase);

    /// <summary>Normaliza: hex em minúsculas (#rrggbb), textos aparados e vazios removidos, listas sem duplicatas.</summary>
    public static TokensMarca Normalizar(TokensMarca origem)
    {
        var t = origem.Clonar();
        string N(string hex) => Cor.TentarLer(hex, out var c) ? c.Hex : hex;
        string? NO(string? hex) => string.IsNullOrWhiteSpace(hex) ? null : N(hex);
        t.Esquema = 1;
        t.Cores = new CoresMarca
        {
            Primaria = N(t.Cores.Primaria), Secundaria = N(t.Cores.Secundaria), Destaque = N(t.Cores.Destaque),
            Fundo = N(t.Cores.Fundo), Texto = N(t.Cores.Texto), Sidebar = N(t.Cores.Sidebar),
            Sucesso = NO(t.Cores.Sucesso), Alerta = NO(t.Cores.Alerta), Erro = NO(t.Cores.Erro)
        };
        t.OrdemMenu = t.OrdemMenu.Distinct().ToList();
        t.MenuOculto = t.MenuOculto.Distinct().ToList();
        t.OrdemKpis = t.OrdemKpis.Distinct().ToList();
        t.PainelOculto = t.PainelOculto.Distinct().ToList();
        t.Textos = t.Textos
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value) && TextosMarca.Padroes.TryGetValue(kv.Key, out var padrao) && padrao != kv.Value.Trim())
            .ToDictionary(kv => kv.Key, kv => kv.Value.Trim());
        t.NomeExibicao = string.IsNullOrWhiteSpace(t.NomeExibicao) ? null : t.NomeExibicao.Trim();
        t.NomeAssistente = string.IsNullOrWhiteSpace(t.NomeAssistente) ? "Nali" : t.NomeAssistente.Trim();
        return t;
    }
}
