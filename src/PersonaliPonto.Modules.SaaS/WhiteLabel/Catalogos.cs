using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Modules.SaaS.WhiteLabel;

/// <summary>Fonte da lista curada: Google Fonts (carregada por URL) ou pilha de fontes do sistema.</summary>
public sealed record FonteMarca(string Id, string Nome, string Pilha, string? GoogleFamilia)
{
    public string? Url => GoogleFamilia is null ? null
        : $"https://fonts.googleapis.com/css2?family={GoogleFamilia}:wght@400;500;600;700&display=swap";
}

public static class FontesMarca
{
    private const string Sans = "system-ui, -apple-system, \"Segoe UI\", Roboto, sans-serif";

    public static readonly IReadOnlyList<FonteMarca> Todas =
    [
        new("inter", "Inter", $"\"Inter\", \"DejaVu Sans\", {Sans}", "Inter"),
        new("roboto", "Roboto", $"\"Roboto\", {Sans}", "Roboto"),
        new("open-sans", "Open Sans", $"\"Open Sans\", {Sans}", "Open+Sans"),
        new("lato", "Lato", $"\"Lato\", {Sans}", "Lato"),
        new("montserrat", "Montserrat", $"\"Montserrat\", {Sans}", "Montserrat"),
        new("poppins", "Poppins", $"\"Poppins\", {Sans}", "Poppins"),
        new("nunito", "Nunito", $"\"Nunito\", {Sans}", "Nunito"),
        new("source-sans-3", "Source Sans 3", $"\"Source Sans 3\", {Sans}", "Source+Sans+3"),
        new("work-sans", "Work Sans", $"\"Work Sans\", {Sans}", "Work+Sans"),
        new("raleway", "Raleway", $"\"Raleway\", {Sans}", "Raleway"),
        new("ibm-plex-sans", "IBM Plex Sans", $"\"IBM Plex Sans\", {Sans}", "IBM+Plex+Sans"),
        new("manrope", "Manrope", $"\"Manrope\", {Sans}", "Manrope"),
        new("dm-sans", "DM Sans", $"\"DM Sans\", {Sans}", "DM+Sans"),
        new("rubik", "Rubik", $"\"Rubik\", {Sans}", "Rubik"),
        new("noto-sans", "Noto Sans", $"\"Noto Sans\", {Sans}", "Noto+Sans"),
        new("sistema", "Fonte do sistema", Sans, null),
        new("arial", "Arial / Helvetica", "Arial, Helvetica, \"Liberation Sans\", sans-serif", null),
        new("georgia", "Georgia (serifada)", "Georgia, \"Times New Roman\", serif", null)
    ];

    public const string Padrao = "inter";

    public static FonteMarca? Obter(string? id) => Todas.FirstOrDefault(f => f.Id == id);

    public static FonteMarca ObterOuPadrao(string? id) => Obter(id) ?? Obter(Padrao)!;
}

/// <summary>Temas de fábrica.</summary>
public static class TemasMarca
{
    /// <summary>Tema neutro azul (docs/marca/tema-neutro-azul.md): padrão de todo revendedor sem personalização.</summary>
    public static TokensMarca NeutroAzul() => new()
    {
        Cores = new CoresMarca
        {
            Primaria = "#1166d6", Secundaria = "#1166d6", Destaque = "#21b8ef",
            Fundo = "#f4f8fb", Texto = "#59708e", Sidebar = "#082352"
        },
        Fonte = FontesMarca.Padrao
    };

    /// <summary>Marca PersonaliPonto (preto + dourado). Exclusiva do Owner; nunca atribuível a um canal.</summary>
    public static TokensMarca PersonaliPonto() => new()
    {
        Cores = new CoresMarca
        {
            Primaria = "#c9a54c", Secundaria = "#7a5c1e", Destaque = "#e6c77a",
            Fundo = "#faf8f3", Texto = "#6b6b73", Sidebar = "#0b0b0d"
        },
        Fonte = FontesMarca.Padrao
    };

    public const string CorSucesso = "#16a34a";
    public const string CorAlerta = "#b7791f";
    public const string CorErro = "#c0392b";
}

/// <summary>Perfil do usuário para montar o menu (derivado de papéis/claims pela camada web).</summary>
public sealed record PerfilMenu(
    bool Plataforma = false,
    bool Canal = false,
    bool Revendedor = false,
    bool AdminCanal = false,
    bool Empresa = false,
    bool Rh = false,
    bool AdminEmpresa = false,
    bool Funcionario = false);

public sealed record SecaoMenu(string Id, string Rotulo, bool Personalizavel);

/// <summary>Item do registro central de menu. <see cref="Id"/> é estável (usado nos tokens da marca).</summary>
public sealed record ItemMenu(
    string Id,
    string Secao,
    string Rotulo,
    string Href,
    string Icone,
    Func<PerfilMenu, bool> Visivel,
    bool Obrigatorio = false,
    bool CorrespondenciaExata = false,
    string? RotuloParceiro = null);

public sealed record ItemMenuMontado(string Id, string Rotulo, string Href, string Icone, bool CorrespondenciaExata, bool Obrigatorio);

public sealed record SecaoMenuMontada(string Id, string Rotulo, IReadOnlyList<ItemMenuMontado> Itens);

/// <summary>
/// Registro central dos itens de menu do painel web (substitui os links fixos do MainLayout). Itens obrigatórios
/// (acesso a marcações, espelho, AFD/AEJ, auditoria, área do trabalhador, faturas e o próprio editor de marca)
/// nunca são ocultados pela marca. O menu da plataforma (Owner) não é personalizável.
/// </summary>
public static class RegistroMenu
{
    public static readonly IReadOnlyList<SecaoMenu> Secoes =
    [
        new("plataforma", "Plataforma", false),
        new("canal", "Revendedor", true),
        new("operacao", "Operação", true),
        new("cadastros", "Cadastros", true),
        new("conformidade", "Conformidade", true),
        new("configuracoes", "Configurações", true),
        new("meu-ponto", "Meu ponto", true)
    ];

    public static readonly IReadOnlyList<ItemMenu> Itens =
    [
        new("plataforma.visao", "plataforma", "Visão geral", "/plataforma", "painel", p => p.Plataforma, CorrespondenciaExata: true),
        new("plataforma.clientes", "plataforma", "Clientes", "/plataforma/clientes", "predio", p => p.Plataforma),
        new("plataforma.revendedores", "plataforma", "Revendedores", "/plataforma/revendedores", "globo", p => p.Plataforma),
        new("plataforma.dominios", "plataforma", "Domínios de canais", "/plataforma/dominios", "globo", p => p.Plataforma),
        new("plataforma.faturamento", "plataforma", "Faturamento", "/plataforma/faturamento", "dinheiro", p => p.Plataforma),
        new("plataforma.faturamento-canais", "plataforma", "Faturamento de canais", "/plataforma/faturamento-canais", "dinheiro", p => p.Plataforma),
        new("plataforma.importar", "plataforma", "Importar planilha", "/plataforma/importar", "upload", p => p.Plataforma),
        new("plataforma.planos", "plataforma", "Planos", "/plataforma/planos", "cartao", p => p.Plataforma),
        new("plataforma.acessos", "plataforma", "Acessos de suporte", "/plataforma/acessos", "escudo", p => p.Plataforma),

        new("canal.visao", "canal", "Visão geral", "/canal", "painel", p => p.Canal, CorrespondenciaExata: true),
        new("canal.clientes", "canal", "Clientes", "/canal/clientes", "predio", p => p.Canal),
        new("canal.parceiros", "canal", "Parceiros", "/canal/parceiros", "pessoas", p => p.Canal && p.Revendedor),
        new("canal.minha-conta", "canal", "Minha conta PersonaliPonto", "/canal/minha-conta", "cartao", p => p.Canal, Obrigatorio: true, RotuloParceiro: "Minhas faturas"),
        new("canal.usuarios", "canal", "Usuários do canal", "/canal/usuarios", "usuario", p => p.Canal && p.AdminCanal),
        new("canal.marca", "canal", "Marca e aparência", "/canal/marca", "paleta", p => p.Canal && p.AdminCanal, Obrigatorio: true),

        new("op.painel", "operacao", "Painel", "/", "painel", p => p.Empresa, CorrespondenciaExata: true),
        new("op.marcacoes", "operacao", "Marcações", "/marcacoes", "relogio", p => p.Empresa, Obrigatorio: true),
        new("op.espelho", "operacao", "Espelho de ponto", "/espelho", "espelho", p => p.Empresa, Obrigatorio: true),
        new("op.solicitacoes", "operacao", "Solicitações", "/solicitacoes", "checklist", p => p.Empresa),
        new("op.atestados", "operacao", "Atestados", "/atestados", "atestado", p => p.Empresa),
        new("op.banco-horas", "operacao", "Banco de horas", "/banco-horas", "banco", p => p.Empresa),
        new("op.fechamento", "operacao", "Fechamento", "/fechamento", "cadeado", p => p.Empresa && p.Rh),

        new("cad.funcionarios", "cadastros", "Funcionários", "/funcionarios", "pessoas", p => p.Empresa),
        new("cad.jornadas", "cadastros", "Jornadas e escalas", "/jornadas", "calendario", p => p.Empresa && p.Rh),
        new("cad.estabelecimentos", "cadastros", "Empresa e locais", "/estabelecimentos", "predio", p => p.Empresa && p.Rh),
        new("cad.feriados", "cadastros", "Feriados", "/feriados", "feriado", p => p.Empresa && p.Rh),

        new("conf.relatorios", "conformidade", "Relatórios", "/relatorios", "relatorio", p => p.Empresa),
        new("conf.arquivos-fiscais", "conformidade", "AFD e AEJ", "/arquivos-fiscais", "arquivo", p => p.Empresa && p.Rh, Obrigatorio: true),
        new("conf.seguranca", "conformidade", "Segurança e antifraude", "/seguranca", "alerta", p => p.Empresa && p.Rh),
        new("conf.auditoria", "conformidade", "Auditoria", "/auditoria", "escudo", p => p.Empresa && p.Rh, Obrigatorio: true),

        new("cfg.terminais", "configuracoes", "Terminais de ponto", "/terminais", "terminal", p => p.Empresa && p.Rh),
        new("cfg.usuarios", "configuracoes", "Usuários", "/usuarios", "usuario", p => p.Empresa && p.Rh),
        new("cfg.minha-assinatura", "configuracoes", "Minha assinatura", "/minha-assinatura", "cartao", p => p.Empresa && p.AdminEmpresa, Obrigatorio: true),

        new("func.registrar", "meu-ponto", "Registrar ponto", "/meu-ponto", "relogio", p => p.Funcionario, Obrigatorio: true, CorrespondenciaExata: true),
        new("func.espelho", "meu-ponto", "Meu espelho", "/meu-ponto/espelho", "espelho", p => p.Funcionario, Obrigatorio: true),
        new("func.solicitacoes", "meu-ponto", "Ajustes e abonos", "/meu-ponto/solicitacoes", "checklist", p => p.Funcionario)
    ];

    public static ItemMenu? Obter(string id) => Itens.FirstOrDefault(i => i.Id == id);

    /// <summary>Itens que a marca pode ordenar/ocultar/renomear (fora da plataforma).</summary>
    public static IEnumerable<ItemMenu> Personalizaveis =>
        Itens.Where(i => Secoes.First(s => s.Id == i.Secao).Personalizavel);

    /// <summary>
    /// Monta o menu do perfil aplicando a marca: ordem preferida dentro de cada seção, itens ocultos (exceto
    /// obrigatórios) e textos personalizados. Sem <paramref name="tokens"/>, retorna o menu padrão.
    /// </summary>
    public static IReadOnlyList<SecaoMenuMontada> Montar(PerfilMenu perfil, TokensMarca? tokens)
    {
        var ordem = tokens?.OrdemMenu ?? [];
        var ocultos = tokens?.MenuOculto ?? [];
        var resultado = new List<SecaoMenuMontada>();
        foreach (var secao in Secoes)
        {
            var itens = Itens
                .Select((item, indice) => (item, indice))
                .Where(x => x.item.Secao == secao.Id && x.item.Visivel(perfil))
                .Where(x => !secao.Personalizavel || x.item.Obrigatorio || !ocultos.Contains(x.item.Id))
                .OrderBy(x => secao.Personalizavel && ordem.IndexOf(x.item.Id) is var p and >= 0 ? p : 10_000 + x.indice)
                .Select(x =>
                {
                    var padrao = !perfil.Revendedor && x.item.RotuloParceiro is not null ? x.item.RotuloParceiro : x.item.Rotulo;
                    var rotulo = secao.Personalizavel ? TextosMarca.Resolver(tokens, x.item.Id, padrao) : padrao;
                    return new ItemMenuMontado(x.item.Id, rotulo, x.item.Href, x.item.Icone, x.item.CorrespondenciaExata, x.item.Obrigatorio);
                })
                .ToList();
            if (itens.Count == 0) continue;
            var rotuloSecao = secao.Id == "canal" ? (perfil.Revendedor ? "Revendedor" : "Parceiro") : secao.Rotulo;
            if (secao.Personalizavel) rotuloSecao = TextosMarca.Resolver(tokens, "secao." + secao.Id, rotuloSecao);
            resultado.Add(new SecaoMenuMontada(secao.Id, rotuloSecao, itens));
        }
        return resultado;
    }
}

public sealed record ItemPainel(string Id, string Rotulo, bool Kpi, bool Obrigatorio = false);

/// <summary>Registro dos indicadores (KPIs) e cartões do painel da empresa: ordem e visibilidade configuráveis pela marca.</summary>
public static class RegistroPainel
{
    public static readonly IReadOnlyList<ItemPainel> Itens =
    [
        new("kpi.funcionarios", "Funcionários ativos", true),
        new("kpi.marcacoes", "Marcações hoje", true),
        new("kpi.extras", "Horas extras", true),
        new("kpi.absenteismo", "Absenteísmo", true),
        new("card.grafico", "Extras, atrasos e faltas por dia", false),
        new("card.pendencias", "Pendências do RH", false),
        new("card.funcionarios", "Funcionários no período", false),
        new("card.conformidade", "Conformidade REP-P", false, Obrigatorio: true)
    ];

    public static IEnumerable<ItemPainel> Kpis => Itens.Where(i => i.Kpi);

    /// <summary>KPIs visíveis na ordem da marca.</summary>
    public static IReadOnlyList<ItemPainel> KpisVisiveis(TokensMarca? t)
    {
        var ordem = t?.OrdemKpis ?? [];
        return Kpis.Select((k, i) => (k, i))
            .Where(x => Visivel(t, x.k.Id))
            .OrderBy(x => ordem.IndexOf(x.k.Id) is var p and >= 0 ? p : 1000 + x.i)
            .Select(x => x.k).ToList();
    }

    public static bool Visivel(TokensMarca? t, string id) =>
        Itens.FirstOrDefault(i => i.Id == id) is { } item && (item.Obrigatorio || t is null || !t.PainelOculto.Contains(id));
}

/// <summary>Dicionário de textos da marca: chave → texto padrão (fallback quando a marca não personaliza).</summary>
public static class TextosMarca
{
    public const int TamanhoMaximo = 80;

    private static readonly Dictionary<string, string> Gerais = new()
    {
        ["login.eyebrow"] = "PLATAFORMA INTELIGENTE",
        ["login.titulo"] = "DE GESTÃO DE JORNADA",
        ["login.subtitulo"] = "Controle completo da jornada de trabalho em uma única plataforma.",
        ["login.chamada"] = "Acesse sua conta",
        ["rodape.slogan"] = "Plataforma Inteligente de Gestão de Jornada",
        ["contexto.empresa"] = "Gestão de jornada",
        ["contexto.canal"] = "Painel do canal",
        ["painel.secao"] = "Painel",
        ["painel.titulo"] = "Saúde da operação",
        ["terminal.titulo"] = "Registrar ponto",
        ["funcionario.titulo"] = "Meu ponto"
    };

    /// <summary>Todas as chaves conhecidas com o texto padrão (gerais, seções/itens de menu e itens do painel).</summary>
    public static IReadOnlyDictionary<string, string> Padroes { get; } = Construir();

    private static Dictionary<string, string> Construir()
    {
        var d = new Dictionary<string, string>(Gerais);
        foreach (var s in RegistroMenu.Secoes.Where(s => s.Personalizavel)) d["secao." + s.Id] = s.Rotulo;
        foreach (var i in RegistroMenu.Personalizaveis) d[i.Id] = i.Rotulo;
        foreach (var i in RegistroPainel.Itens) d[i.Id] = i.Rotulo;
        return d;
    }

    public static string Resolver(TokensMarca? tokens, string chave, string? padrao = null)
    {
        if (tokens?.Textos is { } t && t.TryGetValue(chave, out var v) && !string.IsNullOrWhiteSpace(v)) return v;
        return padrao ?? (Padroes.TryGetValue(chave, out var p) ? p : chave);
    }
}
