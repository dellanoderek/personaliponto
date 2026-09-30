using Microsoft.EntityFrameworkCore;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Modules.SaaS.Domain;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Modules.SaaS.Services;

public sealed record LinhaGradeCanal(Guid CanalId, string NomeMarca, string RazaoSocial, string Cnpj, StatusCanal Status,
    IReadOnlyDictionary<DateOnly, (CelulaFatura Estado, Guid? FaturaId, decimal Valor)> Meses, decimal EmAberto);

public sealed record CondicoesCanal(DateOnly? PremiumIsentoAte, bool AppProprioAtivo, DateOnly? AppProprioDesde);

/// <summary>
/// Faturamento entre canais (E2): Owner → revendedor (uso apurado + Premium + serviços avulsos) e revendedor → parceiro
/// (funcionários ativos × preço do revendedor), com régua de canal D+5 aviso, D+15 painel bloqueado, D+30 suspensão.
/// A régua nunca toca clientes (tenants): marcação, comprovantes e AFD/AEJ/espelho continuam sempre disponíveis.
/// </summary>
public sealed class FaturamentoCanalService(ISaasDbContext db, ITenantContext ctx, ICurrentUser usuario, AuditService audit, IClock clock)
{
    public const int DiaVencimento = 10;
    public const int DiasAviso = 5;
    public const int DiasPainelBloqueado = 15;
    public const int DiasSuspensao = 30;

    private static readonly TimeZoneInfo Fuso = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    public DateOnly Hoje => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, Fuso).DateTime);
    public static DateOnly DataLocal(DateTimeOffset d) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(d, Fuso).DateTime);
    private static DateOnly Mes(DateOnly d) => new(d.Year, d.Month, 1);

    private void ExigirPlataforma()
    {
        if (!ctx.IsSystem || ctx.CanalId is not null) throw new AcessoNegadoException("Operação exclusiva da plataforma.");
    }

    private void ExigirSuperAdmin()
    {
        ExigirPlataforma();
        if (usuario.Papel is not (null or Roles.SuperAdmin)) throw new AcessoNegadoException("Apenas o Super Admin altera condições comerciais.");
    }

    // ---------------- Tabela de preço ----------------

    public async Task<TabelaPrecoCanal> TabelaVigenteAsync(DateOnly competencia, CancellationToken ct)
    {
        competencia = Mes(competencia);
        return await db.TabelasPrecoCanal.AsNoTracking().Where(t => t.VigenciaInicio <= competencia)
                   .OrderByDescending(t => t.VigenciaInicio).FirstOrDefaultAsync(ct)
               ?? throw new RegraNegocioException("Não há tabela de preço vigente para a competência.");
    }

    public Task<List<TabelaPrecoCanal>> TabelasAsync(CancellationToken ct) =>
        db.TabelasPrecoCanal.AsNoTracking().OrderByDescending(t => t.VigenciaInicio).ToListAsync(ct);

    /// <summary>Nova versão da tabela (vigência a partir de uma competência futura ou corrente). Versões passadas não mudam.</summary>
    public async Task<TabelaPrecoCanal> NovaTabelaAsync(TabelaPrecoCanal t, CancellationToken ct)
    {
        ExigirSuperAdmin();
        t.VigenciaInicio = Mes(t.VigenciaInicio);
        if (t.VigenciaInicio < Mes(Hoje)) throw new RegraNegocioException("A vigência deve começar na competência atual ou futura.");
        if (t.Faixa1Ate <= 0 || t.Faixa2Ate <= t.Faixa1Ate) throw new RegraNegocioException("Faixas inválidas.");
        if (new[] { t.PrecoEmpresaAtiva, t.Faixa1Preco, t.Faixa2Preco, t.Faixa3Preco, t.MinimoMensal, t.PrecoPremium, t.AppProprioImplantacao, t.AppProprioMensal }.Any(v => v < 0))
            throw new RegraNegocioException("Valores não podem ser negativos.");
        if (await db.TabelasPrecoCanal.AnyAsync(x => x.VigenciaInicio == t.VigenciaInicio, ct))
            throw new RegraNegocioException("Já existe tabela com esta vigência.");
        if (await db.FaturasCanal.AnyAsync(f => f.Competencia >= t.VigenciaInicio && f.Tipo == TipoFaturaCanal.Plataforma, ct))
            throw new RegraNegocioException("Já há faturas geradas nesta competência.");
        t.Id = Guid.CreateVersion7();
        t.CriadaEm = clock.UtcNow;
        db.TabelasPrecoCanal.Add(t);
        audit.Registrar("canal.tabela_preco_criada", nameof(TabelaPrecoCanal), t.Id, new { t.VigenciaInicio, t.PrecoEmpresaAtiva, t.Faixa1Preco, t.Faixa2Preco, t.Faixa3Preco, t.MinimoMensal });
        await db.SaveChangesAsync(ct);
        return t;
    }

    // ---------------- Condições comerciais ----------------

    public async Task DefinirCondicoesAsync(Guid canalId, CondicoesCanal c, CancellationToken ct)
    {
        ExigirSuperAdmin();
        var canal = await db.Canais.FirstOrDefaultAsync(x => x.Id == canalId, ct) ?? throw new NaoEncontradoException("Canal não encontrado.");
        if (canal.Tipo != TipoCanal.Revendedor) throw new RegraNegocioException("Condições da plataforma valem só para revendedores.");
        if (c.AppProprioAtivo && c.AppProprioDesde is null) throw new RegraNegocioException("Informe a data de início do app próprio.");
        canal.PremiumIsentoAte = c.PremiumIsentoAte;
        var contratouApp = !canal.AppProprioAtivo && c.AppProprioAtivo;
        if (contratouApp) canal.AppProprioImplantacaoCobrada = false;
        canal.AppProprioAtivo = c.AppProprioAtivo;
        canal.AppProprioDesde = c.AppProprioAtivo ? c.AppProprioDesde : canal.AppProprioDesde;
        audit.Registrar("canal.condicoes_alteradas", nameof(Canal), canalId, c);
        // Compra do app próprio: implantação cobrada na hora (fatura avulsa); a mensalidade segue na fatura mensal.
        if (contratouApp)
        {
            var t = await TabelaVigenteAsync(Hoje, ct);
            if (t.AppProprioImplantacao > 0)
            {
                NovaAvulsa(canal.Id, TipoItemFaturaCanal.AppProprioImplantacao, "App próprio nas lojas — implantação", t.AppProprioImplantacao);
                canal.AppProprioImplantacaoCobrada = true;
            }
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Preço por funcionário ativo (e mínimo/isenção opcionais) que o revendedor cobra de um parceiro.</summary>
    public async Task DefinirPrecoParceiroAsync(Guid parceiroId, decimal? precoFuncionario, decimal? minimo, int? isencaoMinClientesPublicos, CancellationToken ct)
    {
        if (usuario.Papel != Roles.AdminRevendedor || ctx.CanalId is not { } rev) throw new AcessoNegadoException("Apenas o administrador do revendedor define o preço do parceiro.");
        if (precoFuncionario < 0 || minimo < 0 || isencaoMinClientesPublicos < 1) throw new RegraNegocioException("Valores inválidos.");
        var p = await db.Canais.FirstOrDefaultAsync(x => x.Id == parceiroId && x.CanalPaiId == rev, ct) ?? throw new NaoEncontradoException("Parceiro não encontrado.");
        p.PrecoFuncionarioParceiro = precoFuncionario;
        p.MinimoMensalParceiro = minimo;
        p.IsencaoMinClientesPublicos = isencaoMinClientesPublicos;
        audit.Registrar("canal.preco_parceiro_alterado", nameof(Canal), parceiroId, new { precoFuncionario, minimo, isencaoMinClientesPublicos });
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Premium ----------------

    public async Task<AssinaturaPremium?> PremiumVigenteAsync(Guid canalId, CancellationToken ct) =>
        await db.AssinaturasPremium.AsNoTracking().Where(a => a.CanalId == canalId && a.CanceladaEm == null).FirstOrDefaultAsync(ct);

    /// <summary>Flag Premium do canal (remover "by PersonaliPonto", domínio próprio etc. nas etapas seguintes).</summary>
    public async Task<bool> PremiumAtivoAsync(Guid canalId, CancellationToken ct)
    {
        var hoje = Hoje;
        var lista = await db.AssinaturasPremium.AsNoTracking().Where(a => a.CanalId == canalId).ToListAsync(ct);
        return lista.Any(a => a.AtivaEm(hoje));
    }

    /// <summary>Contratação do Premium pelo administrador do revendedor (somente no painel web).</summary>
    public async Task AtivarPremiumAsync(CancellationToken ct)
    {
        var canalId = ExigirAdminRevendedor();
        if (await db.AssinaturasPremium.AnyAsync(a => a.CanalId == canalId && a.CanceladaEm == null, ct)) throw new RegraNegocioException("O Premium já está ativo.");
        var a = new AssinaturaPremium { CanalId = canalId, Inicio = Hoje, CriadaEm = clock.UtcNow };
        db.AssinaturasPremium.Add(a);
        audit.Registrar("canal.premium_ativado", nameof(AssinaturaPremium), a.Id, new { a.Inicio });
        await db.SaveChangesAsync(ct);
    }

    public async Task CancelarPremiumAsync(CancellationToken ct)
    {
        var canalId = ExigirAdminRevendedor();
        var a = await db.AssinaturasPremium.FirstOrDefaultAsync(x => x.CanalId == canalId && x.CanceladaEm == null, ct)
                ?? throw new RegraNegocioException("O Premium não está ativo.");
        a.CanceladaEm = Hoje;
        audit.Registrar("canal.premium_cancelado", nameof(AssinaturaPremium), a.Id, new { a.CanceladaEm });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Cobrança imediata do Premium contratado no painel web (fatura avulsa do mês corrente, idempotente).
    /// Roda em modo sistema (INSERT em faturas_canal é exclusivo do sistema). Isento → nada a cobrar.
    /// </summary>
    public async Task<FaturaCanal?> CobrarPremiumImediatoAsync(Guid canalId, CancellationToken ct)
    {
        ExigirSistema();
        var canal = await db.Canais.AsNoTracking().FirstOrDefaultAsync(c => c.Id == canalId, ct) ?? throw new NaoEncontradoException("Canal não encontrado.");
        var comp = Mes(Hoje);
        if (canal.Tipo != TipoCanal.Revendedor || canal.PremiumIsentoAte is { } ate && comp <= ate) return null;
        if (!CalculadoraUsoCanal.Cobravel(DataLocal(canal.CriadoEm), comp)) return null; // mês de entrada não é cobrado
        if (await db.FaturasCanal.AnyAsync(f => f.CanalPagadorId == canalId && f.Tipo == TipoFaturaCanal.Avulsa && f.Competencia == comp
                && f.Itens.Any(i => i.Tipo == TipoItemFaturaCanal.Premium), ct)) return null;
        var t = await TabelaVigenteAsync(comp, ct);
        if (t.PrecoPremium <= 0) return null;
        var f = NovaAvulsa(canalId, TipoItemFaturaCanal.Premium, $"Assinatura Premium — {comp:MM/yyyy}", t.PrecoPremium);
        await db.SaveChangesAsync(ct);
        return f;
    }

    private FaturaCanal NovaAvulsa(Guid revendedorId, TipoItemFaturaCanal tipo, string desc, decimal valor)
    {
        var f = new FaturaCanal
        {
            Tipo = TipoFaturaCanal.Avulsa, CanalPagadorId = revendedorId, CanalRecebedorId = Canal.OwnerRaizId, Competencia = Mes(Hoje),
            Vencimento = Hoje.AddDays(DiasVencimentoAvulsa), CriadaEm = clock.UtcNow
        };
        Item(f, tipo, desc, 1, valor, valor);
        Fechar(f);
        db.FaturasCanal.Add(f);
        audit.Registrar("fatura_canal.avulsa_gerada", nameof(FaturaCanal), f.Id, new { f.CanalPagadorId, tipo, valor });
        FilaCobrancaGateway.Enfileirar(db, f, clock);
        return f;
    }

    public const int DiasVencimentoAvulsa = 3;

    private void ExigirSistema()
    {
        if (!ctx.IsSystem) throw new AcessoNegadoException("Operação interna da plataforma.");
    }

    private Guid ExigirAdminRevendedor()
    {
        if (ctx.IsSystem || ctx.CanalId is not { } id || usuario.Papel != Roles.AdminRevendedor)
            throw new AcessoNegadoException("Apenas o administrador do revendedor contrata ou cancela o Premium.");
        return id;
    }

    // ---------------- Geração de faturas ----------------

    public static DateOnly VencimentoDe(DateOnly competencia) => Mes(competencia).AddMonths(1).AddDays(DiaVencimento - 1);

    /// <summary>
    /// Gera (idempotente) as faturas da competência a partir das apurações já gravadas: Owner → revendedor e
    /// revendedor → parceiro. Canais não são cobrados na competência em que entraram.
    /// </summary>
    public async Task<int> GerarFaturasAsync(DateOnly competencia, CancellationToken ct)
    {
        ExigirPlataforma();
        competencia = Mes(competencia);
        var apuracoes = await db.ApuracoesUsoCanal.AsNoTracking().Where(a => a.Competencia == competencia).ToListAsync(ct);
        if (apuracoes.Count == 0) return 0;
        var tabela = await TabelaVigenteAsync(competencia, ct);
        var existentes = (await db.FaturasCanal.AsNoTracking().Where(f => f.Competencia == competencia && f.Tipo != TipoFaturaCanal.Avulsa)
            .Select(f => new { f.CanalPagadorId, f.CanalRecebedorId }).ToListAsync(ct)).Select(x => (x.CanalPagadorId, x.CanalRecebedorId)).ToHashSet();
        var revIds = apuracoes.Select(a => a.CanalId).ToList();
        var canais = await db.Canais.Where(c => revIds.Contains(c.Id) || (c.CanalPaiId != null && revIds.Contains(c.CanalPaiId.Value))).ToListAsync(ct);
        var premium = await db.AssinaturasPremium.AsNoTracking().Where(a => revIds.Contains(a.CanalId)).ToListAsync(ct);
        var apIds = apuracoes.Select(a => a.Id).ToList();
        var detalhes = await db.ApuracoesUsoTenant.AsNoTracking().Where(d => apIds.Contains(d.ApuracaoId)).ToListAsync(ct);
        // Premium já cobrado na hora da contratação (fatura avulsa) não entra de novo na fatura mensal.
        var premiumAvulso = (await db.FaturasCanal.AsNoTracking()
            .Where(f => f.Competencia == competencia && f.Tipo == TipoFaturaCanal.Avulsa && f.Itens.Any(i => i.Tipo == TipoItemFaturaCanal.Premium))
            .Select(f => f.CanalPagadorId).ToListAsync(ct)).ToHashSet();
        var novas = 0;
        var geradas = new List<FaturaCanal>();

        foreach (var ap in apuracoes)
        {
            var rev = canais.First(c => c.Id == ap.CanalId);
            if (!existentes.Contains((rev.Id, Canal.OwnerRaizId)) && CalculadoraUsoCanal.Cobravel(DataLocal(rev.CriadoEm), competencia))
            {
                var fp = FaturaPlataforma(rev, ap, tabela, premium.Where(p => p.CanalId == rev.Id), competencia, premiumAvulso.Contains(rev.Id));
                db.FaturasCanal.Add(fp);
                geradas.Add(fp);
                novas++;
            }

            foreach (var par in canais.Where(c => c.CanalPaiId == rev.Id && c.Tipo == TipoCanal.Parceiro && c.PrecoFuncionarioParceiro is not null))
            {
                if (existentes.Contains((par.Id, rev.Id)) || !CalculadoraUsoCanal.Cobravel(DataLocal(par.CriadoEm), competencia)) continue;
                var doParceiro = detalhes.Where(d => d.ApuracaoId == ap.Id && d.CanalDonoId == par.Id).ToList();
                var fpar = FaturaParceiro(par, rev, ap, doParceiro, competencia);
                db.FaturasCanal.Add(fpar);
                geradas.Add(fpar);
                novas++;
            }
        }
        if (novas > 0) audit.Registrar("canal.faturas_geradas", nameof(FaturaCanal), competencia.ToString("yyyy-MM"), new { novas });
        // Cobrança no gateway (E3/E9): enfileirada na mesma transação; falha do Asaas nunca impede a geração.
        foreach (var f in geradas) FilaCobrancaGateway.Enfileirar(db, f, clock);
        await db.SaveChangesAsync(ct);
        return novas;
    }

    private FaturaCanal FaturaPlataforma(Canal rev, ApuracaoUsoCanal ap, TabelaPrecoCanal t, IEnumerable<AssinaturaPremium> premium, DateOnly comp, bool premiumJaCobrado = false)
    {
        var f = NovaFatura(TipoFaturaCanal.Plataforma, rev.Id, Canal.OwnerRaizId, comp, ap.Id);
        var u = CalculadoraUsoCanal.Calcular(t, ap.EmpresasAtivas, ap.FuncionariosAtivos);
        Item(f, TipoItemFaturaCanal.EmpresasAtivas, "Empresas ativas", u.Empresas, t.PrecoEmpresaAtiva, u.ValorEmpresas);
        Item(f, TipoItemFaturaCanal.FuncionariosFaixa1, $"Funcionários ativos até {t.Faixa1Ate:N0}", u.QtdFaixa1, t.Faixa1Preco, u.ValorFaixa1);
        if (u.QtdFaixa2 > 0) Item(f, TipoItemFaturaCanal.FuncionariosFaixa2, $"Funcionários ativos de {t.Faixa1Ate + 1:N0} a {t.Faixa2Ate:N0}", u.QtdFaixa2, t.Faixa2Preco, u.ValorFaixa2);
        if (u.QtdFaixa3 > 0) Item(f, TipoItemFaturaCanal.FuncionariosFaixa3, $"Funcionários ativos acima de {t.Faixa2Ate:N0}", u.QtdFaixa3, t.Faixa3Preco, u.ValorFaixa3);
        if (u.AjusteMinimo > 0) Item(f, TipoItemFaturaCanal.AjusteMinimo, $"Complemento ao mínimo mensal ({t.MinimoMensal:C})", 1, u.AjusteMinimo, u.AjusteMinimo);

        if (!premiumJaCobrado && premium.Any(p => p.AtivaNaCompetencia(comp)))
        {
            if (rev.PremiumIsentoAte is { } ate && comp <= ate)
                Item(f, TipoItemFaturaCanal.PremiumIsento, $"Premium — isento até {ate:dd/MM/yyyy}", 1, 0, 0);
            else
                Item(f, TipoItemFaturaCanal.Premium, "Assinatura Premium", 1, t.PrecoPremium, t.PrecoPremium);
        }
        if (rev.AppProprioAtivo && rev.AppProprioDesde is { } desde && Mes(desde) <= comp)
        {
            if (!rev.AppProprioImplantacaoCobrada)
            {
                Item(f, TipoItemFaturaCanal.AppProprioImplantacao, "App próprio nas lojas — implantação", 1, t.AppProprioImplantacao, t.AppProprioImplantacao);
                rev.AppProprioImplantacaoCobrada = true;
            }
            Item(f, TipoItemFaturaCanal.AppProprioMensal, "App próprio nas lojas — mensalidade", 1, t.AppProprioMensal, t.AppProprioMensal);
        }
        return Fechar(f);
    }

    private FaturaCanal FaturaParceiro(Canal par, Canal rev, ApuracaoUsoCanal ap, List<ApuracaoUsoTenant> detalhes, DateOnly comp)
    {
        var f = NovaFatura(TipoFaturaCanal.Parceiro, par.Id, rev.Id, comp, ap.Id);
        var func = detalhes.Where(d => d.EmpresaAtiva).Sum(d => d.FuncionariosAtivos);
        var (valor, ajuste) = CalculadoraUsoCanal.CalcularParceiro(func, par.PrecoFuncionarioParceiro!.Value, par.MinimoMensalParceiro);
        Item(f, TipoItemFaturaCanal.FuncionariosParceiro, "Funcionários ativos", func, par.PrecoFuncionarioParceiro.Value, valor);
        if (ajuste > 0) Item(f, TipoItemFaturaCanal.AjusteMinimo, $"Complemento ao mínimo mensal ({par.MinimoMensalParceiro:C})", 1, ajuste, ajuste);

        // Isenção por regra objetiva, avaliada sobre o snapshot da competência.
        var publicos = detalhes.Count(d => d.EmpresaAtiva && d.TipoEntidade == TipoEntidade.Publica);
        if (par.IsencaoMinClientesPublicos is { } n && publicos >= n)
        {
            var total = f.Itens.Sum(i => i.Valor);
            Item(f, TipoItemFaturaCanal.Isencao, $"Isenção: {publicos} clientes públicos ativos (mínimo {n})", 1, -total, -total);
            f.Observacao = "Isenta pela regra de clientes públicos ativos.";
        }
        return Fechar(f);
    }

    private FaturaCanal NovaFatura(TipoFaturaCanal tipo, Guid pagador, Guid recebedor, DateOnly comp, Guid apuracao) => new()
    {
        Tipo = tipo, CanalPagadorId = pagador, CanalRecebedorId = recebedor, Competencia = comp, ApuracaoId = apuracao,
        Vencimento = VencimentoDe(comp), CriadaEm = clock.UtcNow
    };

    private static void Item(FaturaCanal f, TipoItemFaturaCanal tipo, string desc, decimal qtd, decimal unit, decimal valor) =>
        f.Itens.Add(new ItemFaturaCanal { FaturaCanalId = f.Id, Tipo = tipo, Descricao = desc, Quantidade = qtd, ValorUnitario = unit, Valor = valor, Ordem = f.Itens.Count + 1 });

    private static FaturaCanal Fechar(FaturaCanal f)
    {
        f.Valor = f.Itens.Sum(i => i.Valor);
        f.Status = f.Valor <= 0 ? StatusFatura.NaoCobrada : StatusFatura.Aberta;
        return f;
    }

    // ---------------- Baixa manual (pagamento real entra na E3 — Asaas) ----------------

    private async Task<FaturaCanal> FaturaParaBaixaAsync(Guid faturaId, CancellationToken ct)
    {
        var f = await db.FaturasCanal.FirstOrDefaultAsync(x => x.Id == faturaId, ct) ?? throw new NaoEncontradoException("Fatura não encontrada.");
        // Plataforma dá baixa nas faturas que recebe; o revendedor, nas dos seus parceiros. Nunca na própria.
        var pode = ctx.IsSystem && ctx.CanalId is null ? f.CanalRecebedorId == Canal.OwnerRaizId
            : ctx.CanalId == f.CanalRecebedorId && usuario.Papel == Roles.AdminRevendedor;
        if (!pode) throw new AcessoNegadoException("Somente o recebedor registra a baixa desta fatura.");
        return f;
    }

    public async Task RegistrarPagamentoAsync(Guid faturaId, DateOnly pagaEm, decimal valorPago, string? forma, string? observacao, CancellationToken ct)
    {
        var f = await FaturaParaBaixaAsync(faturaId, ct);
        if (f.Status != StatusFatura.Aberta) throw new RegraNegocioException("A fatura não está em aberto.");
        if (valorPago <= 0) throw new RegraNegocioException("Valor pago inválido.");
        f.Status = StatusFatura.Paga;
        f.PagaEm = pagaEm;
        f.ValorPago = valorPago;
        f.FormaPagamento = forma;
        f.Observacao = observacao;
        f.AtualizadaEm = clock.UtcNow;
        audit.Registrar("fatura_canal.paga", nameof(FaturaCanal), f.Id, new { f.Competencia, f.CanalPagadorId, valorPago, pagaEm, forma });
        await db.SaveChangesAsync(ct);
        await AvaliarCanalAsync(f.CanalPagadorId, ct);
    }

    /// <summary>
    /// Baixa automática pelo webhook do gateway (modo sistema). A conta de origem do evento já foi conferida contra o
    /// recebedor da fatura. Idempotente: fatura já paga não muda. Reavalia a régua do pagador.
    /// </summary>
    public async Task<bool> BaixarViaGatewayAsync(Guid faturaId, DateOnly pagaEm, decimal valorPago, string forma, string origem, CancellationToken ct)
    {
        ExigirSistema();
        var f = await db.FaturasCanal.FirstOrDefaultAsync(x => x.Id == faturaId, ct) ?? throw new NaoEncontradoException("Fatura não encontrada.");
        if (f.Status != StatusFatura.Aberta || valorPago <= 0) return false;
        f.Status = StatusFatura.Paga;
        f.PagaEm = pagaEm;
        f.ValorPago = valorPago;
        f.FormaPagamento = forma.Length > 40 ? forma[..40] : forma;
        f.AtualizadaEm = clock.UtcNow;
        audit.Registrar("fatura_canal.paga_gateway", nameof(FaturaCanal), f.Id, new { f.Competencia, f.CanalPagadorId, f.CanalRecebedorId, valorPago, pagaEm, forma, origem });
        await db.SaveChangesAsync(ct);
        await AvaliarCanalAsync(f.CanalPagadorId, ct);
        return true;
    }

    /// <summary>Estorno informado pelo gateway (modo sistema): a fatura volta a ficar em aberto e a régua é reavaliada.</summary>
    public async Task<bool> EstornarViaGatewayAsync(Guid faturaId, string motivo, string origem, CancellationToken ct)
    {
        ExigirSistema();
        var f = await db.FaturasCanal.FirstOrDefaultAsync(x => x.Id == faturaId, ct) ?? throw new NaoEncontradoException("Fatura não encontrada.");
        if (f.Status != StatusFatura.Paga) return false;
        f.Status = StatusFatura.Aberta;
        f.PagaEm = null;
        f.ValorPago = null;
        f.FormaPagamento = null;
        f.Observacao = motivo;
        f.AtualizadaEm = clock.UtcNow;
        audit.Registrar("fatura_canal.estornada_gateway", nameof(FaturaCanal), f.Id, new { f.Competencia, f.CanalPagadorId, motivo, origem });
        await db.SaveChangesAsync(ct);
        await AvaliarCanalAsync(f.CanalPagadorId, ct);
        return true;
    }

    public async Task MarcarNaoCobradaAsync(Guid faturaId, string motivo, CancellationToken ct)
    {
        var f = await FaturaParaBaixaAsync(faturaId, ct);
        if (f.Status != StatusFatura.Aberta) throw new RegraNegocioException("A fatura não está em aberto.");
        if (string.IsNullOrWhiteSpace(motivo)) throw new RegraNegocioException("Informe o motivo da isenção.");
        f.Status = StatusFatura.NaoCobrada;
        f.Observacao = motivo.Trim();
        f.AtualizadaEm = clock.UtcNow;
        audit.Registrar("fatura_canal.nao_cobrada", nameof(FaturaCanal), f.Id, new { f.Competencia, f.CanalPagadorId, motivo });
        await db.SaveChangesAsync(ct);
        await AvaliarCanalAsync(f.CanalPagadorId, ct);
    }

    public async Task EstornarAsync(Guid faturaId, string motivo, CancellationToken ct)
    {
        var f = await FaturaParaBaixaAsync(faturaId, ct);
        if (f.Status is not (StatusFatura.Paga or StatusFatura.NaoCobrada)) throw new RegraNegocioException("A fatura não está paga.");
        f.Status = StatusFatura.Aberta;
        f.PagaEm = null;
        f.ValorPago = null;
        f.FormaPagamento = null;
        f.Observacao = motivo;
        f.AtualizadaEm = clock.UtcNow;
        audit.Registrar("fatura_canal.estornada", nameof(FaturaCanal), f.Id, new { f.Competencia, f.CanalPagadorId, motivo });
        await db.SaveChangesAsync(ct);
        await AvaliarCanalAsync(f.CanalPagadorId, ct);
    }

    // ---------------- Régua de canal ----------------

    public static StatusCanal StatusPorAtraso(int dias) => dias switch
    {
        >= DiasSuspensao => StatusCanal.Suspenso,
        >= DiasPainelBloqueado => StatusCanal.PainelBloqueado,
        >= DiasAviso => StatusCanal.Aviso,
        _ => StatusCanal.Ativo
    };

    /// <summary>Régua diária de todos os canais (rotina da plataforma).</summary>
    public async Task<int> ProcessarReguaAsync(CancellationToken ct)
    {
        ExigirPlataforma();
        var ids = await db.Canais.AsNoTracking().Where(c => c.Tipo != TipoCanal.Owner).Select(c => c.Id).ToListAsync(ct);
        var n = 0;
        foreach (var id in ids) if (await AvaliarCanalAsync(id, ct)) n++;
        return n;
    }

    /// <summary>
    /// Reavalia a situação do canal pela fatura vencida mais antiga. Regularizou → volta a Ativo. Situação definida
    /// manualmente (plataforma/ascendente) não é desfeita pela régua: ela só pode agravar.
    /// </summary>
    public async Task<bool> AvaliarCanalAsync(Guid canalId, CancellationToken ct)
    {
        var c = await db.Canais.FirstOrDefaultAsync(x => x.Id == canalId, ct);
        if (c is null || c.Tipo == TipoCanal.Owner) return false;
        var hoje = Hoje;
        var maisAntiga = await db.FaturasCanal.AsNoTracking()
            .Where(f => f.CanalPagadorId == canalId && f.Status == StatusFatura.Aberta && f.Vencimento < hoje)
            .OrderBy(f => f.Vencimento).Select(f => (DateOnly?)f.Vencimento).FirstOrDefaultAsync(ct);
        var alvo = maisAntiga is null ? StatusCanal.Ativo : StatusPorAtraso(hoje.DayNumber - maisAntiga.Value.DayNumber);

        var novo = alvo;
        if (c.Status != StatusCanal.Ativo && await SituacaoManualAsync(canalId, ct))
            novo = (StatusCanal)Math.Max((int)c.Status, (int)alvo);
        if (novo == c.Status) return false;

        audit.Registrar("canal.status_automatico", nameof(Canal), c.Id, new { anterior = c.Status, novo, faturaVencidaDesde = maisAntiga });
        c.Status = novo;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<bool> SituacaoManualAsync(Guid canalId, CancellationToken ct)
    {
        var id = canalId.ToString();
        var ultima = await db.AuditLogs.AsNoTracking()
            .Where(l => l.EntidadeId == id && l.Entidade == nameof(Canal) && (l.Acao == "canal.status_alterado" || l.Acao == "canal.status_automatico"))
            .OrderByDescending(l => l.Em).Select(l => l.Acao).FirstOrDefaultAsync(ct);
        return ultima == "canal.status_alterado";
    }

    // ---------------- Consultas ----------------

    public Task<List<FaturaCanal>> FaturasPagasPorAsync(Guid canalPagadorId, CancellationToken ct) =>
        db.FaturasCanal.AsNoTracking().Include(f => f.Itens).Where(f => f.CanalPagadorId == canalPagadorId)
            .OrderByDescending(f => f.Competencia).ToListAsync(ct);

    public Task<List<FaturaCanal>> FaturasRecebidasPorAsync(Guid canalRecebedorId, CancellationToken ct) =>
        db.FaturasCanal.AsNoTracking().Include(f => f.Itens).Where(f => f.CanalRecebedorId == canalRecebedorId)
            .OrderByDescending(f => f.Competencia).ToListAsync(ct);

    public Task<FaturaCanal?> FaturaAsync(Guid id, CancellationToken ct) =>
        db.FaturasCanal.AsNoTracking().Include(f => f.Itens.OrderBy(i => i.Ordem)).FirstOrDefaultAsync(f => f.Id == id, ct);

    /// <summary>Grade revendedores × competências do ano (tela da plataforma).</summary>
    public async Task<IReadOnlyList<LinhaGradeCanal>> GradeAsync(int ano, CancellationToken ct)
    {
        ExigirPlataforma();
        var ini = new DateOnly(ano, 1, 1);
        var hoje = Hoje;
        var revs = await db.Canais.AsNoTracking().Where(c => c.Tipo == TipoCanal.Revendedor).OrderBy(c => c.NomeMarca).ToListAsync(ct);
        var faturas = await db.FaturasCanal.AsNoTracking()
            .Where(f => f.CanalRecebedorId == Canal.OwnerRaizId && f.Tipo != TipoFaturaCanal.Avulsa && f.Competencia >= ini && f.Competencia < ini.AddYears(1)).ToListAsync(ct);
        var abertas = await db.FaturasCanal.AsNoTracking()
            .Where(f => f.CanalRecebedorId == Canal.OwnerRaizId && f.Status == StatusFatura.Aberta && f.Vencimento < hoje)
            .GroupBy(f => f.CanalPagadorId).Select(g => new { g.Key, V = g.Sum(f => f.Valor) }).ToDictionaryAsync(x => x.Key, x => x.V, ct);
        return revs.Select(r =>
        {
            var meses = new Dictionary<DateOnly, (CelulaFatura, Guid?, decimal)>();
            for (var m = 1; m <= 12; m++)
            {
                var comp = new DateOnly(ano, m, 1);
                var f = faturas.FirstOrDefault(x => x.CanalPagadorId == r.Id && x.Competencia == comp);
                meses[comp] = f is null ? (CelulaFatura.Vazia, null, 0m) : (Celula(f, hoje), f.Id, f.Valor);
            }
            return new LinhaGradeCanal(r.Id, r.NomeMarca, r.RazaoSocial, r.Cnpj, r.Status, meses, abertas.GetValueOrDefault(r.Id));
        }).ToList();
    }

    public static CelulaFatura Celula(FaturaCanal f, DateOnly hoje) => f.Status switch
    {
        StatusFatura.Paga => CelulaFatura.Paga,
        StatusFatura.NaoCobrada => CelulaFatura.NaoCobrada,
        StatusFatura.Cancelada => CelulaFatura.Cancelada,
        _ => f.Vencimento < hoje ? CelulaFatura.Vencida : CelulaFatura.Aberta
    };
}
