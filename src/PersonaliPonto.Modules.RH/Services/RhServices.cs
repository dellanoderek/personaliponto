using Microsoft.EntityFrameworkCore;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Aej;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Modules.RH.Domain;

namespace PersonaliPonto.Modules.RH.Services;

public sealed class BancoHorasService(IRhDbContext db, AuditService audit, ICurrentUser user, IClock clock)
{
    public Task<int> SaldoAsync(Guid funcionarioId, DateOnly? ate, CancellationToken ct) =>
        db.BancoHoras.Where(l => l.FuncionarioId == funcionarioId && (ate == null || l.Data <= ate)).SumAsync(l => l.Minutos, ct);

    public Task<List<BancoHorasLancamento>> ExtratoAsync(Guid funcionarioId, CancellationToken ct) =>
        db.BancoHoras.AsNoTracking().Where(l => l.FuncionarioId == funcionarioId).OrderByDescending(l => l.Data).ThenByDescending(l => l.CriadoEm).ToListAsync(ct);

    public async Task<BancoHorasLancamento> LancarAsync(Guid funcionarioId, DateOnly data, int minutos, TipoLancamentoBancoHoras tipo, string descricao, CancellationToken ct)
    {
        if (minutos == 0) throw new RegraNegocioException("Informe a quantidade de minutos.");
        if (tipo is TipoLancamentoBancoHoras.CreditoApuracao or TipoLancamentoBancoHoras.DebitoApuracao or TipoLancamentoBancoHoras.Estorno)
            throw new RegraNegocioException("Tipo de lançamento reservado ao fechamento.");
        if (string.IsNullOrWhiteSpace(descricao)) throw new RegraNegocioException("Descreva o lançamento.");
        if (tipo is TipoLancamentoBancoHoras.Compensacao or TipoLancamentoBancoHoras.Pagamento && minutos > 0) minutos = -minutos;
        var f = await db.Funcionarios.AsNoTracking().FirstOrDefaultAsync(x => x.Id == funcionarioId, ct) ?? throw new NaoEncontradoException("Funcionário não encontrado.");
        var l = new BancoHorasLancamento
        {
            TenantId = f.TenantId, FuncionarioId = f.Id, Data = data, Minutos = minutos, Tipo = tipo, Descricao = descricao.Trim(),
            CriadoPor = user.UserId, CriadoPorNome = user.Nome, CriadoEm = clock.UtcNow
        };
        db.BancoHoras.Add(l);
        audit.Registrar("bancohoras.lancamento", nameof(BancoHorasLancamento), l.Id, new { funcionarioId, data, minutos, tipo, descricao });
        await db.SaveChangesAsync(ct);
        return l;
    }

    public async Task<BancoHorasLancamento> EstornarAsync(Guid lancamentoId, string motivo, CancellationToken ct)
    {
        var l = await db.BancoHoras.AsNoTracking().FirstOrDefaultAsync(x => x.Id == lancamentoId, ct) ?? throw new NaoEncontradoException("Lançamento não encontrado.");
        if (l.Tipo == TipoLancamentoBancoHoras.Estorno || l.FechamentoId is not null)
            throw new RegraNegocioException("Lançamentos de fechamento são estornados pela reabertura do período.");
        if (await db.BancoHoras.AnyAsync(x => x.EstornaLancamentoId == l.Id, ct)) throw new RegraNegocioException("Lançamento já estornado.");
        var e = new BancoHorasLancamento
        {
            TenantId = l.TenantId, FuncionarioId = l.FuncionarioId, Data = l.Data, Minutos = -l.Minutos, Tipo = TipoLancamentoBancoHoras.Estorno,
            Descricao = $"Estorno: {motivo}", EstornaLancamentoId = l.Id, CriadoPor = user.UserId, CriadoPorNome = user.Nome, CriadoEm = clock.UtcNow
        };
        db.BancoHoras.Add(e);
        audit.Registrar("bancohoras.estorno", nameof(BancoHorasLancamento), l.Id, new { motivo });
        await db.SaveChangesAsync(ct);
        return e;
    }
}

public sealed class AtestadoService(IRhDbContext db, ArquivoService arquivos, AuditService audit, ICurrentUser user, IClock clock, FechamentoService fechamentos)
{
    public const string Bucket = "atestados";

    public async Task<Atestado> EnviarAsync(Guid funcionarioId, TipoAtestado tipo, DateOnly inicio, DateOnly fim, int? minutosParciais,
        string? observacao, string? nomeArquivo, byte[]? arquivo, CancellationToken ct)
    {
        if (fim < inicio) throw new RegraNegocioException("Data final anterior à inicial.");
        if (fim.DayNumber - inicio.DayNumber > 365) throw new RegraNegocioException("Período máximo de 1 ano.");
        if (minutosParciais is <= 0 or > 1440) throw new RegraNegocioException("Minutos parciais inválidos.");
        var f = await db.Funcionarios.AsNoTracking().FirstOrDefaultAsync(x => x.Id == funcionarioId, ct) ?? throw new NaoEncontradoException("Funcionário não encontrado.");
        Guid? arquivoId = null;
        if (arquivo is { Length: > 0 }) arquivoId = (await arquivos.EnviarAsync(Bucket, nomeArquivo ?? "atestado", arquivo, ct)).Id;
        else if (tipo == TipoAtestado.AtestadoMedico) throw new RegraNegocioException("Anexe o atestado.");

        var a = new Atestado
        {
            TenantId = f.TenantId, FuncionarioId = f.Id, Tipo = tipo, DataInicio = inicio, DataFim = fim, MinutosParciais = minutosParciais,
            Observacao = observacao?.Trim(), ArquivoId = arquivoId, Status = StatusSolicitacao.Pendente, CriadoEm = clock.UtcNow, CriadoPor = user.UserId
        };
        db.Atestados.Add(a);
        audit.Registrar("atestado.enviado", nameof(Atestado), a.Id, new { funcionarioId, tipo, inicio, fim });
        await db.SaveChangesAsync(ct);
        return a;
    }

    public async Task AnalisarAsync(Guid atestadoId, bool aprovar, string? parecer, CancellationToken ct)
    {
        var a = await db.Atestados.FirstOrDefaultAsync(x => x.Id == atestadoId, ct) ?? throw new NaoEncontradoException("Atestado não encontrado.");
        if (a.Status != StatusSolicitacao.Pendente) throw new RegraNegocioException("Atestado já analisado.");
        if (!aprovar && string.IsNullOrWhiteSpace(parecer)) throw new RegraNegocioException("Informe o motivo da recusa.");
        for (var d = a.DataInicio; d <= a.DataFim; d = d.AddDays(1)) await fechamentos.GarantirAbertoAsync(a.FuncionarioId, d, ct);
        a.Status = aprovar ? StatusSolicitacao.Aprovada : StatusSolicitacao.Recusada;
        a.ParecerRh = parecer?.Trim();
        a.AnalisadoPor = user.UserId;
        a.AnalisadoEm = clock.UtcNow;
        audit.Registrar(aprovar ? "atestado.aprovado" : "atestado.recusado", nameof(Atestado), a.Id, new { parecer });
        await db.SaveChangesAsync(ct);
    }
}

public sealed class FeriadoService(IRhDbContext db, AuditService audit, ITenantContext tenant)
{
    public async Task<Feriado> SalvarAsync(Feriado dados, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dados.Descricao)) throw new RegraNegocioException("Descrição obrigatória.");
        var existente = dados.Id == Guid.Empty ? null : await db.Feriados.FirstOrDefaultAsync(x => x.Id == dados.Id, ct);
        var f = existente ?? new Feriado { TenantId = tenant.TenantId ?? throw new RegraNegocioException("Operação exige um tenant.") };
        f.Data = dados.Data;
        f.Descricao = dados.Descricao.Trim();
        f.Abrangencia = dados.Abrangencia;
        f.EstabelecimentoId = dados.EstabelecimentoId;
        if (existente is null) db.Feriados.Add(f);
        audit.Registrar("feriado.salvo", nameof(Feriado), f.Id, new { f.Data, f.Descricao });
        await db.SaveChangesAsync(ct);
        return f;
    }

    public async Task ExcluirAsync(Guid id, CancellationToken ct)
    {
        var f = await db.Feriados.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NaoEncontradoException("Feriado não encontrado.");
        db.Feriados.Remove(f);
        audit.Registrar("feriado.excluido", nameof(Feriado), f.Id, new { f.Data, f.Descricao });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Cadastra os feriados nacionais do ano que ainda não existem.</summary>
    public async Task<int> GerarNacionaisAsync(int ano, CancellationToken ct)
    {
        var tenantId = tenant.TenantId ?? throw new RegraNegocioException("Operação exige um tenant.");
        var existentes = await db.Feriados.Where(f => f.Data.Year == ano && f.EstabelecimentoId == null).Select(f => f.Data).ToListAsync(ct);
        var novos = FeriadosNacionais(ano).Where(x => !existentes.Contains(x.Data)).ToList();
        foreach (var (data, desc) in novos)
            db.Feriados.Add(new Feriado { TenantId = tenantId, Data = data, Descricao = desc, Abrangencia = AbrangenciaFeriado.Nacional });
        audit.Registrar("feriado.nacionais_gerados", nameof(Feriado), ano, new { novos.Count });
        await db.SaveChangesAsync(ct);
        return novos.Count;
    }

    /// <summary>Feriados nacionais (Lei 662/1949, Lei 6.802/1980, Lei 14.759/2023) + Sexta-feira Santa.</summary>
    public static IReadOnlyList<(DateOnly Data, string Descricao)> FeriadosNacionais(int ano)
    {
        var pascoa = Pascoa(ano);
        return
        [
            (new DateOnly(ano, 1, 1), "Confraternização Universal"),
            (pascoa.AddDays(-2), "Sexta-feira Santa"),
            (new DateOnly(ano, 4, 21), "Tiradentes"),
            (new DateOnly(ano, 5, 1), "Dia do Trabalho"),
            (new DateOnly(ano, 9, 7), "Independência do Brasil"),
            (new DateOnly(ano, 10, 12), "Nossa Senhora Aparecida"),
            (new DateOnly(ano, 11, 2), "Finados"),
            (new DateOnly(ano, 11, 15), "Proclamação da República"),
            (new DateOnly(ano, 11, 20), "Dia Nacional de Zumbi e da Consciência Negra"),
            (new DateOnly(ano, 12, 25), "Natal")
        ];
    }

    /// <summary>Domingo de Páscoa (algoritmo de Meeus/Jones/Butcher).</summary>
    public static DateOnly Pascoa(int ano)
    {
        int a = ano % 19, b = ano / 100, c = ano % 100, d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30, i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451, mes = (h + l - 7 * m + 114) / 31, dia = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(ano, mes, dia);
    }
}

public sealed class EscalaService(IRhDbContext db, AuditService audit, IClock clock)
{
    /// <summary>Atribui uma jornada a partir de uma data, encerrando a escala anterior no dia anterior.</summary>
    public async Task<EscalaFuncionario> AtribuirAsync(Guid funcionarioId, Guid jornadaId, DateOnly inicio, CancellationToken ct)
    {
        var f = await db.Funcionarios.FirstOrDefaultAsync(x => x.Id == funcionarioId, ct) ?? throw new NaoEncontradoException("Funcionário não encontrado.");
        if (!await db.Jornadas.AnyAsync(j => j.Id == jornadaId, ct)) throw new NaoEncontradoException("Jornada não encontrada.");
        if (await db.Escalas.AnyAsync(e => e.FuncionarioId == funcionarioId && e.Inicio >= inicio, ct))
            throw new RegraNegocioException("Já existe escala iniciando nesta data ou depois.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var vigente = await db.Escalas.Where(e => e.FuncionarioId == funcionarioId && e.Fim == null).FirstOrDefaultAsync(ct);
        if (vigente is not null) vigente.Fim = inicio.AddDays(-1);
        var nova = new EscalaFuncionario { TenantId = f.TenantId, FuncionarioId = f.Id, JornadaId = jornadaId, Inicio = inicio, CriadoEm = clock.UtcNow };
        db.Escalas.Add(nova);
        f.JornadaId = jornadaId;
        audit.Registrar("escala.atribuida", nameof(EscalaFuncionario), nova.Id, new { funcionarioId, jornadaId, inicio });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return nova;
    }
}

/// <summary>Fornece ao motor de apuração os dados do RH: feriados, abonos, escalas e banco de horas.</summary>
public sealed class RhApuracaoComplementos(IRhDbContext db) : IApuracaoComplementos
{
    public async Task<ComplementosApuracao> ObterAsync(Funcionario f, DateOnly inicio, DateOnly fim, CancellationToken ct)
    {
        var feriados = await db.Feriados.AsNoTracking()
            .Where(x => x.Data >= inicio && x.Data <= fim && (x.EstabelecimentoId == null || x.EstabelecimentoId == f.EstabelecimentoId))
            .Select(x => x.Data).ToListAsync(ct);

        var atestados = await db.Atestados.AsNoTracking()
            .Where(a => a.FuncionarioId == f.Id && a.Status == StatusSolicitacao.Aprovada && a.MinutosParciais == null
                        && a.DataInicio <= fim && a.DataFim >= inicio)
            .ToListAsync(ct);
        var abonos = new Dictionary<DateOnly, string>();
        foreach (var a in atestados)
            for (var d = a.DataInicio; d <= a.DataFim; d = d.AddDays(1))
                abonos[d] = a.Tipo switch
                {
                    TipoAtestado.AtestadoMedico => "Atestado médico",
                    TipoAtestado.LicencaLegal => "Licença",
                    TipoAtestado.DeclaracaoComparecimento => "Declaração de comparecimento",
                    _ => "Ausência justificada"
                };

        var escalas = await db.Escalas.AsNoTracking().Where(e => e.FuncionarioId == f.Id && e.Inicio <= fim && (e.Fim == null || e.Fim >= inicio)).ToListAsync(ct);
        Func<DateOnly, Jornada?>? jornadaDoDia = null;
        if (escalas.Count > 0)
        {
            var ids = escalas.Select(e => e.JornadaId).Distinct().ToList();
            var jornadas = await db.Jornadas.AsNoTracking().Where(j => ids.Contains(j.Id)).ToDictionaryAsync(j => j.Id, ct);
            var padrao = f.Jornada;
            jornadaDoDia = d => escalas.FirstOrDefault(e => e.Inicio <= d && (e.Fim == null || e.Fim >= d)) is { } e
                ? jornadas.GetValueOrDefault(e.JornadaId) : padrao;
        }

        var movimentos = await db.BancoHoras.AsNoTracking()
            .Where(l => l.FuncionarioId == f.Id && l.Data >= inicio && l.Data <= fim)
            .Select(l => new { l.Data, l.Minutos }).ToListAsync(ct);
        var aej = movimentos.Where(m => m.Minutos != 0)
            .Select(m => new AejAusencia(m.Data, TipoAusencia.BancoHoras, Math.Abs(m.Minutos), m.Minutos > 0 ? 1 : 2)).ToList();

        var saldo = await db.BancoHoras.Where(l => l.FuncionarioId == f.Id && l.Data <= fim).SumAsync(l => (int?)l.Minutos, ct) ?? 0;
        return new ComplementosApuracao(feriados.ToHashSet(), abonos, jornadaDoDia, aej, saldo);
    }
}

/// <summary>Geofence (Fase 2): sinaliza ou bloqueia marcações fora do raio configurado do estabelecimento.</summary>
public sealed class GeofenceInterceptor(IRhDbContext db) : IMarcacaoInterceptor
{
    public async Task<MarcacaoAvaliacao> AvaliarAsync(MarcacaoContextoEntrada e, CancellationToken ct)
    {
        var est = await db.Estabelecimentos.AsNoTracking().Where(x => x.Id == e.EstabelecimentoId)
            .Select(x => new { x.Latitude, x.Longitude, x.RaioCercaMetros, x.ModoCerca }).FirstAsync(ct);
        var modoFunc = await db.Funcionarios.AsNoTracking().Where(x => x.Id == e.FuncionarioId).Select(x => x.ModoCercaFuncionario).FirstAsync(ct);
        var modo = modoFunc ?? est.ModoCerca;
        if (modo == ModoCerca.Desativada || est.Latitude is null || est.Longitude is null || est.RaioCercaMetros is null)
            return MarcacaoAvaliacao.Livre;
        // O terminal fixo do estabelecimento não envia localização; a cerca vale para coletores móveis.
        if (e.Coletor == Shared.Contracts.Coletor.Browser && e.Latitude is null) return MarcacaoAvaliacao.Livre;

        if (e.Latitude is null || e.Longitude is null)
            return modo == ModoCerca.Bloquear
                ? new MarcacaoAvaliacao(true, null, "Ative a localização do dispositivo para registrar o ponto.")
                : new MarcacaoAvaliacao(false, null, null);

        var distancia = DistanciaMetros(est.Latitude.Value, est.Longitude.Value, e.Latitude.Value, e.Longitude.Value);
        var dentro = distancia <= est.RaioCercaMetros.Value;
        if (!dentro && modo == ModoCerca.Bloquear)
            return new MarcacaoAvaliacao(true, false, $"Você está a {distancia:0} m do local de trabalho (limite {est.RaioCercaMetros} m).");
        return new MarcacaoAvaliacao(false, dentro, null);
    }

    public static double DistanciaMetros(double lat1, double lon1, double lat2, double lon2)
    {
        const double r = 6_371_000;
        double Rad(double g) => g * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * r * Math.Asin(Math.Sqrt(a));
    }
}
