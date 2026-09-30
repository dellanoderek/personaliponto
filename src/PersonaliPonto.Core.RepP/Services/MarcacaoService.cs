using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Formatacao;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Core.RepP.Services;

/// <summary>
/// Fluxo de marcação de ponto (Anexo IX, item 8): identifica o trabalhador, obtém a hora oficial do
/// servidor, grava no ARP e disponibiliza o comprovante. Idempotente por ClientId (UUID do coletor).
/// </summary>
public sealed class MarcacaoService(
    IRepPDbContext db,
    RegistroRepService registros,
    IClock clock,
    IOptions<RepPOptions> options,
    IEnumerable<IMarcacaoInterceptor> interceptores)
{
    private readonly RepPOptions _opt = options.Value;

    public async Task<AncoraHoraDto> EmitirAncoraAsync(Guid tenantId, Guid estabelecimentoId, Guid? funcionarioId, Guid? terminalId, CancellationToken ct)
    {
        var agora = clock.UtcNow;
        var ancora = new AncoraHora { TenantId = tenantId, FuncionarioId = funcionarioId, TerminalId = terminalId, HoraServidor = agora };
        db.AncorasHora.Add(ancora);
        await db.SaveChangesAsync(ct);
        var fuso = await registros.FusoAsync(estabelecimentoId, ct);
        return new AncoraHoraDto(ancora.Id, agora, fuso.Id, RegistroRepService.Offset(fuso, agora));
    }

    /// <summary>Identificação no terminal: matrícula ou CPF + PIN, restrita ao estabelecimento do terminal.</summary>
    public async Task<Funcionario> IdentificarPorPinAsync(Guid estabelecimentoId, string identificacao, string pin, CancellationToken ct)
    {
        var id = identificacao.Trim();
        var cpf = Documentos.Normalizar(id);
        var f = await db.Funcionarios.FirstOrDefaultAsync(x =>
            x.EstabelecimentoId == estabelecimentoId && x.Ativo && (x.Matricula == id || x.Cpf == cpf), ct);
        if (f is null) throw new AcessoNegadoException("Matrícula/CPF ou PIN inválido.");

        var agora = clock.UtcNow;
        if (f.PinBloqueadoAte is { } ate && ate > agora)
            throw new AcessoNegadoException($"PIN bloqueado por excesso de tentativas. Tente novamente às {ate.ToLocalTime():HH:mm}.", true);

        if (!SecretHasher.Verificar(pin, f.PinHash))
        {
            f.PinTentativasFalhas++;
            if (f.PinTentativasFalhas >= _opt.PinMaximoTentativas)
            {
                f.PinBloqueadoAte = agora.AddMinutes(_opt.PinBloqueioMinutos);
                f.PinTentativasFalhas = 0;
            }
            await db.SaveChangesAsync(ct);
            throw new AcessoNegadoException("Matrícula/CPF ou PIN inválido.");
        }

        if (f.PinTentativasFalhas != 0 || f.PinBloqueadoAte is not null)
        {
            f.PinTentativasFalhas = 0;
            f.PinBloqueadoAte = null;
            await db.SaveChangesAsync(ct);
        }
        return f;
    }

    public async Task<MarcacaoRegistradaDto> RegistrarAsync(
        Funcionario funcionario,
        RegistrarMarcacaoRequest req,
        Coletor coletor,
        Guid? terminalId,
        string? ip,
        CancellationToken ct)
    {
        if (req.ClientId == Guid.Empty) throw new RegraNegocioException("ClientId obrigatório.");
        if (!funcionario.Ativo) throw new RegraNegocioException("Funcionário inativo.");
        if (coletor == Coletor.AplicativoMobile && !funcionario.PermiteMarcacaoApp)
            throw new RegraNegocioException("Marcação pelo aplicativo não habilitada para este funcionário.");

        var existente = await db.RegistrosRep.AsNoTracking()
            .FirstOrDefaultAsync(r => r.TenantId == funcionario.TenantId && r.ClientId == req.ClientId, ct);
        if (existente is not null) return Dto(existente, funcionario, duplicada: true);

        var agora = clock.UtcNow;
        DateTimeOffset horario;
        if (req.Offline)
        {
            if (!funcionario.PermiteMarcacaoOffline)
                throw new ConflitoMarcacaoException("Marcação off-line não habilitada para este funcionário.");
            horario = await ValidarOfflineAsync(funcionario, req, terminalId, agora, ct);
        }
        else
        {
            horario = agora;
        }

        bool? dentroCerca = null;
        foreach (var i in interceptores)
        {
            var aval = await i.AvaliarAsync(new MarcacaoContextoEntrada(
                funcionario.TenantId, funcionario.Id, funcionario.EstabelecimentoId, req.Latitude, req.Longitude, coletor, req.DispositivoId), ct);
            if (aval.Bloquear) throw new RegraNegocioException(aval.Mensagem ?? "Marcação não permitida.");
            dentroCerca ??= aval.DentroCerca;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        RegistroRep reg;
        try
        {
            reg = await registros.RegistrarMarcacaoAsync(funcionario.TenantId, funcionario.EstabelecimentoId, funcionario,
                horario, coletor, req.Offline, req.ClientId, ct);
        }
        catch (DbUpdateException)
        {
            // Duas requisições simultâneas com o mesmo ClientId: a segunda devolve a primeira.
            await tx.RollbackAsync(ct);
            foreach (var e in db.RegistrosRep.Local.Where(r => r.ClientId == req.ClientId).ToList())
                db.RegistrosRep.Entry(e).State = EntityState.Detached;
            var dup = await db.RegistrosRep.AsNoTracking()
                .FirstOrDefaultAsync(r => r.TenantId == funcionario.TenantId && r.ClientId == req.ClientId, ct);
            if (dup is null) throw;
            return Dto(dup, funcionario, duplicada: true);
        }

        db.MarcacoesContexto.Add(new MarcacaoContexto
        {
            TenantId = funcionario.TenantId,
            RegistroRepId = reg.Id,
            Latitude = req.Latitude,
            Longitude = req.Longitude,
            DentroCerca = dentroCerca,
            DispositivoId = req.DispositivoId,
            Ip = ip,
            TerminalId = terminalId,
            HorarioInformadoColetor = req.HorarioOfflineReferenciado
        });
        db.Outbox.Add(new OutboxMessage
        {
            TenantId = funcionario.TenantId,
            Tipo = "MarcacaoRegistrada",
            Payload = JsonSerializer.Serialize(new { RegistroRepId = reg.Id, FuncionarioId = funcionario.Id }),
            CriadoEm = agora
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Dto(reg, funcionario, duplicada: false);
    }

    /// <summary>
    /// Marcação off-line: o horário vem da âncora emitida pelo servidor + relógio monotônico do coletor.
    /// Rejeita horário anterior à âncora, no futuro ou mais antigo que o limite configurado.
    /// </summary>
    private async Task<DateTimeOffset> ValidarOfflineAsync(Funcionario f, RegistrarMarcacaoRequest req, Guid? terminalId, DateTimeOffset agora, CancellationToken ct)
    {
        if (req.AncoraId is null || req.HorarioOfflineReferenciado is null)
            throw new ConflitoMarcacaoException("Marcação off-line sem âncora de hora do servidor.");

        var ancora = await db.AncorasHora.AsNoTracking().FirstOrDefaultAsync(a => a.Id == req.AncoraId, ct)
                     ?? throw new ConflitoMarcacaoException("Âncora de hora desconhecida.");
        if (ancora.TenantId != f.TenantId
            || (ancora.FuncionarioId is not null && ancora.FuncionarioId != f.Id)
            || (ancora.TerminalId is not null && ancora.TerminalId != terminalId))
            throw new ConflitoMarcacaoException("Âncora de hora não pertence a este coletor.");

        var h = req.HorarioOfflineReferenciado.Value;
        if (h < ancora.HoraServidor.AddSeconds(-1))
            throw new ConflitoMarcacaoException("Horário off-line anterior à âncora de hora.");
        if (h > agora.AddSeconds(30))
            throw new ConflitoMarcacaoException("Horário off-line no futuro.");
        if (agora - h > TimeSpan.FromDays(_opt.OfflineMaximoDias))
            throw new ConflitoMarcacaoException("Marcação off-line fora do prazo de sincronização.");
        // Deriva mínima do relógio monotônico do coletor: a marcação nunca pode ser posterior à gravação.
        return h > agora ? agora : h;
    }

    public async Task<IReadOnlyList<UltimaMarcacaoDto>> UltimasAsync(Guid funcionarioId, int quantidade, CancellationToken ct) =>
        await db.RegistrosRep.AsNoTracking()
            .Where(r => r.FuncionarioId == funcionarioId && r.Tipo == TipoRegistroRep.MarcacaoRepP)
            .OrderByDescending(r => r.DataHoraMarcacao)
            .Take(quantidade)
            .Select(r => new UltimaMarcacaoDto(r.Id, r.Nsr, r.DataHoraMarcacao!.Value, r.Offline == true, r.Hash!))
            .ToListAsync(ct);

    private static MarcacaoRegistradaDto Dto(RegistroRep r, Funcionario f, bool duplicada) =>
        new(r.Id, r.ClientId ?? Guid.Empty, r.Nsr, r.DataHoraMarcacao!.Value, r.DataHoraGravacao, r.Offline == true, r.Hash!, f.Nome, duplicada);
}
