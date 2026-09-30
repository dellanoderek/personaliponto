namespace PersonaliPonto.Shared.Contracts;

/// <summary>Estilo do menu principal do painel web.</summary>
public enum EstiloMenu
{
    Lateral = 0,
    Superior = 1,
    Compacto = 2
}

/// <summary>Cores da marca (hex #rrggbb). As opcionais usam o padrão da plataforma quando nulas.</summary>
public sealed class CoresMarca
{
    /// <summary>Botões primários e elementos de ação.</summary>
    public string Primaria { get; set; } = "";
    /// <summary>Links, abas ativas e números em fundo claro.</summary>
    public string Secundaria { get; set; } = "";
    /// <summary>Destaque sobre a barra lateral (seções, item ativo).</summary>
    public string Destaque { get; set; } = "";
    public string Fundo { get; set; } = "";
    /// <summary>Texto corrido sobre o fundo e os cartões (brancos).</summary>
    public string Texto { get; set; } = "";
    /// <summary>Barra lateral/superfícies escuras (cabeçalho do terminal, tela de login).</summary>
    public string Sidebar { get; set; } = "";
    public string? Sucesso { get; set; }
    public string? Alerta { get; set; }
    public string? Erro { get; set; }

    public CoresMarca Clonar() => (CoresMarca)MemberwiseClone();
}

/// <summary>
/// Tokens versionados da marca de um canal (JSON). Tudo que não for informado cai no padrão
/// (tema neutro azul para revendedores). Documentos legais nunca usam estes tokens.
/// </summary>
public sealed class TokensMarca
{
    /// <summary>Versão do esquema do JSON (evolução compatível dos tokens).</summary>
    public int Esquema { get; set; } = 1;
    public CoresMarca Cores { get; set; } = new();
    /// <summary>Id de uma fonte da lista curada (ver FontesMarca).</summary>
    public string Fonte { get; set; } = "inter";
    public EstiloMenu Menu { get; set; } = EstiloMenu.Lateral;
    /// <summary>Ordem preferida dos itens de menu (ids estáveis do registro central); ausentes seguem a ordem padrão.</summary>
    public List<string> OrdemMenu { get; set; } = [];
    /// <summary>Itens de menu ocultos (itens obrigatórios/legais são ignorados aqui).</summary>
    public List<string> MenuOculto { get; set; } = [];
    /// <summary>Ordem dos indicadores (KPIs) do painel.</summary>
    public List<string> OrdemKpis { get; set; } = [];
    /// <summary>Indicadores/cartões ocultos do painel.</summary>
    public List<string> PainelOculto { get; set; } = [];
    /// <summary>Textos personalizados por chave (nomes de telas, rótulos); chave ausente = texto padrão.</summary>
    public Dictionary<string, string> Textos { get; set; } = [];
    /// <summary>Nome exibido da marca (nulo = nome da marca do canal).</summary>
    public string? NomeExibicao { get; set; }
    /// <summary>Nome da assistente de IA (a "Nali", renomeável).</summary>
    public string NomeAssistente { get; set; } = "Nali";

    public TokensMarca Clonar() => new()
    {
        Esquema = Esquema, Cores = Cores.Clonar(), Fonte = Fonte, Menu = Menu,
        OrdemMenu = [.. OrdemMenu], MenuOculto = [.. MenuOculto], OrdemKpis = [.. OrdemKpis], PainelOculto = [.. PainelOculto],
        Textos = new Dictionary<string, string>(Textos), NomeExibicao = NomeExibicao, NomeAssistente = NomeAssistente
    };
}

/// <summary>Marca resolvida para o app (GET /api/app/marca). Dados públicos de marca apenas.</summary>
public sealed record MarcaAppDto(
    Guid? CanalId,
    string Nome,
    int Versao,
    bool Plataforma,
    bool ExibirByPersonaliPonto,
    TokensMarca Tokens,
    string FonteFamilia,
    string? FonteUrl,
    string? LogoUrl,
    string? LogoEscuraUrl,
    string? FaviconUrl,
    string? ImagemLoginUrl,
    string NomeAssistente);
