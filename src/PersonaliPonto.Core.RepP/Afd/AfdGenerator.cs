using System.Security.Cryptography;
using System.Text;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Formatacao;
using static PersonaliPonto.Core.RepP.Formatacao.AfdCampos;

namespace PersonaliPonto.Core.RepP.Afd;

public sealed record AfdCabecalho(
    TipoIdentificador TipoIdentificadorEmpregador,
    string IdentificadorEmpregador,
    string? CnoCaepf,
    string RazaoSocial,
    string NumeroRegistroInpi,
    DateOnly DataInicial,
    DateOnly DataFinal,
    DateTimeOffset GeradoEm,
    int OffsetMinutos,
    TipoIdentificador TipoIdentificadorDesenvolvedor,
    string IdentificadorDesenvolvedor);

public sealed record AfdArquivo(string NomeArquivo, byte[] Conteudo, int TotalRegistros);

/// <summary>
/// Gerador do Arquivo Fonte de Dados (Anexo V da Portaria MTP 671/2021, leiaute vigente "004").
/// Texto ISO-8859-1, linhas terminadas em CR LF, ordenado por NSR, sem linhas em branco.
/// </summary>
public static class AfdGenerator
{
    public const string VersaoLeiaute = "004";

    public const int TamanhoTipo1 = 302;
    public const int TamanhoTipo2 = 331;
    public const int TamanhoTipo4 = 73;
    public const int TamanhoTipo5 = 118;
    public const int TamanhoTipo6 = 36;
    public const int TamanhoTipo7 = 137;
    public const int TamanhoTipo9 = 64;
    public const int TamanhoAssinatura = 100;

    public static string NomeArquivo(string numeroInpi, string identificadorEmpregador) =>
        $"AFD{Documentos.SomenteDigitos(numeroInpi)}{Documentos.Normalizar(identificadorEmpregador)}REP_P.txt";

    public static AfdArquivo Gerar(AfdCabecalho cab, IEnumerable<RegistroRep> registros)
    {
        var ordenados = registros.OrderBy(r => r.Nsr).ToList();
        var sb = new StringBuilder();
        var cont = new Dictionary<TipoRegistroRep, int>();

        sb.Append(Cabecalho(cab)).Append(FimDeLinha);
        foreach (var r in ordenados)
        {
            sb.Append(Linha(r)).Append(FimDeLinha);
            cont[r.Tipo] = cont.GetValueOrDefault(r.Tipo) + 1;
        }

        int Qt(TipoRegistroRep t) => cont.GetValueOrDefault(t);
        var trailer = N(999999999, 9)
                      + N(Qt(TipoRegistroRep.InclusaoAlteracaoEmpresa), 9)
                      + N(0, 9) // tipo 3 não se aplica a REP-P
                      + N(Qt(TipoRegistroRep.AjusteRelogio), 9)
                      + N(Qt(TipoRegistroRep.InclusaoAlteracaoExclusaoEmpregado), 9)
                      + N(Qt(TipoRegistroRep.EventoSensivel), 9)
                      + N(Qt(TipoRegistroRep.MarcacaoRepP), 9)
                      + "9";
        Garantir(trailer, TamanhoTipo9, "9");
        sb.Append(trailer).Append(FimDeLinha);
        sb.Append(A(TextoAssinatura, TamanhoAssinatura)).Append(FimDeLinha);

        var bytes = Latin1.GetBytes(sb.ToString());
        return new AfdArquivo(NomeArquivo(cab.NumeroRegistroInpi, cab.IdentificadorEmpregador), bytes, ordenados.Count);
    }

    public static string Cabecalho(AfdCabecalho c)
    {
        var s = "000000000"
                + "1"
                + ((int)c.TipoIdentificadorEmpregador).ToString()
                + A(Documentos.Normalizar(c.IdentificadorEmpregador), 14)
                + NEsquerda(c.CnoCaepf, 14)
                + A(c.RazaoSocial, 150)
                + NEsquerda(c.NumeroRegistroInpi, 17)
                + D(c.DataInicial)
                + D(c.DataFinal)
                + DH(c.GeradoEm, c.OffsetMinutos)
                + VersaoLeiaute
                + ((int)c.TipoIdentificadorDesenvolvedor).ToString()
                + A(Documentos.Normalizar(c.IdentificadorDesenvolvedor), 14)
                + A("", 30);
        s = ComCrc(s);
        Garantir(s, TamanhoTipo1, "1");
        return s;
    }

    public static string Linha(RegistroRep r) => r.Tipo switch
    {
        TipoRegistroRep.InclusaoAlteracaoEmpresa => Tipo2(r),
        TipoRegistroRep.AjusteRelogio => Tipo4(r),
        TipoRegistroRep.InclusaoAlteracaoExclusaoEmpregado => Tipo5(r),
        TipoRegistroRep.EventoSensivel => Tipo6(r),
        TipoRegistroRep.MarcacaoRepP => Tipo7(r),
        _ => throw new InvalidOperationException($"Tipo de registro {r.Tipo} não suportado no AFD de REP-P.")
    };

    private static string Tipo2(RegistroRep r)
    {
        var s = N(r.Nsr, 9)
                + "2"
                + DH(r.DataHoraGravacao, r.OffsetGravacaoMinutos)
                + Cpf14(r.CpfResponsavel)
                + ((int)(r.TipoIdentificadorEmpregador ?? TipoIdentificador.Cnpj)).ToString()
                + A(Documentos.Normalizar(r.IdentificadorEmpregador), 14)
                + NEsquerda(r.CnoCaepf, 14)
                + A(r.RazaoSocial, 150)
                + A(r.LocalPrestacao, 100);
        s = ComCrc(s);
        Garantir(s, TamanhoTipo2, "2");
        return s;
    }

    private static string Tipo4(RegistroRep r)
    {
        var s = N(r.Nsr, 9)
                + "4"
                + DH(r.DataHoraAntesAjuste!.Value, r.OffsetGravacaoMinutos)
                + DH(r.DataHoraAjustada!.Value, r.OffsetGravacaoMinutos)
                + Cpf11(r.CpfResponsavel);
        s = ComCrc(s);
        Garantir(s, TamanhoTipo4, "4");
        return s;
    }

    private static string Tipo5(RegistroRep r)
    {
        var s = N(r.Nsr, 9)
                + "5"
                + DH(r.DataHoraGravacao, r.OffsetGravacaoMinutos)
                + (r.TipoOperacao ?? 'I')
                + Cpf12(r.Cpf!)
                + A(r.NomeEmpregado, 52)
                + A(r.DemaisDados, 4)
                + Cpf11(r.CpfResponsavel);
        s = ComCrc(s);
        Garantir(s, TamanhoTipo5, "5");
        return s;
    }

    private static string Tipo6(RegistroRep r)
    {
        var s = N(r.Nsr, 9)
                + "6"
                + DH(r.DataHoraGravacao, r.OffsetGravacaoMinutos)
                + N(r.TipoEvento ?? 0, 2);
        Garantir(s, TamanhoTipo6, "6");
        return s;
    }

    private static string Tipo7(RegistroRep r)
    {
        var s = CamposHashTipo7(r) + r.Hash;
        Garantir(s, TamanhoTipo7, "7");
        return s;
    }

    /// <summary>Campos 1 a 7 do registro tipo 7, na forma exata em que aparecem no AFD.</summary>
    public static string CamposHashTipo7(RegistroRep r) =>
        N(r.Nsr, 9)
        + "7"
        + DH(r.DataHoraMarcacao!.Value, r.OffsetMarcacaoMinutos!.Value)
        + Cpf12(r.Cpf!)
        + DH(r.DataHoraGravacao, r.OffsetGravacaoMinutos)
        + N((int)r.Coletor!.Value, 2)
        + (r.Offline == true ? "1" : "0");

    /// <summary>
    /// SHA-256 sobre os campos 1 a 7 concatenados e o hash do registro tipo 7 anterior do mesmo
    /// estabelecimento, quando existir. Representação: 64 caracteres hexadecimais maiúsculos.
    /// </summary>
    public static string CalcularHashTipo7(RegistroRep r, string? hashAnterior)
    {
        var entrada = CamposHashTipo7(r) + (hashAnterior ?? "");
        return Convert.ToHexString(SHA256.HashData(Latin1.GetBytes(entrada)));
    }

    private static void Garantir(string linha, int tamanho, string tipo)
    {
        if (linha.Length != tamanho)
            throw new InvalidOperationException($"Registro tipo {tipo} com {linha.Length} posições; esperado {tamanho}.");
    }
}
