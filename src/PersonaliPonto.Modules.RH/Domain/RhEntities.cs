using PersonaliPonto.Core.RepP.Domain;

namespace PersonaliPonto.Modules.RH.Domain;

public enum StatusSolicitacao
{
    Pendente = 0,
    Aprovada = 1,
    Recusada = 2,
    Cancelada = 3
}

public enum TipoSolicitacao
{
    /// <summary>Incluir marcação esquecida.</summary>
    Inclusao = 1,
    /// <summary>Desconsiderar marcação indevida.</summary>
    Desconsideracao = 2
}

/// <summary>Solicitação de correção de ponto (funcionário → RH), com decisão auditada.</summary>
public class SolicitacaoCorrecao : TenantEntity
{
    public Guid FuncionarioId { get; set; }
    public TipoSolicitacao Tipo { get; set; } = TipoSolicitacao.Inclusao;
    public DateOnly Data { get; set; }
    public TimeOnly? Horario { get; set; }
    public Guid? RegistroRepId { get; set; }
    public string Motivo { get; set; } = "";
    public StatusSolicitacao Status { get; set; }
    public string? RespostaRh { get; set; }
    public Guid? RespondidoPor { get; set; }
    public DateTimeOffset? RespondidoEm { get; set; }
    public Guid? TratamentoId { get; set; }
    public DateTimeOffset CriadaEm { get; set; }
}

public enum TipoAtestado
{
    AtestadoMedico = 1,
    DeclaracaoComparecimento = 2,
    Justificativa = 3,
    LicencaLegal = 4
}

public class Atestado : TenantEntity
{
    public Guid FuncionarioId { get; set; }
    public TipoAtestado Tipo { get; set; }
    public DateOnly DataInicio { get; set; }
    public DateOnly DataFim { get; set; }
    /// <summary>Minutos abonados em ausência parcial (ex.: declaração de 2h). Nulo = dia inteiro.</summary>
    public int? MinutosParciais { get; set; }
    public string? Observacao { get; set; }
    public Guid? ArquivoId { get; set; }
    public StatusSolicitacao Status { get; set; }
    public string? ParecerRh { get; set; }
    public Guid? AnalisadoPor { get; set; }
    public DateTimeOffset? AnalisadoEm { get; set; }
    public DateTimeOffset CriadoEm { get; set; }
    public Guid? CriadoPor { get; set; }
}

public enum TipoLancamentoBancoHoras
{
    CreditoApuracao = 1,
    DebitoApuracao = 2,
    Compensacao = 3,
    AjusteManual = 4,
    Pagamento = 5,
    Estorno = 6
}

/// <summary>Lançamento no banco de horas. Livro-razão imutável: saldo = soma; correções por estorno.</summary>
public class BancoHorasLancamento : TenantEntity, IImmutableEntity
{
    public Guid FuncionarioId { get; set; }
    public DateOnly Data { get; set; }
    public int Minutos { get; set; }
    public TipoLancamentoBancoHoras Tipo { get; set; }
    public string Descricao { get; set; } = "";
    public Guid? FechamentoId { get; set; }
    public Guid? EstornaLancamentoId { get; set; }
    public Guid? CriadoPor { get; set; }
    public string? CriadoPorNome { get; set; }
    public DateTimeOffset CriadoEm { get; set; }
}

public enum AbrangenciaFeriado
{
    Nacional = 1,
    Estadual = 2,
    Municipal = 3,
    Empresa = 4
}

public class Feriado : TenantEntity
{
    /// <summary>Nulo = vale para todos os estabelecimentos do tenant.</summary>
    public Guid? EstabelecimentoId { get; set; }
    public DateOnly Data { get; set; }
    public string Descricao { get; set; } = "";
    public AbrangenciaFeriado Abrangencia { get; set; }
}

/// <summary>Jornada vigente de um funcionário por período (troca de escala sem perder histórico).</summary>
public class EscalaFuncionario : TenantEntity
{
    public Guid FuncionarioId { get; set; }
    public Guid JornadaId { get; set; }
    public DateOnly Inicio { get; set; }
    public DateOnly? Fim { get; set; }
    public DateTimeOffset CriadoEm { get; set; }
}

public enum StatusFechamento
{
    Fechado = 1,
    Reaberto = 2
}

/// <summary>Fechamento mensal de um funcionário: consolida totais e lança o banco de horas.</summary>
public class FechamentoPeriodo : TenantEntity
{
    public Guid FuncionarioId { get; set; }
    public int Ano { get; set; }
    public int Mes { get; set; }
    public StatusFechamento Status { get; set; }
    public int MinutosTrabalhados { get; set; }
    public int MinutosExtras { get; set; }
    public int MinutosAtrasos { get; set; }
    public int MinutosFaltas { get; set; }
    public int DiasInconsistentes { get; set; }
    public Guid? FechadoPor { get; set; }
    public DateTimeOffset FechadoEm { get; set; }
    public DateTimeOffset? ReabertoEm { get; set; }
}

/// <summary>Política de banco de horas por tenant.</summary>
public class PoliticaBancoHoras : TenantEntity
{
    public bool Ativo { get; set; } = true;
    /// <summary>Extras vão para o banco (true) ou são pagas (false).</summary>
    public bool ExtrasParaBanco { get; set; } = true;
    /// <summary>Atrasos e faltas não justificadas debitam do banco.</summary>
    public bool DescontarAtrasos { get; set; } = true;
    public int ValidadeMeses { get; set; } = 6;
}

/// <summary>Ciência/assinatura eletrônica do espelho mensal pelo funcionário. Imutável.</summary>
public class AssinaturaEspelho : TenantEntity, IImmutableEntity
{
    public Guid FuncionarioId { get; set; }
    public int Ano { get; set; }
    public int Mes { get; set; }
    public bool Concorda { get; set; }
    public string? Observacao { get; set; }
    /// <summary>SHA-256 do conteúdo do espelho no momento da assinatura (prova de integridade).</summary>
    public string HashEspelho { get; set; } = "";
    public string? Ip { get; set; }
    public DateTimeOffset AssinadoEm { get; set; }
}
