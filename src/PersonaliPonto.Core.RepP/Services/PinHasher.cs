using System.Security.Cryptography;

namespace PersonaliPonto.Core.RepP.Services;

/// <summary>Hash de PIN e senha com PBKDF2-SHA256 + sal aleatório. Nunca armazena texto puro.</summary>
public static class SecretHasher
{
    private const int Iteracoes = 210_000;
    private const int TamanhoSal = 16;
    private const int TamanhoHash = 32;

    public static string Hash(string segredo)
    {
        var sal = RandomNumberGenerator.GetBytes(TamanhoSal);
        var hash = Rfc2898DeriveBytes.Pbkdf2(segredo, sal, Iteracoes, HashAlgorithmName.SHA256, TamanhoHash);
        return $"pbkdf2-sha256${Iteracoes}${Convert.ToBase64String(sal)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verificar(string segredo, string? armazenado)
    {
        if (string.IsNullOrEmpty(armazenado)) return false;
        var partes = armazenado.Split('$');
        if (partes.Length != 4 || partes[0] != "pbkdf2-sha256") return false;
        var iter = int.Parse(partes[1]);
        var sal = Convert.FromBase64String(partes[2]);
        var esperado = Convert.FromBase64String(partes[3]);
        var hash = Rfc2898DeriveBytes.Pbkdf2(segredo, sal, iter, HashAlgorithmName.SHA256, esperado.Length);
        return CryptographicOperations.FixedTimeEquals(hash, esperado);
    }

    /// <summary>PIN numérico de 4 a 8 dígitos, rejeitando sequências triviais.</summary>
    public static bool PinAceitavel(string pin)
    {
        if (pin.Length is < 4 or > 8 || !pin.All(char.IsAsciiDigit)) return false;
        if (pin.Distinct().Count() == 1) return false;
        const string seq = "01234567890";
        const string seqDesc = "09876543210";
        return !seq.Contains(pin) && !seqDesc.Contains(pin);
    }

    /// <summary>Hash determinístico para tokens opacos de alta entropia (refresh token, terminal).</summary>
    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    public static string NovoToken(int bytes = 32) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
