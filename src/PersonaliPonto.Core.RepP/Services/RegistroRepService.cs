using Microsoft.EntityFrameworkCore;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Afd;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Formatacao;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Core.RepP.Services;

/// <summary>
/// Único ponto de escrita do Armazenamento de Registro de Ponto (ARP).
/// Aloca o NSR do estabelecimento, calcula o hash encadeado (tipo 7) e grava o registro imutável,
/// tudo na mesma transação — o NSR nunca tem lacunas nem duplicidade.
/// </summary>
public sealed class RegistroRepService(IRepPDbContext db, INsrAllocator nsr, IClock clock)
{
    public async Task<RegistroRep> GravarAsync(RegistroRep registro, CancellationToken ct)
    {
        if (registro.TenantId == Guid.Empty || registro.EstabelecimentoId == Guid.Empty)
            throw new InvalidOperationException("Registro sem tenant/estabelecimento.");

        var propria = db.Database.CurrentTransaction is null;
        var tx = propria ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            var fuso = await FusoAsync(registro.EstabelecimentoId, ct);
            var agora = Milissegundos(clock.UtcNow);
            registro.DataHoraGravacao = agora;
            if (registro.DataHoraMarcacao is { } dm) registro.DataHoraMarcacao = Milissegundos(dm);
            registro.OffsetGravacaoMinutos = Offset(fuso, agora);

            var reservado = await nsr.ProximoAsync(registro.TenantId, registro.EstabelecimentoId, ct);
            registro.Nsr = reservado.Nsr;

            if (registro.Tipo == TipoRegistroRep.MarcacaoRepP)
            {
                registro.OffsetMarcacaoMinutos ??= Offset(fuso, registro.DataHoraMarcacao!.Value);
                registro.Cpf = Documentos.Normalizar(registro.Cpf);
                registro.Hash = AfdGenerator.CalcularHashTipo7(registro, reservado.UltimoHashTipo7);
                await nsr.AtualizarUltimoHashAsync(registro.EstabelecimentoId, registro.Hash, ct);
            }

            db.RegistrosRep.Add(registro);
            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
            return registro;
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            throw;
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync();
        }
    }

    /// <summary>Registro tipo 2 — inclusão/alteração da identificação da empresa no REP.</summary>
    public Task<RegistroRep> RegistrarEmpresaAsync(Estabelecimento e, Empregador emp, string? cpfResponsavel, CancellationToken ct) =>
        GravarAsync(new RegistroRep
        {
            TenantId = e.TenantId,
            EstabelecimentoId = e.Id,
            Tipo = TipoRegistroRep.InclusaoAlteracaoEmpresa,
            CpfResponsavel = Documentos.Normalizar(cpfResponsavel),
            TipoIdentificadorEmpregador = e.TipoIdentificador,
            IdentificadorEmpregador = Documentos.Normalizar(e.Identificador),
            CnoCaepf = Documentos.Normalizar(e.CnoCaepf),
            RazaoSocial = emp.RazaoSocial,
            LocalPrestacao = e.LocalPrestacao
        }, ct);

    /// <summary>Registro tipo 5 — inclusão (I), alteração (A) ou exclusão (E) de empregado no REP.</summary>
    public Task<RegistroRep> RegistrarEmpregadoAsync(Funcionario f, char operacao, string? cpfResponsavel, CancellationToken ct) =>
        GravarAsync(new RegistroRep
        {
            TenantId = f.TenantId,
            EstabelecimentoId = f.EstabelecimentoId,
            Tipo = TipoRegistroRep.InclusaoAlteracaoExclusaoEmpregado,
            TipoOperacao = operacao,
            Cpf = Documentos.Normalizar(f.Cpf),
            NomeEmpregado = f.Nome,
            DemaisDados = "",
            CpfResponsavel = Documentos.Normalizar(cpfResponsavel)
        }, ct);

    /// <summary>Registro tipo 6 — eventos sensíveis ("02" retorno de energia, "07"/"08" disponibilidade de serviço).</summary>
    public Task<RegistroRep> RegistrarEventoAsync(Guid tenantId, Guid estabelecimentoId, int tipoEvento, CancellationToken ct) =>
        GravarAsync(new RegistroRep
        {
            TenantId = tenantId,
            EstabelecimentoId = estabelecimentoId,
            Tipo = TipoRegistroRep.EventoSensivel,
            TipoEvento = tipoEvento
        }, ct);

    public Task<RegistroRep> RegistrarMarcacaoAsync(
        Guid tenantId, Guid estabelecimentoId, Funcionario f, DateTimeOffset dataHoraMarcacao,
        Coletor coletor, bool offline, Guid clientId, CancellationToken ct) =>
        GravarAsync(new RegistroRep
        {
            TenantId = tenantId,
            EstabelecimentoId = estabelecimentoId,
            Tipo = TipoRegistroRep.MarcacaoRepP,
            FuncionarioId = f.Id,
            Cpf = f.Cpf,
            DataHoraMarcacao = dataHoraMarcacao,
            Coletor = coletor,
            Offline = offline,
            ClientId = clientId
        }, ct);

    private readonly Dictionary<Guid, TimeZoneInfo> _fusos = new();

    public async Task<TimeZoneInfo> FusoAsync(Guid estabelecimentoId, CancellationToken ct)
    {
        if (_fusos.TryGetValue(estabelecimentoId, out var f)) return f;
        var id = await db.Estabelecimentos.Where(e => e.Id == estabelecimentoId).Select(e => e.FusoHorario).FirstAsync(ct);
        return _fusos[estabelecimentoId] = TimeZoneInfo.FindSystemTimeZoneById(id);
    }

    /// <summary>O PostgreSQL guarda microssegundos; normaliza para milissegundos antes de gravar.</summary>
    public static DateTimeOffset Milissegundos(DateTimeOffset d) => new(d.Ticks - d.Ticks % TimeSpan.TicksPerMillisecond, d.Offset);

    public static int Offset(TimeZoneInfo fuso, DateTimeOffset instante) => (int)fuso.GetUtcOffset(instante).TotalMinutes;
}
