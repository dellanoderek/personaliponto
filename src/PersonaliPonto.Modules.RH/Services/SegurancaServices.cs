using Microsoft.EntityFrameworkCore;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Modules.RH.Domain;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Modules.RH.Services;

public sealed class ConfiguracaoSegurancaService(IRhDbContext db, AuditService audit, ICurrentUser user, IClock clock, ITenantContext tenant)
{
    public async Task<ConfiguracaoSeguranca> ObterAsync(CancellationToken ct) =>
        await db.ConfiguracoesSeguranca.AsNoTracking().FirstOrDefaultAsync(ct) ?? new ConfiguracaoSeguranca { RetencaoFotoDias = 90 };

    public async Task SalvarAsync(bool exigirDispositivo, bool fotoNaMarcacao, int retencaoDias, bool avaliacaoLgpdConfirmada, CancellationToken ct)
    {
        if (retencaoDias is < 1 or > 1825) throw new RegraNegocioException("Retenção entre 1 e 1825 dias.");
        var c = await db.ConfiguracoesSeguranca.FirstOrDefaultAsync(ct);
        var ativandoFoto = fotoNaMarcacao && c?.FotoNaMarcacao != true;
        if (ativandoFoto && !avaliacaoLgpdConfirmada)
            throw new RegraNegocioException("Para ativar a foto na marcação, confirme que a avaliação de LGPD foi realizada e os funcionários foram informados.");
        if (c is null)
        {
            c = new ConfiguracaoSeguranca { TenantId = tenant.TenantId ?? throw new RegraNegocioException("Operação exige um tenant.") };
            db.ConfiguracoesSeguranca.Add(c);
        }
        if (ativandoFoto)
        {
            c.AvaliacaoLgpdResponsavel = user.Nome;
            c.AvaliacaoLgpdEm = clock.UtcNow;
        }
        c.ExigirDispositivoAutorizado = exigirDispositivo;
        c.FotoNaMarcacao = fotoNaMarcacao;
        c.RetencaoFotoDias = retencaoDias;
        audit.Registrar("seguranca.configuracao", nameof(ConfiguracaoSeguranca), c.Id, new { exigirDispositivo, fotoNaMarcacao, retencaoDias });
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Device binding: registra aparelhos novos e, se exigido, bloqueia aparelhos não autorizados.</summary>
public sealed class DispositivoInterceptor(IRhDbContext db, IClock clock) : IMarcacaoInterceptor
{
    public async Task<MarcacaoAvaliacao> AvaliarAsync(MarcacaoContextoEntrada e, CancellationToken ct)
    {
        if (e.Coletor != Coletor.AplicativoMobile) return MarcacaoAvaliacao.Livre;
        var (ok, msg) = await VerificarAsync(e.TenantId, e.FuncionarioId, e.DispositivoId, ct);
        return ok ? MarcacaoAvaliacao.Livre : new MarcacaoAvaliacao(true, null, msg);
    }

    /// <summary>Registra o aparelho (se novo) e informa se ele pode marcar.</summary>
    public async Task<(bool Permitido, string? Mensagem)> VerificarAsync(Guid tenantId, Guid funcionarioId, string? dispositivoId, CancellationToken ct)
    {
        var cfg = await db.ConfiguracoesSeguranca.AsNoTracking().FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(dispositivoId))
            return cfg?.ExigirDispositivoAutorizado == true ? (false, "Aparelho não identificado.") : (true, null);

        var id = dispositivoId.Trim();
        if (id.Length > 120) id = id[..120];
        var d = await db.Dispositivos.FirstOrDefaultAsync(x => x.FuncionarioId == funcionarioId && x.DispositivoId == id, ct);
        var agora = clock.UtcNow;
        if (d is null)
        {
            d = new DispositivoFuncionario
            {
                TenantId = tenantId, FuncionarioId = funcionarioId, DispositivoId = id, PrimeiroUso = agora, UltimoUso = agora,
                // Sem exigência, o aparelho é registrado já autorizado; com exigência, o primeiro aparelho ainda precisa do RH.
                Status = cfg?.ExigirDispositivoAutorizado == true ? StatusDispositivo.Pendente : StatusDispositivo.Autorizado
            };
            db.Dispositivos.Add(d);
        }
        else d.UltimoUso = agora;
        await db.SaveChangesAsync(ct);

        if (cfg?.ExigirDispositivoAutorizado != true) return (true, null);
        return d.Status switch
        {
            StatusDispositivo.Autorizado => (true, null),
            StatusDispositivo.Revogado => (false, "Este aparelho foi bloqueado pelo RH para marcação de ponto."),
            _ => (false, "Aparelho aguardando autorização do RH. Solicite a liberação.")
        };
    }
}

public sealed class DispositivoService(IRhDbContext db, AuditService audit, ICurrentUser user)
{
    public async Task AlterarAsync(Guid dispositivoId, StatusDispositivo status, CancellationToken ct)
    {
        var d = await db.Dispositivos.FirstOrDefaultAsync(x => x.Id == dispositivoId, ct) ?? throw new NaoEncontradoException("Aparelho não encontrado.");
        d.Status = status;
        d.AlteradoPor = user.UserId;
        audit.Registrar("dispositivo.status", nameof(DispositivoFuncionario), d.Id, new { d.FuncionarioId, status });
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Fotos de marcação com retenção limitada (LGPD: minimização e eliminação após a finalidade).</summary>
public sealed class FotoMarcacaoService(IRhDbContext db, ArquivoService arquivos, IFileStorage storage, IClock clock)
{
    public const string Bucket = "fotos-marcacao";

    public async Task AnexarAsync(Guid registroRepId, Guid funcionarioId, byte[] foto, CancellationToken ct)
    {
        var cfg = await db.ConfiguracoesSeguranca.AsNoTracking().FirstOrDefaultAsync(ct);
        if (cfg?.FotoNaMarcacao != true) throw new RegraNegocioException("Foto na marcação não está habilitada para esta empresa.");
        var r = await db.RegistrosRep.AsNoTracking().FirstOrDefaultAsync(x => x.Id == registroRepId && x.FuncionarioId == funcionarioId, ct)
                ?? throw new NaoEncontradoException("Marcação não encontrada.");
        if (await db.FotosMarcacao.AnyAsync(f => f.RegistroRepId == r.Id, ct)) throw new RegraNegocioException("Marcação já possui foto.");
        var arq = await arquivos.EnviarAsync(Bucket, "marcacao.jpg", foto, ct);
        if (arq.ContentType != "image/webp") throw new RegraNegocioException("Envie uma imagem.");
        db.FotosMarcacao.Add(new FotoMarcacao
        {
            TenantId = r.TenantId, RegistroRepId = r.Id, ArquivoId = arq.Id, CriadaEm = clock.UtcNow, ExpiraEm = clock.UtcNow.AddDays(cfg.RetencaoFotoDias)
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Exclui do storage as fotos vencidas (rotina diária, executada em contexto de sistema).</summary>
    public async Task<int> ExpurgarAsync(CancellationToken ct)
    {
        var agora = clock.UtcNow;
        var vencidas = await db.FotosMarcacao.Where(f => f.ExcluidaEm == null && f.ExpiraEm <= agora).Take(500).ToListAsync(ct);
        foreach (var f in vencidas)
        {
            if (f.ArquivoId is { } id && await db.Arquivos.FirstOrDefaultAsync(a => a.Id == id, ct) is { } arq)
            {
                await storage.ExcluirAsync(arq.Bucket, arq.Caminho, ct);
                db.Arquivos.Remove(arq);
            }
            f.ArquivoId = null;
            f.ExcluidaEm = agora;
        }
        await db.SaveChangesAsync(ct);
        return vencidas.Count;
    }
}

public enum TipoAlerta
{
    ForaDaCerca,
    GpsImpreciso,
    DeslocamentoImpossivel,
    AparelhoNaoHabitual,
    OfflineRecorrente,
    AparelhoCompartilhado
}

public sealed record AlertaSeguranca(TipoAlerta Tipo, Guid FuncionarioId, string Funcionario, DateTimeOffset Quando, string Detalhe, int Gravidade);

/// <summary>Detecção de padrões anômalos (Fase 5) a partir do contexto das marcações. Não bloqueia: sinaliza para o RH.</summary>
public sealed class AnaliseFraudeService(IRhDbContext db)
{
    public async Task<IReadOnlyList<AlertaSeguranca>> AnalisarAsync(DateTimeOffset de, DateTimeOffset ate, CancellationToken ct)
    {
        var dados = await (from r in db.RegistrosRep.AsNoTracking()
                           join c in db.MarcacoesContexto.AsNoTracking() on r.Id equals c.RegistroRepId
                           join f in db.Funcionarios.AsNoTracking() on r.FuncionarioId equals f.Id
                           where r.Tipo == TipoRegistroRep.MarcacaoRepP && r.DataHoraMarcacao >= de && r.DataHoraMarcacao < ate
                           orderby r.DataHoraMarcacao
                           select new { r.FuncionarioId, f.Nome, r.DataHoraMarcacao, r.Offline, r.Coletor, c.Latitude, c.Longitude, c.DentroCerca, c.DispositivoId })
            .ToListAsync(ct);

        var alertas = new List<AlertaSeguranca>();
        foreach (var g in dados.GroupBy(x => x.FuncionarioId))
        {
            var lista = g.ToList();
            var nome = lista[0].Nome;
            var fid = g.Key!.Value;

            foreach (var m in lista.Where(x => x.DentroCerca == false))
                alertas.Add(new(TipoAlerta.ForaDaCerca, fid, nome, m.DataHoraMarcacao!.Value, "Marcação fora da cerca virtual do local.", 2));

            // Deslocamento impossível: velocidade acima de 150 km/h entre marcações consecutivas com GPS.
            var comGps = lista.Where(x => x.Latitude is not null && x.Longitude is not null).ToList();
            for (var i = 1; i < comGps.Count; i++)
            {
                var a = comGps[i - 1];
                var b = comGps[i];
                var horas = (b.DataHoraMarcacao!.Value - a.DataHoraMarcacao!.Value).TotalHours;
                if (horas <= 0) continue;
                var km = GeofenceInterceptor.DistanciaMetros(a.Latitude!.Value, a.Longitude!.Value, b.Latitude!.Value, b.Longitude!.Value) / 1000;
                if (km > 5 && km / horas > 150)
                    alertas.Add(new(TipoAlerta.DeslocamentoImpossivel, fid, nome, b.DataHoraMarcacao.Value,
                        $"{km:0} km em {horas * 60:0} min entre marcações ({km / horas:0} km/h).", 3));
            }

            // Aparelho não habitual: aparelho usado em menos de 10% das marcações do app, com histórico suficiente.
            var app = lista.Where(x => x.Coletor == Coletor.AplicativoMobile && !string.IsNullOrEmpty(x.DispositivoId)).ToList();
            if (app.Count >= 10)
            {
                foreach (var d in app.GroupBy(x => x.DispositivoId).Where(d => d.Count() < app.Count * 0.1))
                    alertas.Add(new(TipoAlerta.AparelhoNaoHabitual, fid, nome, d.Max(x => x.DataHoraMarcacao!.Value),
                        $"{d.Count()} marcação(ões) em aparelho diferente do habitual.", 2));
            }

            var offline = lista.Count(x => x.Offline == true);
            if (lista.Count >= 8 && offline > lista.Count * 0.5)
                alertas.Add(new(TipoAlerta.OfflineRecorrente, fid, nome, lista[^1].DataHoraMarcacao!.Value,
                    $"{offline} de {lista.Count} marcações feitas sem conexão.", 1));
        }

        // Mesmo aparelho usado por funcionários diferentes (possível marcação por terceiros).
        foreach (var d in dados.Where(x => x.Coletor == Coletor.AplicativoMobile && !string.IsNullOrEmpty(x.DispositivoId))
                     .GroupBy(x => x.DispositivoId).Where(d => d.Select(x => x.FuncionarioId).Distinct().Count() > 1))
        {
            var nomes = string.Join(", ", d.Select(x => x.Nome).Distinct());
            foreach (var f in d.GroupBy(x => x.FuncionarioId))
                alertas.Add(new(TipoAlerta.AparelhoCompartilhado, f.Key!.Value, f.First().Nome, f.Max(x => x.DataHoraMarcacao!.Value),
                    $"Aparelho usado também por: {nomes}.", 3));
        }

        return alertas.OrderByDescending(a => a.Gravidade).ThenByDescending(a => a.Quando).ToList();
    }
}
