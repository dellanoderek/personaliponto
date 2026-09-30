using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Core.RepP.Domain;

public interface ITenantEntity
{
    Guid TenantId { get; set; }
}

/// <summary>
/// Entidade imutável: nunca pode ser alterada nem excluída (ARP — Anexo IX, item 7).
/// Garantido em duas camadas: SaveChanges rejeita e o banco possui trigger que bloqueia UPDATE/DELETE.
/// </summary>
public interface IImmutableEntity;

public abstract class TenantEntity : ITenantEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
}

public enum TenantStatus
{
    Teste = 0,
    Ativo = 1,
    Inadimplente = 2,
    Suspenso = 3,
    Cancelado = 4
}

/// <summary>Cliente do SaaS. O próprio Id é o TenantId.</summary>
public class Tenant
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Nome { get; set; } = "";
    public string Slug { get; set; } = "";
    public TenantStatus Status { get; set; } = TenantStatus.Teste;
    public DateTimeOffset CriadoEm { get; set; }
    public DateTimeOffset? TesteAte { get; set; }
    public string? EmailContato { get; set; }
    public string? TelefoneContato { get; set; }
}

public enum TipoIdentificador
{
    Cnpj = 1,
    Cpf = 2
}

/// <summary>Empresa (empregador). Pode possuir vários estabelecimentos.</summary>
public class Empregador : TenantEntity
{
    public string RazaoSocial { get; set; } = "";
    public string? NomeFantasia { get; set; }
    public TipoIdentificador TipoIdentificador { get; set; } = TipoIdentificador.Cnpj;
    public string Identificador { get; set; } = "";
    public bool Ativo { get; set; } = true;
}

/// <summary>
/// Estabelecimento (CNPJ de 14 posições ou CPF). Cada estabelecimento tem sua própria sequência de NSR
/// (Anexo IX, OBS do item 6; FAQ MTE nº 41).
/// </summary>
public class Estabelecimento : TenantEntity
{
    public Guid EmpregadorId { get; set; }
    public Empregador? Empregador { get; set; }
    public string Nome { get; set; } = "";
    public TipoIdentificador TipoIdentificador { get; set; } = TipoIdentificador.Cnpj;
    public string Identificador { get; set; } = "";
    public string? CnoCaepf { get; set; }
    /// <summary>Local de prestação de serviços (100 posições no AFD).</summary>
    public string LocalPrestacao { get; set; } = "";
    public string FusoHorario { get; set; } = "America/Sao_Paulo";
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? RaioCercaMetros { get; set; }
    public ModoCerca ModoCerca { get; set; } = ModoCerca.Desativada;
    public bool Ativo { get; set; } = true;
}

public enum ModoCerca
{
    Desativada = 0,
    /// <summary>Registra a marcação e sinaliza para o RH quando fora da cerca.</summary>
    Sinalizar = 1,
    /// <summary>Impede a marcação fora da cerca.</summary>
    Bloquear = 2
}

public class Funcionario : TenantEntity
{
    public Guid EstabelecimentoId { get; set; }
    public Estabelecimento? Estabelecimento { get; set; }
    public string Nome { get; set; } = "";
    public string Cpf { get; set; } = "";
    public string Matricula { get; set; } = "";
    public string? MatriculaEsocial { get; set; }
    public DateOnly DataAdmissao { get; set; }
    public DateOnly? DataDemissao { get; set; }
    public string? Cargo { get; set; }
    public string? Email { get; set; }
    public Guid? JornadaId { get; set; }
    public Jornada? Jornada { get; set; }
    public Guid? GestorId { get; set; }
    public string? PinHash { get; set; }
    public int PinTentativasFalhas { get; set; }
    public DateTimeOffset? PinBloqueadoAte { get; set; }
    public bool PermiteMarcacaoApp { get; set; } = true;
    public bool PermiteMarcacaoOffline { get; set; } = true;
    public ModoCerca? ModoCercaFuncionario { get; set; }
    public bool Ativo { get; set; } = true;
}

public enum TipoJornada
{
    Fixa = 0,
    Semanal5x2 = 1,
    Semanal6x1 = 2,
    Escala12x36 = 3,
    Personalizada = 4
}

/// <summary>Horário contratual (AEJ registro tipo 04).</summary>
public class Jornada : TenantEntity
{
    public string Codigo { get; set; } = "";
    public string Nome { get; set; } = "";
    public TipoJornada Tipo { get; set; }
    /// <summary>Para escalas cíclicas (ex.: 12x36 = 2 dias). Nulo = ciclo semanal (0 = domingo).</summary>
    public int? CicloDias { get; set; }
    public DateOnly? DataReferenciaCiclo { get; set; }
    public int ToleranciaMinutos { get; set; } = 5;
    public int ToleranciaDiariaMinutos { get; set; } = 10;
    public List<HorarioDia> Horarios { get; set; } = [];
    public bool Ativo { get; set; } = true;
}

public class HorarioDia
{
    /// <summary>Dia da semana (0=domingo..6=sábado) ou índice no ciclo.</summary>
    public int Indice { get; set; }
    public List<Periodo> Periodos { get; set; } = [];
}

public class Periodo
{
    public TimeOnly Entrada { get; set; }
    public TimeOnly Saida { get; set; }
}

public enum TipoRegistroRep
{
    InclusaoAlteracaoEmpresa = 2,
    AjusteRelogio = 4,
    InclusaoAlteracaoExclusaoEmpregado = 5,
    EventoSensivel = 6,
    MarcacaoRepP = 7
}

/// <summary>
/// Registro do Armazenamento de Registro de Ponto (ARP). Imutável.
/// Um único registro por NSR de estabelecimento, espelhando exatamente os tipos do AFD (Anexo V).
/// </summary>
public class RegistroRep : IImmutableEntity, ITenantEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid EstabelecimentoId { get; set; }
    public long Nsr { get; set; }
    public TipoRegistroRep Tipo { get; set; }

    /// <summary>Data/hora de gravação (UTC) e fuso (minutos) em que foi gravado.</summary>
    public DateTimeOffset DataHoraGravacao { get; set; }
    public int OffsetGravacaoMinutos { get; set; }

    // Tipo 7 — marcação REP-P
    public Guid? FuncionarioId { get; set; }
    public string? Cpf { get; set; }
    public DateTimeOffset? DataHoraMarcacao { get; set; }
    public int? OffsetMarcacaoMinutos { get; set; }
    public Coletor? Coletor { get; set; }
    public bool? Offline { get; set; }
    public string? Hash { get; set; }
    public Guid? ClientId { get; set; }

    // Tipo 2 — empresa
    public TipoIdentificador? TipoIdentificadorEmpregador { get; set; }
    public string? IdentificadorEmpregador { get; set; }
    public string? CnoCaepf { get; set; }
    public string? RazaoSocial { get; set; }
    public string? LocalPrestacao { get; set; }

    // Tipos 2, 4, 5 — responsável
    public string? CpfResponsavel { get; set; }

    // Tipo 4 — ajuste do relógio
    public DateTimeOffset? DataHoraAntesAjuste { get; set; }
    public DateTimeOffset? DataHoraAjustada { get; set; }

    // Tipo 5 — empregado
    public char? TipoOperacao { get; set; }
    public string? NomeEmpregado { get; set; }
    public string? DemaisDados { get; set; }

    // Tipo 6 — evento sensível
    public int? TipoEvento { get; set; }
}

/// <summary>Contexto operacional da marcação (fora do ARP): geolocalização, dispositivo, IP, cerca.</summary>
public class MarcacaoContexto : TenantEntity, IImmutableEntity
{
    public Guid RegistroRepId { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool? DentroCerca { get; set; }
    public string? DispositivoId { get; set; }
    public string? Ip { get; set; }
    public Guid? TerminalId { get; set; }
    public DateTimeOffset? HorarioInformadoColetor { get; set; }
}

public class NsrContador
{
    public Guid EstabelecimentoId { get; set; }
    public Guid TenantId { get; set; }
    public long UltimoNsr { get; set; }
    public string? UltimoHashTipo7 { get; set; }
}

public enum TipoTratamento
{
    /// <summary>Marcação incluída manualmente (fonteMarc "I").</summary>
    Inclusao = 1,
    /// <summary>Marcação desconsiderada (tpMarc "D").</summary>
    Desconsideracao = 2,
    /// <summary>Marcação pré-assinalada de intervalo (fonteMarc "P").</summary>
    PreAssinalada = 3,
    /// <summary>Revoga um tratamento anterior (o original permanece no histórico).</summary>
    Revogacao = 4
}

/// <summary>
/// Tratamento de ponto (PTRP — art. 82). Nunca altera a marcação original; apenas acrescenta
/// informação vinculada. Imutável: correções geram novo tratamento (revogação).
/// </summary>
public class Tratamento : TenantEntity, IImmutableEntity
{
    public Guid FuncionarioId { get; set; }
    public TipoTratamento Tipo { get; set; }
    public Guid? RegistroRepId { get; set; }
    public Guid? TratamentoRevogadoId { get; set; }
    public DateTimeOffset? DataHora { get; set; }
    public int? OffsetMinutos { get; set; }
    public string Motivo { get; set; } = "";
    public Guid? UsuarioId { get; set; }
    public string? UsuarioNome { get; set; }
    public Guid? SolicitacaoId { get; set; }
    public DateTimeOffset CriadoEm { get; set; }
}

public class AuditLog : IImmutableEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid? TenantId { get; set; }
    public Guid? UsuarioId { get; set; }
    public string? UsuarioNome { get; set; }
    public string Acao { get; set; } = "";
    public string Entidade { get; set; } = "";
    public string? EntidadeId { get; set; }
    public string? Dados { get; set; }
    public string? Ip { get; set; }
    public DateTimeOffset Em { get; set; }
}

public class Usuario
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>Nulo para usuários da plataforma (Super Admin/Suporte).</summary>
    public Guid? TenantId { get; set; }
    public string Email { get; set; } = "";
    public string Nome { get; set; } = "";
    public string? Cpf { get; set; }
    public string SenhaHash { get; set; } = "";
    public string Papel { get; set; } = Roles.Funcionario;
    public Guid? FuncionarioId { get; set; }
    public bool Ativo { get; set; } = true;
    public bool DeveTrocarSenha { get; set; }
    public int TentativasFalhas { get; set; }
    public DateTimeOffset? BloqueadoAte { get; set; }
    /// <summary>Gancho para MFA (Fase 5): segredo TOTP protegido.</summary>
    public bool MfaHabilitado { get; set; }
    public string? MfaSegredo { get; set; }
    public DateTimeOffset CriadoEm { get; set; }
    public DateTimeOffset? UltimoLogin { get; set; }
}

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UsuarioId { get; set; }
    public Guid? TenantId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset CriadoEm { get; set; }
    public DateTimeOffset ExpiraEm { get; set; }
    public DateTimeOffset? RevogadoEm { get; set; }
    public Guid? SubstituidoPor { get; set; }
}

/// <summary>Navegador ativado como terminal de marcação (sem login persistente de usuário).</summary>
public class TerminalWeb : TenantEntity
{
    public Guid EstabelecimentoId { get; set; }
    public string Nome { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public bool Ativo { get; set; } = true;
    public DateTimeOffset CriadoEm { get; set; }
    public DateTimeOffset? UltimoUso { get; set; }
}

/// <summary>Âncora de hora emitida pelo servidor para validar marcações off-line.</summary>
public class AncoraHora : TenantEntity
{
    public Guid? FuncionarioId { get; set; }
    public Guid? TerminalId { get; set; }
    public DateTimeOffset HoraServidor { get; set; }
}

/// <summary>Outbox transacional (decisão 8): processamento assíncrono via banco.</summary>
public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid? TenantId { get; set; }
    public string Tipo { get; set; } = "";
    public string Payload { get; set; } = "";
    public DateTimeOffset CriadoEm { get; set; }
    public DateTimeOffset? ProcessadoEm { get; set; }
    public int Tentativas { get; set; }
    public DateTimeOffset? ProximaTentativa { get; set; }
    public string? Erro { get; set; }
}

/// <summary>Arquivo armazenado no Supabase Storage.</summary>
public class ArquivoArmazenado : TenantEntity
{
    public string Bucket { get; set; } = "";
    public string Caminho { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Tamanho { get; set; }
    public string NomeOriginal { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public DateTimeOffset CriadoEm { get; set; }
    public Guid? CriadoPor { get; set; }
}
