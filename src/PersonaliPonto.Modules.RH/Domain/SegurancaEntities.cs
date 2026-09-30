using PersonaliPonto.Core.RepP.Domain;

namespace PersonaliPonto.Modules.RH.Domain;

/// <summary>Configurações de segurança da empresa (Fase 5). Um registro por tenant.</summary>
public class ConfiguracaoSeguranca : TenantEntity
{
    /// <summary>Exige que o aparelho do funcionário seja autorizado pelo RH antes de marcar pelo app.</summary>
    public bool ExigirDispositivoAutorizado { get; set; }
    /// <summary>
    /// Solicita foto no momento da marcação. Só deve ser ativada após avaliação de LGPD (finalidade,
    /// base legal e comunicação aos titulares). Desativada por padrão.
    /// </summary>
    public bool FotoNaMarcacao { get; set; }
    /// <summary>Prazo de retenção das fotos; após o prazo o arquivo é excluído automaticamente.</summary>
    public int RetencaoFotoDias { get; set; } = 90;
    /// <summary>Registro do aceite da avaliação de LGPD que habilitou a foto (quem/quando).</summary>
    public string? AvaliacaoLgpdResponsavel { get; set; }
    public DateTimeOffset? AvaliacaoLgpdEm { get; set; }
}

public enum StatusDispositivo
{
    Pendente = 0,
    Autorizado = 1,
    Revogado = 2
}

/// <summary>Aparelho usado pelo funcionário no aplicativo (device binding).</summary>
public class DispositivoFuncionario : TenantEntity
{
    public Guid FuncionarioId { get; set; }
    public string DispositivoId { get; set; } = "";
    public StatusDispositivo Status { get; set; }
    public DateTimeOffset PrimeiroUso { get; set; }
    public DateTimeOffset UltimoUso { get; set; }
    public Guid? AlteradoPor { get; set; }
}

/// <summary>Foto associada a uma marcação, com prazo de retenção.</summary>
public class FotoMarcacao : TenantEntity
{
    public Guid RegistroRepId { get; set; }
    public Guid? ArquivoId { get; set; }
    public DateTimeOffset CriadaEm { get; set; }
    public DateTimeOffset ExpiraEm { get; set; }
    public DateTimeOffset? ExcluidaEm { get; set; }
}
