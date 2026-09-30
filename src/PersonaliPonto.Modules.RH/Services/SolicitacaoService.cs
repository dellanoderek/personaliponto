using Microsoft.EntityFrameworkCore;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Modules.RH.Domain;

namespace PersonaliPonto.Modules.RH.Services;

/// <summary>Fluxo de correção de ponto: funcionário solicita, RH aprova (gera tratamento) ou recusa.</summary>
public sealed class SolicitacaoService(IRhDbContext db, TratamentoService tratamentos, AuditService audit, ICurrentUser user, IClock clock, FechamentoService fechamentos)
{
    public async Task<SolicitacaoCorrecao> SolicitarAsync(Guid funcionarioId, TipoSolicitacao tipo, DateOnly data, TimeOnly? horario, Guid? registroRepId, string motivo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 5) throw new RegraNegocioException("Descreva o motivo (mínimo 5 caracteres).");
        if (motivo.Length > 150) throw new RegraNegocioException("Motivo com no máximo 150 caracteres.");
        var f = await db.Funcionarios.AsNoTracking().Include(x => x.Estabelecimento).FirstOrDefaultAsync(x => x.Id == funcionarioId, ct)
                ?? throw new NaoEncontradoException("Funcionário não encontrado.");
        var fuso = TimeZoneInfo.FindSystemTimeZoneById(f.Estabelecimento!.FusoHorario);
        var hoje = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, fuso).DateTime);
        if (data > hoje) throw new RegraNegocioException("Não é possível solicitar correção para data futura.");
        await fechamentos.GarantirAbertoAsync(funcionarioId, data, ct);

        if (tipo == TipoSolicitacao.Inclusao && horario is null) throw new RegraNegocioException("Informe o horário.");
        if (tipo == TipoSolicitacao.Desconsideracao)
        {
            if (registroRepId is null) throw new RegraNegocioException("Informe a marcação a desconsiderar.");
            if (!await db.RegistrosRep.AnyAsync(r => r.Id == registroRepId && r.FuncionarioId == funcionarioId, ct))
                throw new NaoEncontradoException("Marcação não encontrada.");
        }
        if (await db.Solicitacoes.AnyAsync(s => s.FuncionarioId == funcionarioId && s.Status == StatusSolicitacao.Pendente
                                                && s.Data == data && s.Horario == horario && s.RegistroRepId == registroRepId, ct))
            throw new RegraNegocioException("Já existe solicitação pendente igual.");

        var s = new SolicitacaoCorrecao
        {
            TenantId = f.TenantId,
            FuncionarioId = funcionarioId,
            Tipo = tipo,
            Data = data,
            Horario = horario,
            RegistroRepId = registroRepId,
            Motivo = motivo.Trim(),
            Status = StatusSolicitacao.Pendente,
            CriadaEm = clock.UtcNow
        };
        db.Solicitacoes.Add(s);
        audit.Registrar("solicitacao.criada", nameof(SolicitacaoCorrecao), s.Id, new { s.Tipo, s.Data, s.Horario });
        await db.SaveChangesAsync(ct);
        return s;
    }

    public async Task AprovarAsync(Guid solicitacaoId, string? resposta, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var s = await Pendente(solicitacaoId, ct);
        await fechamentos.GarantirAbertoAsync(s.FuncionarioId, s.Data, ct);
        Tratamento t;
        if (s.Tipo == TipoSolicitacao.Inclusao)
        {
            var f = await db.Funcionarios.AsNoTracking().Include(x => x.Estabelecimento).FirstAsync(x => x.Id == s.FuncionarioId, ct);
            var fuso = TimeZoneInfo.FindSystemTimeZoneById(f.Estabelecimento!.FusoHorario);
            var local = s.Data.ToDateTime(s.Horario!.Value);
            var instante = new DateTimeOffset(local, fuso.GetUtcOffset(local));
            t = await tratamentos.IncluirAsync(s.FuncionarioId, instante, s.Motivo, s.Id, ct);
        }
        else
        {
            t = await tratamentos.DesconsiderarAsync(s.RegistroRepId!.Value, s.Motivo, ct);
        }
        s.Status = StatusSolicitacao.Aprovada;
        s.TratamentoId = t.Id;
        Responder(s, resposta);
        audit.Registrar("solicitacao.aprovada", nameof(SolicitacaoCorrecao), s.Id, new { s.TratamentoId, resposta });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task RecusarAsync(Guid solicitacaoId, string resposta, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(resposta)) throw new RegraNegocioException("Informe o motivo da recusa.");
        var s = await Pendente(solicitacaoId, ct);
        s.Status = StatusSolicitacao.Recusada;
        Responder(s, resposta);
        audit.Registrar("solicitacao.recusada", nameof(SolicitacaoCorrecao), s.Id, new { resposta });
        await db.SaveChangesAsync(ct);
    }

    public async Task CancelarAsync(Guid solicitacaoId, Guid funcionarioId, CancellationToken ct)
    {
        var s = await Pendente(solicitacaoId, ct);
        if (s.FuncionarioId != funcionarioId) throw new NaoEncontradoException("Solicitação não encontrada.");
        s.Status = StatusSolicitacao.Cancelada;
        audit.Registrar("solicitacao.cancelada", nameof(SolicitacaoCorrecao), s.Id);
        await db.SaveChangesAsync(ct);
    }

    private void Responder(SolicitacaoCorrecao s, string? resposta)
    {
        s.RespostaRh = resposta?.Trim();
        s.RespondidoPor = user.UserId;
        s.RespondidoEm = clock.UtcNow;
    }

    private async Task<SolicitacaoCorrecao> Pendente(Guid id, CancellationToken ct)
    {
        var s = await db.Solicitacoes.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NaoEncontradoException("Solicitação não encontrada.");
        if (s.Status != StatusSolicitacao.Pendente) throw new RegraNegocioException("Solicitação já respondida.");
        return s;
    }
}
