using PersonaliPonto.Core.RepP.Domain;

namespace PersonaliPonto.Modules.SaaS.Domain;

/// <summary>
/// Tabela de preço Owner → revendedor, versionada por vigência (competência inicial). A versão aplicada a uma
/// competência é a de maior <see cref="VigenciaInicio"/> menor ou igual à competência. Tabela global:
/// leitura livre, escrita só pela plataforma.
/// </summary>
public class TabelaPrecoCanal
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>Primeiro dia da competência a partir da qual a tabela vale.</summary>
    public DateOnly VigenciaInicio { get; set; }
    public decimal PrecoEmpresaAtiva { get; set; } = 12m;
    /// <summary>Último funcionário da 1ª faixa (inclusive).</summary>
    public int Faixa1Ate { get; set; } = 2000;
    public decimal Faixa1Preco { get; set; } = 0.30m;
    /// <summary>Último funcionário da 2ª faixa (inclusive).</summary>
    public int Faixa2Ate { get; set; } = 5000;
    public decimal Faixa2Preco { get; set; } = 0.25m;
    public decimal Faixa3Preco { get; set; } = 0.20m;
    /// <summary>Mínimo mensal do uso (empresas + funcionários).</summary>
    public decimal MinimoMensal { get; set; } = 290m;
    public decimal PrecoPremium { get; set; } = 149m;
    public decimal AppProprioImplantacao { get; set; } = 1500m;
    public decimal AppProprioMensal { get; set; } = 200m;
    public string? Observacao { get; set; }
    public DateTimeOffset CriadaEm { get; set; }

    /// <summary>Valores padrão da decisão do dono (docs/decisoes-negocio.md §4).</summary>
    public static readonly Guid PadraoId = new("00000000-0000-7000-8000-00000000f001");
}

/// <summary>Resultado do cálculo de uso: itens (faixas marginais) e ajuste ao mínimo.</summary>
public sealed record CalculoUso(
    int Empresas, int Funcionarios,
    decimal ValorEmpresas, int QtdFaixa1, decimal ValorFaixa1, int QtdFaixa2, decimal ValorFaixa2, int QtdFaixa3, decimal ValorFaixa3,
    decimal AjusteMinimo)
{
    public decimal Subtotal => ValorEmpresas + ValorFaixa1 + ValorFaixa2 + ValorFaixa3;
    public decimal Total => Subtotal + AjusteMinimo;
}

public static class CalculadoraUsoCanal
{
    /// <summary>Uso do revendedor na competência: R$/empresa ativa + faixas marginais por funcionário, com mínimo.</summary>
    public static CalculoUso Calcular(TabelaPrecoCanal t, int empresas, int funcionarios)
    {
        if (empresas < 0 || funcionarios < 0) throw new ArgumentOutOfRangeException(nameof(funcionarios));
        var f1 = Math.Min(funcionarios, t.Faixa1Ate);
        var f2 = Math.Clamp(funcionarios - t.Faixa1Ate, 0, t.Faixa2Ate - t.Faixa1Ate);
        var f3 = Math.Max(funcionarios - t.Faixa2Ate, 0);
        var ve = Math.Round(empresas * t.PrecoEmpresaAtiva, 2);
        var v1 = Math.Round(f1 * t.Faixa1Preco, 2);
        var v2 = Math.Round(f2 * t.Faixa2Preco, 2);
        var v3 = Math.Round(f3 * t.Faixa3Preco, 2);
        var sub = ve + v1 + v2 + v3;
        return new CalculoUso(empresas, funcionarios, ve, f1, v1, f2, v2, f3, v3, sub < t.MinimoMensal ? t.MinimoMensal - sub : 0m);
    }

    /// <summary>Sem pró-rata: o canal não é cobrado na competência em que entrou; a primeira cobrança é a do mês seguinte.</summary>
    public static DateOnly PrimeiraCompetenciaCobrada(DateOnly dataEntrada) => new DateOnly(dataEntrada.Year, dataEntrada.Month, 1).AddMonths(1);

    public static bool Cobravel(DateOnly dataEntrada, DateOnly competencia) => competencia >= PrimeiraCompetenciaCobrada(dataEntrada);

    /// <summary>Fatura do parceiro: funcionários ativos × preço definido pelo revendedor, com mínimo opcional.</summary>
    public static (decimal Valor, decimal AjusteMinimo) CalcularParceiro(int funcionarios, decimal precoFuncionario, decimal? minimo)
    {
        var v = Math.Round(funcionarios * precoFuncionario, 2);
        var ajuste = minimo is { } m && v < m ? m - v : 0m;
        return (v, ajuste);
    }
}

/// <summary>
/// Snapshot imutável da apuração de uso de um revendedor numa competência (inclui clientes dos parceiros).
/// Gerado uma única vez pelo job (idempotente: índice único canal+competência).
/// </summary>
public class ApuracaoUsoCanal : IImmutableEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>Revendedor apurado (o RLS de canal usa esta coluna: parceiros não enxergam).</summary>
    public Guid CanalId { get; set; }
    public DateOnly Competencia { get; set; }
    public int EmpresasAtivas { get; set; }
    public int FuncionariosAtivos { get; set; }
    public DateTimeOffset GeradaEm { get; set; }
}

/// <summary>Detalhe da apuração por cliente (imutável). Não é dado do tenant: pertence ao canal apurado.</summary>
public class ApuracaoUsoTenant : IImmutableEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ApuracaoId { get; set; }
    /// <summary>Revendedor da apuração (mesmo da apuração, para o RLS).</summary>
    public Guid CanalId { get; set; }
    /// <summary>Canal dono do cliente (o próprio revendedor ou um parceiro dele).</summary>
    public Guid CanalDonoId { get; set; }
    public Guid ClienteId { get; set; }
    public string NomeCliente { get; set; } = "";
    public TenantStatus StatusCliente { get; set; }
    public TipoEntidade TipoEntidade { get; set; }
    public bool EmpresaAtiva { get; set; }
    public int FuncionariosAtivos { get; set; }
}

public enum TipoFaturaCanal
{
    /// <summary>Owner → revendedor (uso + Premium + serviços avulsos).</summary>
    Plataforma = 1,
    /// <summary>Revendedor → parceiro (funcionários ativos × preço do revendedor).</summary>
    Parceiro = 2,
    /// <summary>Owner → revendedor, cobrança imediata de compra no painel web (Premium, implantação do app próprio).</summary>
    Avulsa = 3
}

public enum TipoItemFaturaCanal
{
    EmpresasAtivas = 1,
    FuncionariosFaixa1 = 2,
    FuncionariosFaixa2 = 3,
    FuncionariosFaixa3 = 4,
    AjusteMinimo = 5,
    Premium = 6,
    PremiumIsento = 7,
    AppProprioImplantacao = 8,
    AppProprioMensal = 9,
    FuncionariosParceiro = 10,
    Isencao = 11
}

/// <summary>
/// Fatura entre canais. Visível ao pagador e ao recebedor (e à plataforma). Criada só pelo job/plataforma;
/// baixa/estorno só pelo recebedor (Owner via plataforma; revendedor para faturas dos seus parceiros).
/// </summary>
public class FaturaCanal : ICobrancaGateway
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public TipoFaturaCanal Tipo { get; set; }
    public Guid CanalPagadorId { get; set; }
    public Guid CanalRecebedorId { get; set; }
    public DateOnly Competencia { get; set; }
    public Guid? ApuracaoId { get; set; }
    public decimal Valor { get; set; }
    public DateOnly Vencimento { get; set; }
    public StatusFatura Status { get; set; }
    public DateOnly? PagaEm { get; set; }
    public decimal? ValorPago { get; set; }
    public string? FormaPagamento { get; set; }
    public string? Observacao { get; set; }
    public DateTimeOffset CriadaEm { get; set; }
    public DateTimeOffset? AtualizadaEm { get; set; }
    public List<ItemFaturaCanal> Itens { get; set; } = [];

    // ---- Cobrança no gateway (E3/E9) ----
    public StatusCobrancaGateway GatewayStatus { get; set; }
    public Guid? GatewayContaId { get; set; }
    public string? GatewayCobrancaId { get; set; }
    public string? GatewayLink { get; set; }
    public string? GatewayBoletoUrl { get; set; }
    public string? GatewayLinhaDigitavel { get; set; }
    public string? GatewayPixCopiaCola { get; set; }
    public string? GatewayPixQrCode { get; set; }
    public DateTimeOffset? GatewayUltimoEventoEm { get; set; }
    public string? GatewayErro { get; set; }

    public bool Vencida(DateOnly hoje) => Status == StatusFatura.Aberta && Vencimento < hoje;
}

public class ItemFaturaCanal
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid FaturaCanalId { get; set; }
    public TipoItemFaturaCanal Tipo { get; set; }
    public string Descricao { get; set; } = "";
    public decimal Quantidade { get; set; }
    public decimal ValorUnitario { get; set; }
    public decimal Valor { get; set; }
    public int Ordem { get; set; }
}

/// <summary>Assinatura Premium do revendedor (R$ 149/mês). Contratada só pelo painel web.</summary>
public class AssinaturaPremium
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CanalId { get; set; }
    public DateOnly Inicio { get; set; }
    public DateOnly? CanceladaEm { get; set; }
    public DateTimeOffset CriadaEm { get; set; }

    /// <summary>Esteve ativa em algum dia da competência (cobrança do mês cheio, sem pró-rata).</summary>
    public bool AtivaNaCompetencia(DateOnly competencia) =>
        Inicio <= competencia.AddMonths(1).AddDays(-1) && (CanceladaEm is null || CanceladaEm >= competencia);

    public bool AtivaEm(DateOnly dia) => Inicio <= dia && (CanceladaEm is null || CanceladaEm > dia);
}
