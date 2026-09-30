using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using PersonaliPonto.Core.RepP.Formatacao;

namespace PersonaliPonto.Core.RepP.Afd;

/// <summary>
/// Validador independente do AFD de REP-P contra o leiaute oficial (posições, tamanhos, tipos, CRC-16,
/// cadeia de hash SHA-256, contadores do trailer, CR LF, ISO-8859-1). Usado nos testes e antes da entrega.
/// </summary>
public static partial class AfdValidator
{
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:00[+-]\d{4}$")]
    private static partial Regex DhRegex();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
    private static partial Regex DRegex();

    public static IReadOnlyList<string> Validar(byte[] conteudo, string? hashAnteriorAoPrimeiro = null)
    {
        var erros = new List<string>();
        var texto = AfdCampos.Latin1.GetString(conteudo);
        if (!texto.EndsWith("\r\n")) erros.Add("Arquivo não termina com CR LF.");
        if (texto.Replace("\r\n", "").Contains('\n') || texto.Replace("\r\n", "").Contains('\r'))
            erros.Add("Quebra de linha diferente de CR LF.");

        var linhas = texto.Split("\r\n");
        if (linhas[^1] == "") linhas = linhas[..^1];
        if (linhas.Any(l => l.Length == 0)) erros.Add("Arquivo contém linha em branco.");
        if (linhas.Length < 3) { erros.Add("Arquivo sem cabeçalho/trailer/assinatura."); return erros; }

        // Cabeçalho
        var h = linhas[0];
        Tam(h, 302, 1);
        if (h.Length == 302)
        {
            Igual(h, 1, 9, "000000000", "cab.NSR");
            Igual(h, 10, 1, "1", "cab.tipo");
            Em(h, 11, 1, ["1", "2"], "cab.tipoIdEmpregador");
            Dt(h, 207, "cab.dataInicial");
            Dt(h, 217, "cab.dataFinal");
            Dh(h, 227, "cab.geracao");
            Igual(h, 251, 3, AfdGenerator.VersaoLeiaute, "cab.versao");
            Em(h, 254, 1, ["1", "2"], "cab.tipoIdDesenvolvedor");
            Crc(h, 1);
        }

        var cont = new Dictionary<char, int>();
        long nsrAnterior = 0;
        var hashAnterior = hashAnteriorAoPrimeiro;
        for (var i = 1; i < linhas.Length - 2; i++)
        {
            var l = linhas[i];
            if (l.Length < 10) { erros.Add($"Linha {i + 1}: registro curto."); continue; }
            var tipo = l[9];
            cont[tipo] = cont.GetValueOrDefault(tipo) + 1;
            if (!long.TryParse(l[..9], NumberStyles.None, CultureInfo.InvariantCulture, out var nsr))
                erros.Add($"Linha {i + 1}: NSR não numérico.");
            else if (nsr <= nsrAnterior)
                erros.Add($"Linha {i + 1}: NSR fora de ordem ({nsr} após {nsrAnterior}).");
            else nsrAnterior = nsr;

            switch (tipo)
            {
                case '2':
                    if (Tam(l, 331, i + 1)) { Dh(l, 11, $"L{i + 1}.gravacao"); Em(l, 49, 1, ["1", "2"], $"L{i + 1}.tipoId"); Crc(l, i + 1); }
                    break;
                case '4':
                    if (Tam(l, 73, i + 1)) { Dh(l, 11, $"L{i + 1}.antes"); Dh(l, 35, $"L{i + 1}.ajustada"); Crc(l, i + 1); }
                    break;
                case '5':
                    if (Tam(l, 118, i + 1))
                    {
                        Dh(l, 11, $"L{i + 1}.gravacao");
                        Em(l, 35, 1, ["I", "A", "E"], $"L{i + 1}.operacao");
                        Num(l, 36, 12, $"L{i + 1}.cpf");
                        Crc(l, i + 1);
                    }
                    break;
                case '6':
                    if (Tam(l, 36, i + 1)) { Dh(l, 11, $"L{i + 1}.gravacao"); Em(l, 35, 2, ["02", "07", "08"], $"L{i + 1}.evento"); }
                    break;
                case '7':
                    if (Tam(l, 137, i + 1))
                    {
                        Dh(l, 11, $"L{i + 1}.marcacao");
                        Num(l, 35, 12, $"L{i + 1}.cpf");
                        Dh(l, 47, $"L{i + 1}.gravacao");
                        Em(l, 71, 2, ["01", "02", "03", "04", "05"], $"L{i + 1}.coletor");
                        Em(l, 73, 1, ["0", "1"], $"L{i + 1}.offline");
                        var esperado = Convert.ToHexString(SHA256.HashData(AfdCampos.Latin1.GetBytes(l[..73] + (hashAnterior ?? ""))));
                        var hash = l[73..137];
                        if (hash != esperado) erros.Add($"Linha {i + 1}: hash SHA-256 inválido ou cadeia quebrada.");
                        hashAnterior = hash;
                    }
                    break;
                default:
                    erros.Add($"Linha {i + 1}: tipo de registro '{tipo}' inválido para REP-P.");
                    break;
            }
        }

        var t = linhas[^2];
        if (Tam(t, 64, linhas.Length - 1))
        {
            Igual(t, 1, 9, "999999999", "trailer.inicio");
            Igual(t, 64, 1, "9", "trailer.tipo");
            var tipos = new[] { '2', '3', '4', '5', '6', '7' };
            for (var k = 0; k < tipos.Length; k++)
                Igual(t, 10 + k * 9, 9, cont.GetValueOrDefault(tipos[k]).ToString("000000000"), $"trailer.qt{tipos[k]}");
        }

        var a = linhas[^1];
        if (a != AfdCampos.A(AfdCampos.TextoAssinatura, 100)) erros.Add("Linha de assinatura digital inválida.");
        return erros;

        bool Tam(string l, int n, int linha)
        {
            if (l.Length == n) return true;
            erros.Add($"Linha {linha}: {l.Length} posições (esperado {n}).");
            return false;
        }
        string Campo(string l, int pos, int tam) => l.Substring(pos - 1, tam);
        void Igual(string l, int pos, int tam, string v, string nome)
        {
            if (Campo(l, pos, tam) != v) erros.Add($"{nome}: '{Campo(l, pos, tam)}' esperado '{v}'.");
        }
        void Em(string l, int pos, int tam, string[] vs, string nome)
        {
            if (!vs.Contains(Campo(l, pos, tam))) erros.Add($"{nome}: valor '{Campo(l, pos, tam)}' inválido.");
        }
        void Dh(string l, int pos, string nome)
        {
            if (!DhRegex().IsMatch(Campo(l, pos, 24))) erros.Add($"{nome}: data/hora '{Campo(l, pos, 24)}' fora do formato.");
        }
        void Dt(string l, int pos, string nome)
        {
            if (!DRegex().IsMatch(Campo(l, pos, 10))) erros.Add($"{nome}: data '{Campo(l, pos, 10)}' fora do formato.");
        }
        void Num(string l, int pos, int tam, string nome)
        {
            if (!Campo(l, pos, tam).All(char.IsAsciiDigit)) erros.Add($"{nome}: não numérico.");
        }
        void Crc(string l, int linha)
        {
            var corpo = l[..^4];
            if (Crc16Kermit.Hex(AfdCampos.Latin1.GetBytes(corpo)) != l[^4..]) erros.Add($"Linha {linha}: CRC-16 inválido.");
        }
    }
}
