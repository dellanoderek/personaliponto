namespace PersonaliPonto.Modules.SaaS.Domain;

/// <summary>
/// Marca (white-label) de um canal. Revendedor: tokens completos (cores, fonte, menu, painel, textos) e imagens.
/// Parceiro: herda do revendedor e só pode sobrescrever nome e logos quando o revendedor permitir
/// (<see cref="PermitirParceiroSobrescrever"/>). Editável apenas pelo próprio canal (e pela plataforma) — RLS.
/// </summary>
public class MarcaCanal
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CanalId { get; set; }
    /// <summary>Versão publicada (incrementa a cada publicação; o app usa para invalidar o cache).</summary>
    public int Versao { get; set; }
    /// <summary>Tokens publicados (JSON <c>TokensMarca</c>).</summary>
    public string TokensJson { get; set; } = "{}";
    /// <summary>Somente revendedor: parceiros podem trocar nome e logos.</summary>
    public bool PermitirParceiroSobrescrever { get; set; }

    // Imagens: caminhos no IFileStorage (bucket "marca"); nulo = padrão.
    public string? LogoCaminho { get; set; }
    public string? LogoEscuraCaminho { get; set; }
    public string? FaviconCaminho { get; set; }
    public string? ImagemLoginCaminho { get; set; }

    public DateTimeOffset AtualizadaEm { get; set; }
    public Guid? AtualizadaPor { get; set; }
}

public enum TipoDominio
{
    /// <summary>Domínio próprio do canal via CNAME (ex.: ponto.tempocerto.com.br).</summary>
    Proprio = 1,
    /// <summary>Subdomínio adicional em personaliponto.com.br além do slug.</summary>
    Alias = 2
}

public enum StatusDominio
{
    /// <summary>Cadastrado, aguardando verificação de DNS/TLS (E8). Não resolve.</summary>
    Pendente = 0,
    Ativo = 1,
    Inativo = 2
}

/// <summary>
/// Host próprio que resolve para a marca de um canal. Cadastro manual pela plataforma (verificação de DNS e TLS
/// automático ficam para a E8). O subdomínio {slug}.personaliponto.com.br é automático e não precisa de registro.
/// </summary>
public class DominioCanal
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>Host em minúsculas, sem porta.</summary>
    public string Host { get; set; } = "";
    public Guid CanalId { get; set; }
    public TipoDominio Tipo { get; set; } = TipoDominio.Proprio;
    public StatusDominio Status { get; set; } = StatusDominio.Pendente;
    public string? Observacao { get; set; }
    public DateTimeOffset CriadoEm { get; set; }
    public DateTimeOffset? AtivadoEm { get; set; }
}
