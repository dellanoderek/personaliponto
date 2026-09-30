namespace PersonaliPonto.Modules.SaaS.Domain;

/// <summary>Nível do canal na hierarquia de revenda: Owner → Revendedor → Parceiro (máximo 3 níveis).</summary>
public enum TipoCanal
{
    Owner = 1,
    Revendedor = 2,
    Parceiro = 3
}

/// <summary>Situação comercial do canal (régua de inadimplência: D+5 aviso, D+15 painel bloqueado, D+30 suspensão).</summary>
public enum StatusCanal
{
    Ativo = 0,
    Aviso = 1,
    PainelBloqueado = 2,
    Suspenso = 3
}

/// <summary>
/// Canal de venda: o Owner (plataforma), um revendedor ou um parceiro de revendedor.
/// <see cref="Caminho"/> é o caminho materializado ("/{owner}/{revendedor}/{parceiro}/") usado para
/// resolver descendentes com um único LIKE. <see cref="Nivel"/> e <see cref="Caminho"/> são recalculados
/// pelo banco (trigger) a partir do canal pai — não confie no valor enviado pela aplicação.
/// </summary>
public class Canal
{
    /// <summary>Canal Owner raiz ("PersonaliPonto"), criado pela migration e pelo seed de forma idempotente.</summary>
    public static readonly Guid OwnerRaizId = new("00000000-0000-7000-8000-000000000001");

    public const int ProfundidadeMaxima = 3;

    public Guid Id { get; set; } = Guid.CreateVersion7();
    public TipoCanal Tipo { get; set; }
    public Guid? CanalPaiId { get; set; }
    public string Caminho { get; set; } = "";
    public int Nivel { get; set; }
    public string Slug { get; set; } = "";
    public string RazaoSocial { get; set; } = "";
    public string Cnpj { get; set; } = "";
    /// <summary>Nome da marca exibido nos painéis (e fornecedor padrão dos clientes do canal).</summary>
    public string NomeMarca { get; set; } = "";
    public StatusCanal Status { get; set; } = StatusCanal.Ativo;
    public string? Email { get; set; }
    public string? Telefone { get; set; }
    public DateTimeOffset CriadoEm { get; set; }

    // ---- Condições comerciais (E2). Alteráveis só pela plataforma ou por canal ascendente (trigger). ----

    /// <summary>Premium isento até esta data (inclusive), ex.: revendedor fundador 24 meses. Definido pela plataforma.</summary>
    public DateOnly? PremiumIsentoAte { get; set; }
    /// <summary>Serviço avulso "app próprio nas lojas" contratado (R$ 1.500 implantação + R$ 200/mês).</summary>
    public bool AppProprioAtivo { get; set; }
    public DateOnly? AppProprioDesde { get; set; }
    /// <summary>Implantação do app próprio já lançada em fatura (cobrança única).</summary>
    public bool AppProprioImplantacaoCobrada { get; set; }
    /// <summary>Parceiro: preço por funcionário ativo definido pelo revendedor (nulo = revendedor não cobra o parceiro).</summary>
    public decimal? PrecoFuncionarioParceiro { get; set; }
    /// <summary>Parceiro: mínimo mensal opcional definido pelo revendedor.</summary>
    public decimal? MinimoMensalParceiro { get; set; }
    /// <summary>Parceiro: isento na competência quando tiver ao menos N clientes públicos ativos (nulo = sem regra).</summary>
    public int? IsencaoMinClientesPublicos { get; set; }

    public static int NivelDe(TipoCanal tipo) => (int)tipo;

    /// <summary>Tipo de filho permitido para um canal deste tipo (null = não pode ter filhos).</summary>
    public static TipoCanal? TipoFilho(TipoCanal pai) => pai switch
    {
        TipoCanal.Owner => TipoCanal.Revendedor,
        TipoCanal.Revendedor => TipoCanal.Parceiro,
        _ => null
    };
}

/// <summary>Município brasileiro (código IBGE de 7 dígitos). Tabela global (somente leitura para os usuários).</summary>
public class Municipio
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public int CodigoIbge { get; set; }
    public string Nome { get; set; } = "";
    public string Uf { get; set; } = "";
}

/// <summary>Nível de quem iniciou um acesso de suporte.</summary>
public enum NivelSuporte
{
    Plataforma = 0,
    Revendedor = 1,
    Parceiro = 2
}
