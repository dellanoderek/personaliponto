using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Formatacao;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Modules.SaaS.Domain;

namespace PersonaliPonto.Modules.SaaS.Services;

public enum SituacaoMesImportado
{
    Pago,
    NaoCobrado,
    EmAberto
}

public sealed record LinhaImportada(
    int Linha,
    string RazaoSocial,
    string Cnpj,
    bool CnpjValido,
    string? Telefone,
    decimal Valor,
    int Funcionarios,
    decimal Custo,
    string? Fornecedor,
    bool Cancelado,
    IReadOnlyDictionary<DateOnly, SituacaoMesImportado> Meses,
    bool JaExiste,
    IReadOnlyList<string> Avisos);

public sealed record ResultadoImportacao(int Criados, int Atualizados, int Faturas, IReadOnlyList<string> Erros);

/// <summary>
/// Importa a planilha de controle de clientes usada hoje (TELEFONE, RAZÃO SOCIAL, CNPJ, VALOR, FUNCIONÁRIOS,
/// uma coluna por competência com "PAGO"/"XXXXX", CUSTO, fornecedor e "cancelado").
/// Sempre gera uma prévia antes de gravar.
/// </summary>
public sealed class ImportacaoPlanilhaService(ISaasDbContext db, ClienteService clientes, AuditService audit, IClock clock)
{
    public async Task<IReadOnlyList<LinhaImportada>> PreviaAsync(Stream xlsx, CancellationToken ct)
    {
        var linhas = Ler(xlsx, DateOnly.FromDateTime(clock.UtcNow.UtcDateTime));
        var cnpjs = await db.Assinaturas.AsNoTracking().Select(a => a.Cnpj).ToListAsync(ct);
        return linhas.Select(l => l with { JaExiste = cnpjs.Contains(l.Cnpj) }).ToList();
    }

    public static IReadOnlyList<LinhaImportada> Ler(Stream xlsx, DateOnly hoje)
    {
        using var wb = new XLWorkbook(xlsx);
        var ws = wb.Worksheets.First();
        var usadas = ws.RangeUsed() ?? throw new RegraNegocioException("Planilha vazia.");

        // Localiza a linha de cabeçalho pela coluna CNPJ.
        var cab = usadas.Rows().FirstOrDefault(r => r.Cells().Any(c => Texto(c) == "CNPJ"))
                  ?? throw new RegraNegocioException("Cabeçalho com a coluna CNPJ não encontrado.");
        int? Col(Func<string, bool> pred) => cab.Cells().FirstOrDefault(c => pred(Texto(c)))?.Address.ColumnNumber;
        var cTel = Col(t => t.StartsWith("TELEFONE"));
        var cRazao = Col(t => t.Contains("SOCIAL")) ?? throw new RegraNegocioException("Coluna RAZÃO SOCIAL não encontrada.");
        var cCnpj = Col(t => t == "CNPJ")!.Value;
        var cValor = Col(t => t == "VALOR");
        var cFunc = Col(t => t.StartsWith("FUNCION"));
        var cCusto = Col(t => t == "CUSTO");
        var meses = cab.Cells()
            .Select(c => (Col: c.Address.ColumnNumber, Data: DataCabecalho(c)))
            .Where(x => x.Data is not null)
            .Select(x => (x.Col, Competencia: new DateOnly(x.Data!.Value.Year, x.Data.Value.Month, 1)))
            .ToList();
        if (meses.Count == 0) throw new RegraNegocioException("Nenhuma coluna de competência (mês) encontrada no cabeçalho.");

        var competenciaAtual = new DateOnly(hoje.Year, hoje.Month, 1);
        var resultado = new List<LinhaImportada>();
        var ultimaLinha = ws.LastRowUsed()!.RowNumber();
        for (var n = cab.RowNumber() + 1; n <= ultimaLinha; n++)
        {
            var row = ws.Row(n);
            var razao = row.Cell(cRazao).GetString().Trim();
            var cnpj = Documentos.Normalizar(row.Cell(cCnpj).GetString());
            if (razao.Length == 0 && cnpj.Length == 0) continue;

            var avisos = new List<string>();
            var cnpjOk = Documentos.CnpjValido(cnpj) || Documentos.CpfValido(cnpj);
            if (!cnpjOk) avisos.Add("CNPJ/CPF inválido");

            var textos = row.CellsUsed().Select(c => c.GetString().Trim()).ToList();
            var cancelado = textos.Any(t => t.Equals("cancelado", StringComparison.OrdinalIgnoreCase));
            var usadasCols = new HashSet<int?> { cTel, cRazao, cCnpj, cValor, cFunc, cCusto };
            foreach (var m in meses) usadasCols.Add(m.Col);
            var fornecedor = row.CellsUsed()
                .Where(c => !usadasCols.Contains(c.Address.ColumnNumber))
                .Select(c => c.GetString().Trim())
                .FirstOrDefault(t => t.Length > 2 && !t.Equals("cancelado", StringComparison.OrdinalIgnoreCase) && !decimal.TryParse(t, out _));

            var situacoes = new Dictionary<DateOnly, SituacaoMesImportado>();
            DateOnly? primeiro = null, ultimoMarcado = null;
            foreach (var (col, comp) in meses)
            {
                var v = row.Cell(col).GetString().Trim().ToUpperInvariant();
                if (v.StartsWith("PAGO")) situacoes[comp] = SituacaoMesImportado.Pago;
                else if (v.Length > 0 && v.All(ch => ch == 'X')) situacoes[comp] = SituacaoMesImportado.NaoCobrado;
                else if (v.Length > 0) avisos.Add($"Valor '{v}' em {comp:MM/yyyy} não reconhecido");
                if (situacoes.ContainsKey(comp))
                {
                    primeiro ??= comp;
                    ultimoMarcado = comp;
                }
            }
            // Meses vazios entre o início e a competência atual (ou até o cancelamento) ficam em aberto.
            if (primeiro is not null)
            {
                var limite = cancelado ? ultimoMarcado!.Value : competenciaAtual;
                foreach (var (_, comp) in meses)
                    if (comp >= primeiro && comp <= limite && !situacoes.ContainsKey(comp))
                        situacoes[comp] = SituacaoMesImportado.EmAberto;
            }
            else avisos.Add("Nenhum mês preenchido");

            resultado.Add(new LinhaImportada(n, razao, cnpj, cnpjOk,
                cTel is null ? null : row.Cell(cTel.Value).GetString().Trim(),
                Decimal(row, cValor), (int)Decimal(row, cFunc), Decimal(row, cCusto),
                fornecedor?.ToUpperInvariant(), cancelado, situacoes, false, avisos));
        }
        return resultado;
    }

    public async Task<ResultadoImportacao> ImportarAsync(IReadOnlyList<LinhaImportada> linhas, CancellationToken ct)
    {
        int criados = 0, atualizados = 0, faturas = 0;
        var erros = new List<string>();
        foreach (var l in linhas)
        {
            if (!l.CnpjValido) { erros.Add($"Linha {l.Linha} ({l.RazaoSocial}): CNPJ inválido — ignorada."); continue; }
            try
            {
                var a = await db.Assinaturas.FirstOrDefaultAsync(x => x.Cnpj == l.Cnpj, ct);
                if (a is null)
                {
                    var criado = await clientes.CadastrarAsync(new NovoClienteRequest(
                        l.RazaoSocial, l.Cnpj, l.Telefone, "", "", null, l.Valor, l.Funcionarios, l.Custo, l.Fornecedor, 10, null,
                        l.RazaoSocial, false), ct);
                    a = await db.Assinaturas.FirstAsync(x => x.TenantId == criado.TenantId, ct);
                    criados++;
                }
                else
                {
                    a.ValorMensal = l.Valor;
                    a.FuncionariosContratados = l.Funcionarios;
                    a.CustoMensal = l.Custo;
                    a.Fornecedor = l.Fornecedor ?? a.Fornecedor;
                    a.Telefone = l.Telefone ?? a.Telefone;
                    atualizados++;
                }

                if (l.Meses.Count > 0) a.Inicio = l.Meses.Keys.Min();
                var existentes = await db.Faturas.Where(f => f.TenantId == a.TenantId).ToDictionaryAsync(f => f.Competencia, ct);
                foreach (var (comp, sit) in l.Meses)
                {
                    if (!existentes.TryGetValue(comp, out var f))
                    {
                        f = new Fatura
                        {
                            TenantId = a.TenantId, Competencia = comp, Valor = l.Valor,
                            Vencimento = new DateOnly(comp.Year, comp.Month, Math.Min(a.DiaVencimento, 28)), CriadaEm = clock.UtcNow
                        };
                        db.Faturas.Add(f);
                        faturas++;
                    }
                    if (f.Status == StatusFatura.Paga && sit != SituacaoMesImportado.Pago) continue;
                    f.Status = sit switch
                    {
                        SituacaoMesImportado.Pago => StatusFatura.Paga,
                        SituacaoMesImportado.NaoCobrado => StatusFatura.NaoCobrada,
                        _ => StatusFatura.Aberta
                    };
                    if (sit == SituacaoMesImportado.Pago)
                    {
                        f.PagaEm ??= f.Vencimento;
                        f.ValorPago ??= f.Valor;
                        f.Observacao ??= "Importado da planilha";
                    }
                    f.AtualizadaEm = clock.UtcNow;
                }

                var t = await db.Tenants.FirstAsync(x => x.Id == a.TenantId, ct);
                if (l.Cancelado)
                {
                    a.CanceladaEm ??= (l.Meses.Count > 0 ? l.Meses.Keys.Max().AddMonths(1) : DateOnly.FromDateTime(clock.UtcNow.UtcDateTime));
                    a.MotivoCancelamento ??= "Cancelado (planilha)";
                    t.Status = TenantStatus.Cancelado;
                }
                await db.SaveChangesAsync(ct);
            }
            catch (RegraNegocioException ex)
            {
                erros.Add($"Linha {l.Linha} ({l.RazaoSocial}): {ex.Message}");
                db.ChangeTracker().Clear();
            }
        }
        audit.Registrar("clientes.importados", "Planilha", null, new { criados, atualizados, faturas, erros = erros.Count });
        await db.SaveChangesAsync(ct);
        return new ResultadoImportacao(criados, atualizados, faturas, erros);
    }

    /// <summary>Aceita data real, número serial do Excel formatado como data, ou texto "jan/26", "01/2026".</summary>
    public static DateOnly? DataCabecalho(IXLCell c)
    {
        if (c.DataType == XLDataType.DateTime) return DateOnly.FromDateTime(c.GetDateTime());
        if (c.DataType == XLDataType.Number)
        {
            var n = c.GetDouble();
            return n is > 36526 and < 73051 ? DateOnly.FromDateTime(DateTime.FromOADate(n)) : null; // 2000..2099
        }
        var t = c.GetString().Trim().ToLowerInvariant().Replace(".", "");
        if (t.Length == 0) return null;
        var br = new CultureInfo("pt-BR");
        string[] formatos = ["MM/yyyy", "M/yyyy", "MMM/yy", "MMM/yyyy", "MMMM/yyyy", "MMMM/yy", "dd/MM/yyyy", "yyyy-MM-dd", "yyyy-MM"];
        return DateTime.TryParseExact(t, formatos, br, DateTimeStyles.None, out var d) ? DateOnly.FromDateTime(d) : null;
    }

    private static string Texto(IXLCell c) =>
        c.GetString().Trim().ToUpperInvariant().Replace("Ã", "A").Replace("Á", "A").Replace("Ç", "C");

    private static decimal Decimal(IXLRow row, int? col)
    {
        if (col is null) return 0;
        var c = row.Cell(col.Value);
        if (c.DataType == XLDataType.Number) return (decimal)c.GetDouble();
        var s = c.GetString().Replace("R$", "").Trim();
        return decimal.TryParse(s, NumberStyles.Any, new CultureInfo("pt-BR"), out var v) ? v : 0;
    }
}

internal static class DbContextExtensions
{
    public static Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker ChangeTracker(this IRepPDbContext db) =>
        ((DbContext)db).ChangeTracker;
}
