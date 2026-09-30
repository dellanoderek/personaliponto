using Microsoft.EntityFrameworkCore;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Domain;

namespace PersonaliPonto.Core.RepP.Services;

/// <summary>
/// Módulo de Tratamento (PTRP — art. 82, parágrafo único): acrescenta informações para complementar
/// omissões ou indicar marcações indevidas. O registro original nunca é alterado ou excluído.
/// </summary>
public sealed class TratamentoService(IRepPDbContext db, AuditService audit, ICurrentUser user, IClock clock, RegistroRepService registros)
{
    public async Task<Tratamento> IncluirAsync(Guid funcionarioId, DateTimeOffset dataHora, string motivo, Guid? solicitacaoId, CancellationToken ct)
    {
        var f = await Funcionario(funcionarioId, ct);
        ValidarMotivo(motivo);
        if (dataHora > clock.UtcNow) throw new RegraNegocioException("Não é possível incluir marcação no futuro.");
        var fuso = await registros.FusoAsync(f.EstabelecimentoId, ct);
        return await Gravar(new Tratamento
        {
            TenantId = f.TenantId,
            FuncionarioId = f.Id,
            Tipo = TipoTratamento.Inclusao,
            DataHora = dataHora,
            OffsetMinutos = RegistroRepService.Offset(fuso, dataHora),
            Motivo = motivo.Trim(),
            SolicitacaoId = solicitacaoId
        }, ct);
    }

    public async Task<Tratamento> PreAssinalarAsync(Guid funcionarioId, DateTimeOffset dataHora, string motivo, CancellationToken ct)
    {
        var f = await Funcionario(funcionarioId, ct);
        ValidarMotivo(motivo);
        var fuso = await registros.FusoAsync(f.EstabelecimentoId, ct);
        return await Gravar(new Tratamento
        {
            TenantId = f.TenantId,
            FuncionarioId = f.Id,
            Tipo = TipoTratamento.PreAssinalada,
            DataHora = dataHora,
            OffsetMinutos = RegistroRepService.Offset(fuso, dataHora),
            Motivo = motivo.Trim()
        }, ct);
    }

    public async Task<Tratamento> DesconsiderarAsync(Guid registroRepId, string motivo, CancellationToken ct)
    {
        ValidarMotivo(motivo);
        var reg = await db.RegistrosRep.AsNoTracking().FirstOrDefaultAsync(r => r.Id == registroRepId && r.Tipo == TipoRegistroRep.MarcacaoRepP, ct)
                  ?? throw new NaoEncontradoException("Marcação não encontrada.");
        if (await AtivosQuery().AnyAsync(t => t.Tipo == TipoTratamento.Desconsideracao && t.RegistroRepId == registroRepId, ct))
            throw new RegraNegocioException("Marcação já está desconsiderada.");
        return await Gravar(new Tratamento
        {
            TenantId = reg.TenantId,
            FuncionarioId = reg.FuncionarioId!.Value,
            Tipo = TipoTratamento.Desconsideracao,
            RegistroRepId = reg.Id,
            Motivo = motivo.Trim()
        }, ct);
    }

    /// <summary>Revoga um tratamento (inclusão, pré-assinalação ou desconsideração) mantendo o histórico.</summary>
    public async Task<Tratamento> RevogarAsync(Guid tratamentoId, string motivo, CancellationToken ct)
    {
        ValidarMotivo(motivo);
        var alvo = await AtivosQuery().FirstOrDefaultAsync(t => t.Id == tratamentoId, ct)
                   ?? throw new NaoEncontradoException("Tratamento não encontrado ou já revogado.");
        if (alvo.Tipo == TipoTratamento.Revogacao) throw new RegraNegocioException("Revogação não pode ser revogada.");
        return await Gravar(new Tratamento
        {
            TenantId = alvo.TenantId,
            FuncionarioId = alvo.FuncionarioId,
            Tipo = TipoTratamento.Revogacao,
            TratamentoRevogadoId = alvo.Id,
            Motivo = motivo.Trim()
        }, ct);
    }

    /// <summary>Tratamentos vigentes (não revogados).</summary>
    public IQueryable<Tratamento> AtivosQuery() =>
        db.Tratamentos.AsNoTracking().Where(t => t.Tipo != TipoTratamento.Revogacao
            && !db.Tratamentos.Any(r => r.Tipo == TipoTratamento.Revogacao && r.TratamentoRevogadoId == t.Id));

    private async Task<Tratamento> Gravar(Tratamento t, CancellationToken ct)
    {
        t.UsuarioId = user.UserId;
        t.UsuarioNome = user.Nome;
        t.CriadoEm = clock.UtcNow;
        db.Tratamentos.Add(t);
        audit.Registrar($"tratamento.{t.Tipo.ToString().ToLowerInvariant()}", nameof(Tratamento), t.Id,
            new { t.FuncionarioId, t.RegistroRepId, t.TratamentoRevogadoId, t.DataHora, t.Motivo });
        await db.SaveChangesAsync(ct);
        return t;
    }

    private async Task<Funcionario> Funcionario(Guid id, CancellationToken ct) =>
        await db.Funcionarios.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct)
        ?? throw new NaoEncontradoException("Funcionário não encontrado.");

    private static void ValidarMotivo(string motivo)
    {
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 5)
            throw new RegraNegocioException("Informe o motivo (mínimo 5 caracteres).");
        if (motivo.Length > 150) throw new RegraNegocioException("Motivo com no máximo 150 caracteres (limite do AEJ).");
    }
}
