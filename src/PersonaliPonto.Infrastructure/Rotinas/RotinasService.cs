using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Infrastructure.Persistence;
using PersonaliPonto.Infrastructure.Tempo;
using PersonaliPonto.Infrastructure.Tenancy;
using PersonaliPonto.Modules.SaaS.Services;

namespace PersonaliPonto.Infrastructure.Rotinas;

/// <summary>Handler de mensagens do outbox (decisão 8: fila simples via banco).</summary>
public interface IOutboxHandler
{
    string Tipo { get; }
    Task ProcessarAsync(OutboxMessage mensagem, CancellationToken ct);
}

/// <summary>Processa o outbox com retentativa exponencial. Mensagens sem handler são concluídas.</summary>
public sealed class OutboxProcessor(IServiceScopeFactory scopes, ILogger<OutboxProcessor> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processadas = await ProcessarLoteAsync(stoppingToken);
                if (processadas == 0) await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                log.LogError(ex, "Falha no processamento do outbox.");
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }
    }

    public async Task<int> ProcessarLoteAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>().DefinirSistema();
        var db = scope.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
        var handlers = scope.ServiceProvider.GetServices<IOutboxHandler>().ToDictionary(h => h.Tipo);
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var agora = clock.UtcNow;

        var lote = await db.Outbox
            .Where(m => m.ProcessadoEm == null && (m.ProximaTentativa == null || m.ProximaTentativa <= agora) && m.Tentativas < 10)
            .OrderBy(m => m.CriadoEm).Take(50).ToListAsync(ct);
        foreach (var m in lote)
        {
            try
            {
                if (handlers.TryGetValue(m.Tipo, out var h)) await h.ProcessarAsync(m, ct);
                m.ProcessadoEm = clock.UtcNow;
                m.Erro = null;
            }
            catch (Exception ex)
            {
                m.Tentativas++;
                m.Erro = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                m.ProximaTentativa = clock.UtcNow.AddSeconds(Math.Pow(2, m.Tentativas) * 10);
                log.LogWarning("Outbox {Tipo} {Id} falhou (tentativa {N}): {Erro}", m.Tipo, m.Id, m.Tentativas, ex.Message);
            }
        }
        await db.SaveChangesAsync(ct);
        return lote.Count;
    }
}

/// <summary>
/// Rotinas periódicas de plataforma: geração de faturas da competência, régua de inadimplência,
/// expiração de testes, limpeza de âncoras antigas e eventos de disponibilidade do REP-P (tipo 6: 07/08).
/// </summary>
public sealed class RotinasPlataforma(IServiceScopeFactory scopes, NtpClock ntp, ILogger<RotinasPlataforma> log) : BackgroundService
{
    private bool? _disponivelAnterior;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ExecutarAsync(stoppingToken); }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested) { log.LogError(ex, "Falha nas rotinas de plataforma."); }
            await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
        }
    }

    public async Task ExecutarAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RequestContext>();
        ctx.DefinirSistema();
        var fat = scope.ServiceProvider.GetRequiredService<FaturamentoService>();
        var hoje = fat.Hoje;
        var novas = await fat.GerarCompetenciaAsync(new DateOnly(hoje.Year, hoje.Month, 1), ct);
        var alterados = await fat.ProcessarInadimplenciaAsync(ct);
        if (novas + alterados > 0) log.LogInformation("Faturamento: {Novas} faturas geradas, {Alterados} clientes com status alterado.", novas, alterados);

        // Canais (E2): apuração da competência anterior (snapshot idempotente), faturas de canal e régua de canais.
        var (apuracoes, faturasCanal, canais) = await scope.ServiceProvider.GetRequiredService<ApuracaoCanalService>().ProcessarAsync(ct);
        if (apuracoes + faturasCanal + canais > 0)
            log.LogInformation("Canais: {A} apurações, {F} faturas de canal, {C} canais com situação alterada.", apuracoes, faturasCanal, canais);

        // Pagamentos (E3/E9): cobranças pendentes de envio ao Asaas sem mensagem viva no outbox voltam para a fila.
        var reenviadas = await scope.ServiceProvider.GetRequiredService<Pagamentos.VarreduraCobrancasGateway>().EnfileirarPendentesAsync(ct);
        if (reenviadas > 0) log.LogInformation("Pagamentos: {N} cobranças reenfileiradas para o gateway.", reenviadas);

        var db = scope.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
        var fotos = await scope.ServiceProvider.GetRequiredService<Modules.RH.Services.FotoMarcacaoService>().ExpurgarAsync(ct);
        if (fotos > 0) log.LogInformation("{N} fotos de marcação excluídas por fim do prazo de retenção.", fotos);
        var limite = DateTimeOffset.UtcNow.AddDays(-30);
        await db.AncorasHora.Where(a => a.HoraServidor < limite).ExecuteDeleteAsync(ct);

        // Disponibilidade do serviço de hora: registra 08 ao perder a sincronização e 07 ao recuperar.
        var disponivel = ntp.Sincronizado;
        if (_disponivelAnterior is not null && _disponivelAnterior != disponivel)
        {
            var registros = scope.ServiceProvider.GetRequiredService<RegistroRepService>();
            var ests = await db.Estabelecimentos.AsNoTracking().Where(e => e.Ativo).Select(e => new { e.Id, e.TenantId }).ToListAsync(ct);
            foreach (var e in ests) await registros.RegistrarEventoAsync(e.TenantId, e.Id, disponivel ? 7 : 8, ct);
            log.LogWarning("Evento de {Tipo} de serviço registrado para {N} estabelecimentos.", disponivel ? "disponibilidade" : "indisponibilidade", ests.Count);
        }
        _disponivelAnterior = disponivel;
    }
}
