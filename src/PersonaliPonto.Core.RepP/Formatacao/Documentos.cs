namespace PersonaliPonto.Core.RepP.Formatacao;

/// <summary>Validação de CPF e CNPJ (inclusive CNPJ alfanumérico — IN RFB nº 2.229/2024).</summary>
public static class Documentos
{
    public static string Normalizar(string? valor) =>
        new string((valor ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public static string SomenteDigitos(string? valor) =>
        new string((valor ?? "").Where(char.IsAsciiDigit).ToArray());

    public static bool CpfValido(string? cpf)
    {
        var d = Normalizar(cpf);
        if (d.Length != 11 || !d.All(char.IsDigit) || d.Distinct().Count() == 1) return false;
        return DigitosCpf(d[..9]) == d[9..];
    }

    private static string DigitosCpf(string nove)
    {
        int Dv(string s, int peso)
        {
            var soma = 0;
            foreach (var c in s) soma += (c - '0') * peso--;
            var r = soma % 11;
            return r < 2 ? 0 : 11 - r;
        }
        var d1 = Dv(nove, 10);
        var d2 = Dv(nove + d1, 11);
        return $"{d1}{d2}";
    }

    /// <summary>CNPJ numérico ou alfanumérico: 12 posições alfanuméricas + 2 dígitos verificadores numéricos.</summary>
    public static bool CnpjValido(string? cnpj)
    {
        var d = Normalizar(cnpj);
        if (d.Length != 14) return false;
        if (!d[..12].All(c => char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c))) return false;
        if (!char.IsAsciiDigit(d[12]) || !char.IsAsciiDigit(d[13])) return false;
        if (d.All(char.IsDigit) && d.Distinct().Count() == 1) return false;
        return DigitosCnpj(d[..12]) == d[12..];
    }

    public static string DigitosCnpj(string doze)
    {
        int Dv(string s)
        {
            var soma = 0;
            var peso = 2;
            for (var i = s.Length - 1; i >= 0; i--)
            {
                soma += (s[i] - '0') * peso;
                peso = peso == 9 ? 2 : peso + 1;
            }
            var r = soma % 11;
            return r < 2 ? 0 : 11 - r;
        }
        var d1 = Dv(doze);
        var d2 = Dv(doze + d1);
        return $"{d1}{d2}";
    }

    public static string FormatarCpf(string cpf)
    {
        var d = Normalizar(cpf);
        return d.Length == 11 ? $"{d[..3]}.{d[3..6]}.{d[6..9]}-{d[9..]}" : cpf;
    }

    public static string FormatarCnpj(string cnpj)
    {
        var d = Normalizar(cnpj);
        return d.Length == 14 ? $"{d[..2]}.{d[2..5]}.{d[5..8]}/{d[8..12]}-{d[12..]}" : cnpj;
    }

    /// <summary>Mascara o CPF para exibição pública (LGPD): ***.456.789-**.</summary>
    public static string MascararCpf(string cpf)
    {
        var d = Normalizar(cpf);
        return d.Length == 11 ? $"***.{d[3..6]}.{d[6..9]}-**" : "***";
    }
}
