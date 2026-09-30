using System.Globalization;
using System.Text;
using PersonaliPonto.Core.RepP.Apuracao;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Formatacao;

namespace PersonaliPonto.Core.RepP.Aej;

public sealed record AejCabecalho(
    TipoIdentificador TipoIdentificadorEmpregador,
    string IdentificadorEmpregador,
    string? Caepf,
    string? Cno,
    string RazaoSocial,
    DateOnly DataInicial,
    DateOnly DataFinal,
    DateTimeOffset GeradoEm,
    int OffsetMinutos,
    string NumeroRegistroInpi);

public sealed record AejPtrp(
    string Nome,
    string Versao,
    TipoIdentificador TipoIdentificadorDesenvolvedor,
    string IdentificadorDesenvolvedor,
    string RazaoSocialDesenvolvedor,
    string Email);

public enum TipoAusencia
{
    Dsr = 1,
    FaltaNaoJustificada = 2,
    BancoHoras = 3,
    FolgaCompensatoriaFeriado = 4
}

public sealed record AejAusencia(DateOnly Data, TipoAusencia Tipo, int? Minutos = null, int? TipoMovimentoBancoHoras = null);

public sealed record AejVinculo(
    string Cpf,
    string Nome,
    string? MatriculaEsocial,
    Jornada? Jornada,
    IReadOnlyList<DiaApurado> Dias,
    IReadOnlyList<AejAusencia> Ausencias,
    TimeZoneInfo Fuso);

public sealed record AejArquivo(string NomeArquivo, byte[] Conteudo);

/// <summary>
/// Gerador do Arquivo Eletrônico de Jornada (Anexo VI da Portaria MTP 671/2021, leiaute vigente "002").
/// Campos delimitados por "|", ISO-8859-1, linhas terminadas em CR LF.
/// </summary>
public static class AejGenerator
{
    public const string VersaoLeiaute = "002";
    private const int IdRepAej = 1;

    public static AejArquivo Gerar(AejCabecalho cab, AejPtrp ptrp, IReadOnlyList<AejVinculo> vinculos)
    {
        var linhas = new List<string>();
        var cont = new int[9];
        void Add(int tipo, params string?[] campos)
        {
            linhas.Add(string.Join('|', new[] { tipo.ToString("00") }.Concat(campos.Select(c => Limpar(c ?? "")))));
            cont[tipo]++;
        }

        var ident = Documentos.Normalizar(cab.IdentificadorEmpregador);
        Add(1,
            ((int)cab.TipoIdentificadorEmpregador).ToString(),
            ident,
            Documentos.SomenteDigitos(cab.Caepf),
            Documentos.SomenteDigitos(cab.Cno),
            Trunc(cab.RazaoSocial, 150),
            AfdCampos.D(cab.DataInicial),
            AfdCampos.D(cab.DataFinal),
            AfdCampos.DH(cab.GeradoEm, cab.OffsetMinutos),
            VersaoLeiaute);

        Add(2, IdRepAej.ToString(), "3", Documentos.SomenteDigitos(cab.NumeroRegistroInpi));

        // Vínculos (03) e matrícula eSocial para CPFs com mais de um vínculo (06).
        var cpfsRepetidos = vinculos.GroupBy(v => Documentos.Normalizar(v.Cpf)).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        for (var i = 0; i < vinculos.Count; i++)
            Add(3, (i + 1).ToString(), Documentos.Normalizar(vinculos[i].Cpf), Trunc(vinculos[i].Nome, 150));

        // Horários contratuais (04): um código por padrão de dia de cada jornada.
        var horarios = new Dictionary<string, (int Duracao, HorarioDia Horario)>();
        string? CodigoHorario(Jornada? j, DiaApurado d)
        {
            if (j is null) return null;
            var h = d.Horario ?? j.Horarios.Where(x => x.Periodos.Count > 0).OrderBy(x => x.Indice).FirstOrDefault();
            if (h is null) return null;
            var codigo = Trunc(CodigoPadrao(j, h), 30);
            if (!horarios.ContainsKey(codigo))
                horarios[codigo] = (ApuracaoJornada.MinutosPrevistos(h, d.Data, new ParametrosApuracao()), h);
            return codigo;
        }

        var marcacoes = new List<string?[]>();
        var ausencias = new List<string?[]>();
        for (var i = 0; i < vinculos.Count; i++)
        {
            var v = vinculos[i];
            var idVinc = (i + 1).ToString();
            foreach (var dia in v.Dias)
            {
                var seq = 0;
                for (var k = 0; k < dia.Validas.Count; k++)
                {
                    var m = dia.Validas[k];
                    var entrada = k % 2 == 0;
                    if (entrada) seq++;
                    var fonte = (char)m.Fonte;
                    marcacoes.Add([
                        idVinc,
                        DH(m.Instante, v.Fuso),
                        fonte == 'O' ? IdRepAej.ToString() : "",
                        entrada ? "E" : "S",
                        seq.ToString(),
                        fonte.ToString(),
                        entrada && seq == 1 ? CodigoHorario(dia.Jornada ?? v.Jornada, dia) : "",
                        fonte == 'I' ? Trunc(m.Motivo, 150) : ""
                    ]);
                }
                foreach (var m in dia.Marcacoes.Where(x => x.Desconsiderada))
                {
                    var fonte = (char)m.Fonte;
                    marcacoes.Add([
                        idVinc,
                        DH(m.Instante, v.Fuso),
                        fonte == 'O' ? IdRepAej.ToString() : "",
                        "D",
                        "0",
                        fonte.ToString(),
                        "",
                        Trunc(string.IsNullOrWhiteSpace(m.Motivo) ? "Marcação desconsiderada" : m.Motivo, 150)
                    ]);
                }
            }
            foreach (var a in v.Ausencias.OrderBy(x => x.Data))
            {
                ausencias.Add([
                    idVinc,
                    ((int)a.Tipo).ToString(),
                    AfdCampos.D(a.Data),
                    a.Tipo == TipoAusencia.BancoHoras ? (a.Minutos ?? 0).ToString(CultureInfo.InvariantCulture) : "",
                    a.Tipo == TipoAusencia.BancoHoras ? (a.TipoMovimentoBancoHoras ?? 1).ToString() : ""
                ]);
            }
        }

        foreach (var (codigo, (duracao, h)) in horarios)
        {
            var campos = new List<string?> { codigo, duracao.ToString() };
            foreach (var per in h.Periodos)
            {
                campos.Add(per.Entrada.ToString("HHmm", CultureInfo.InvariantCulture));
                campos.Add(per.Saida.ToString("HHmm", CultureInfo.InvariantCulture));
            }
            if (h.Periodos.Count == 1) { campos.Add(""); campos.Add(""); }
            Add(4, campos.ToArray());
        }

        foreach (var m in marcacoes) Add(5, m);

        for (var i = 0; i < vinculos.Count; i++)
        {
            var cpf = Documentos.Normalizar(vinculos[i].Cpf);
            if (cpfsRepetidos.Contains(cpf) && !string.IsNullOrWhiteSpace(vinculos[i].MatriculaEsocial))
                Add(6, (i + 1).ToString(), Trunc(vinculos[i].MatriculaEsocial, 30));
        }

        foreach (var a in ausencias) Add(7, a);

        Add(8, Trunc(ptrp.Nome, 150), Trunc(ptrp.Versao, 8), ((int)ptrp.TipoIdentificadorDesenvolvedor).ToString(),
            Documentos.Normalizar(ptrp.IdentificadorDesenvolvedor), Trunc(ptrp.RazaoSocialDesenvolvedor, 150), Trunc(ptrp.Email, 50));

        linhas.Add("99|" + string.Join('|', Enumerable.Range(1, 8).Select(t => cont[t].ToString())));
        linhas.Add(AfdCampos.A(AfdCampos.TextoAssinatura, 100));

        var texto = string.Join(AfdCampos.FimDeLinha, linhas) + AfdCampos.FimDeLinha;
        var nome = $"AEJ{ident}{cab.DataInicial:yyyyMMdd}{cab.DataFinal:yyyyMMdd}.txt";
        return new AejArquivo(nome, AfdCampos.Latin1.GetBytes(texto));
    }

    /// <summary>
    /// Código estável do horário contratual: dias da jornada com os mesmos períodos compartilham o código.
    /// O primeiro padrão (menor índice) recebe o código da jornada; os demais, "-2", "-3"...
    /// </summary>
    public static string CodigoPadrao(Jornada j, HorarioDia h)
    {
        static string Assinatura(HorarioDia x) => string.Join(';', x.Periodos.Select(p => $"{p.Entrada:HHmm}-{p.Saida:HHmm}"));
        var padroes = j.Horarios.Where(x => x.Periodos.Count > 0).OrderBy(x => x.Indice)
            .Select(Assinatura).Distinct().ToList();
        var i = padroes.IndexOf(Assinatura(h));
        return i <= 0 ? j.Codigo : $"{j.Codigo}-{i + 1}";
    }

    private static string DH(DateTimeOffset instante, TimeZoneInfo fuso) =>
        AfdCampos.DH(instante, (int)fuso.GetUtcOffset(instante).TotalMinutes);

    private static string Trunc(string? s, int max)
    {
        var v = (s ?? "").Trim();
        return v.Length > max ? v[..max] : v;
    }

    /// <summary>O delimitador "|" não pode aparecer dentro de um campo.</summary>
    private static string Limpar(string s) => AfdCampos.Sanitizar(s).Replace('|', '/');
}
