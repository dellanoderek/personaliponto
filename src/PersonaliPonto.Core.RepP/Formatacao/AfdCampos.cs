using System.Globalization;
using System.Text;

namespace PersonaliPonto.Core.RepP.Formatacao;

/// <summary>Formatação de campos conforme Anexo V / leiaute AFD vigente no gov.br.</summary>
public static class AfdCampos
{
    public static readonly Encoding Latin1 = Encoding.Latin1;
    public const string FimDeLinha = "\r\n";
    public const string TextoAssinatura = "ASSINATURA_DIGITAL_EM_ARQUIVO_P7S";

    /// <summary>Campo alfanumérico: alinhado à esquerda, completado com espaços, truncado ao tamanho.</summary>
    public static string A(string? valor, int tamanho)
    {
        var v = Sanitizar(valor ?? "");
        return v.Length >= tamanho ? v[..tamanho] : v.PadRight(tamanho, ' ');
    }

    /// <summary>Número com zeros à esquerda (NSR, contadores do trailer).</summary>
    public static string N(long valor, int tamanho)
    {
        if (valor < 0) throw new ArgumentOutOfRangeException(nameof(valor));
        var s = valor.ToString(CultureInfo.InvariantCulture);
        if (s.Length > tamanho) throw new InvalidOperationException($"Valor {s} excede {tamanho} posições.");
        return s.PadLeft(tamanho, '0');
    }

    /// <summary>
    /// Campo "N" opcional/identificador: iniciado pela esquerda e completado com espaços (regra geral do
    /// Anexo V, item 7; FAQ MTE nº 49 para o registro INPI). Vazio =&gt; espaços.
    /// </summary>
    public static string NEsquerda(string? valor, int tamanho)
    {
        var v = Documentos.SomenteDigitos(valor);
        if (v.Length > tamanho) throw new InvalidOperationException($"Valor {v} excede {tamanho} posições.");
        return v.PadRight(tamanho, ' ');
    }

    /// <summary>CPF em 12 posições com zero à esquerda (FAQ MTE nº 50, opção "a").</summary>
    public static string Cpf12(string cpf)
    {
        var d = Documentos.Normalizar(cpf);
        if (d.Length != 11) throw new InvalidOperationException("CPF deve ter 11 dígitos.");
        return "0" + d;
    }

    /// <summary>CPF em 11 posições (campos de responsável nos registros 4 e 5).</summary>
    public static string Cpf11(string? cpf)
    {
        var d = Documentos.Normalizar(cpf);
        return d.Length == 11 ? d : new string(' ', 11);
    }

    /// <summary>CPF do responsável no registro 2 (14 posições): iniciado pela esquerda, espaços à direita.</summary>
    public static string Cpf14(string? cpf) => NEsquerda(cpf, 14);

    /// <summary>Data "AAAA-MM-dd".</summary>
    public static string D(DateOnly data) => data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Data e hora "AAAA-MM-ddThh:mm:00ZZZZZ" no fuso informado (segundos fixos em 00).</summary>
    public static string DH(DateTimeOffset instante, int offsetMinutos)
    {
        var local = instante.ToOffset(TimeSpan.FromMinutes(offsetMinutos));
        var sinal = offsetMinutos < 0 ? '-' : '+';
        var abs = Math.Abs(offsetMinutos);
        return string.Create(CultureInfo.InvariantCulture,
            $"{local:yyyy-MM-dd}T{local:HH:mm}:00{sinal}{abs / 60:00}{abs % 60:00}");
    }

    /// <summary>Remove caracteres fora do ISO-8859-1 e quebras de linha.</summary>
    public static string Sanitizar(string valor)
    {
        var sb = new StringBuilder(valor.Length);
        foreach (var c in valor)
        {
            if (c is '\r' or '\n' or '\t') sb.Append(' ');
            else if (c <= 0xFF && !char.IsControl(c)) sb.Append(c);
            else sb.Append('?');
        }
        return sb.ToString();
    }

    public static string ComCrc(string conteudoSemCrc) =>
        conteudoSemCrc + Crc16Kermit.Hex(Latin1.GetBytes(conteudoSemCrc));
}
