using PersonaliPonto.Core.RepP.Domain;

namespace PersonaliPonto.Core.RepP.Services;

/// <summary>Identificação do REP-P e do PTRP (fabricante/desenvolvedor). Configurado em "RepP".</summary>
public sealed class RepPOptions
{
    /// <summary>Número de registro do programa no INPI (art. 91). Apenas os caracteres numéricos vão ao AFD.</summary>
    public string NumeroRegistroInpi { get; set; } = "";
    public TipoIdentificador TipoIdentificadorDesenvolvedor { get; set; } = TipoIdentificador.Cnpj;
    public string IdentificadorDesenvolvedor { get; set; } = "";
    public string RazaoSocialDesenvolvedor { get; set; } = "";
    public string EmailDesenvolvedor { get; set; } = "";
    public string NomePrograma { get; set; } = "PersonaliPonto";
    public string VersaoPrograma { get; set; } = "1.0.0";
    /// <summary>Idade máxima aceita para uma marcação off-line (a partir da âncora de hora).</summary>
    public int OfflineMaximoDias { get; set; } = 7;
    public int PinMaximoTentativas { get; set; } = 5;
    public int PinBloqueioMinutos { get; set; } = 15;
    /// <summary>Janela mínima legal de extração de comprovantes (art. 80, III).</summary>
    public int ComprovanteJanelaHoras { get; set; } = 48;
}
