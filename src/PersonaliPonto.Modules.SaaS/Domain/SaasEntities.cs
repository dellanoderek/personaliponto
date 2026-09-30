using PersonaliPonto.Core.RepP.Domain;

namespace PersonaliPonto.Modules.SaaS.Domain;

public class Plano
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Nome { get; set; } = "";
    public decimal PrecoMensal { get; set; }
    public int LimiteFuncionarios { get; set; }
    public decimal PrecoFuncionarioExcedente { get; set; }
    public bool PermiteApp { get; set; } = true;
    public bool PermiteTerminalWeb { get; set; } = true;
    public bool Ativo { get; set; } = true;
}

/// <summary>Contrato comercial do cliente (um por tenant).</summary>
public class Assinatura : ITenantEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid? PlanoId { get; set; }
    public Plano? Plano { get; set; }
    public string RazaoSocial { get; set; } = "";
    public string Cnpj { get; set; } = "";
    public string? Telefone { get; set; }
    public string? Email { get; set; }
    /// <summary>Valor mensal negociado.</summary>
    public decimal ValorMensal { get; set; }
    public int FuncionariosContratados { get; set; }
    /// <summary>Custo mensal do cliente para a operação (licença de fornecedor, infraestrutura).</summary>
    public decimal CustoMensal { get; set; }
    /// <summary>Origem/fornecedor atual (ex.: EZPOINT, COTIPONTO, TEMPO CERTO).</summary>
    public string? Fornecedor { get; set; }
    public int DiaVencimento { get; set; } = 10;
    /// <summary>Dias após o vencimento até marcar como inadimplente.</summary>
    public int DiasTolerancia { get; set; } = 5;
    /// <summary>Dias após o vencimento até suspender o uso (quando bloqueio automático ativo).</summary>
    public int DiasParaBloqueio { get; set; } = 15;
    public bool BloqueioAutomatico { get; set; } = true;
    public DateOnly Inicio { get; set; }
    public DateOnly? CanceladaEm { get; set; }
    public string? MotivoCancelamento { get; set; }
    public string? Observacoes { get; set; }
}

public enum StatusFatura
{
    Aberta = 0,
    Paga = 1,
    /// <summary>Competência não cobrada (cortesia, período anterior à contratação etc.).</summary>
    NaoCobrada = 2,
    Cancelada = 3
}

public class Fatura : ITenantEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    /// <summary>Primeiro dia do mês de competência.</summary>
    public DateOnly Competencia { get; set; }
    public decimal Valor { get; set; }
    public DateOnly Vencimento { get; set; }
    public StatusFatura Status { get; set; }
    public DateOnly? PagaEm { get; set; }
    public decimal? ValorPago { get; set; }
    public string? FormaPagamento { get; set; }
    public string? Observacao { get; set; }
    public DateTimeOffset CriadaEm { get; set; }
    public DateTimeOffset? AtualizadaEm { get; set; }

    public bool Vencida(DateOnly hoje) => Status == StatusFatura.Aberta && Vencimento < hoje;
}

/// <summary>Registro de todo acesso da equipe da plataforma aos dados de um cliente.</summary>
public class AcessoSuporte
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid UsuarioId { get; set; }
    public string UsuarioNome { get; set; } = "";
    public string Motivo { get; set; } = "";
    public DateTimeOffset Inicio { get; set; }
    public DateTimeOffset? Fim { get; set; }
    public string? Ip { get; set; }
}
