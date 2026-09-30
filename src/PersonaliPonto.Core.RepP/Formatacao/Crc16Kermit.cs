namespace PersonaliPonto.Core.RepP.Formatacao;

/// <summary>
/// CRC-16 CCITT-TRUE (CRC-16/KERMIT), exigido pelo MTE para os registros 1 a 5 do AFD de REP-A/REP-P
/// (FAQ MTE nº 35). "123456789" =&gt; "2189".
/// </summary>
public static class Crc16Kermit
{
    private static readonly ushort[] Tabela = CriarTabela();

    private static ushort[] CriarTabela()
    {
        var t = new ushort[256];
        for (ushort i = 0; i < 256; i++)
        {
            ushort crc = i;
            for (var b = 0; b < 8; b++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0x8408) : (ushort)(crc >> 1);
            t[i] = crc;
        }
        return t;
    }

    public static ushort Calcular(ReadOnlySpan<byte> dados)
    {
        ushort crc = 0;
        foreach (var b in dados)
            crc = (ushort)((crc >> 8) ^ Tabela[(crc ^ b) & 0xFF]);
        return crc;
    }

    /// <summary>Representação exigida pelo MTE: valor 0x2189 gravado como "2189".</summary>
    public static string Hex(ReadOnlySpan<byte> dados) => Calcular(dados).ToString("X4");
}
