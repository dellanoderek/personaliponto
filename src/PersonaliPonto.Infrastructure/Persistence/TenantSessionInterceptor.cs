using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using PersonaliPonto.Core.RepP.Abstractions;

namespace PersonaliPonto.Infrastructure.Persistence;

/// <summary>
/// Aplica, na sessão do PostgreSQL, o papel sem BYPASSRLS da aplicação e as variáveis usadas pelas
/// políticas de Row-Level Security (app.tenant_id / app.bypass_rls). Reaplica antes de qualquer comando
/// sempre que o contexto de tenant muda — o isolamento não depende do desenvolvedor lembrar de filtrar.
/// </summary>
public sealed class TenantSessionInterceptor(string? papelAplicacao) : DbCommandInterceptor, IDbConnectionInterceptor, IDbTransactionInterceptor
{
    public const string PapelPadrao = "personaliponto_app";

    private sealed class Estado
    {
        public string? Chave;
    }

    private static readonly ConditionalWeakTable<DbConnection, Estado> Estados = new();

    private static (string TenantId, string Bypass, string Canal, string Canais, string Tenants) Valores(DbContext? ctx)
    {
        var t = (ctx as PersonaliPontoDbContext)?.TenantContext;
        if (t is null) return ("", "off", "", "", "");
        return (t.TenantId?.ToString() ?? "", t.IsSystem ? "on" : "off", t.CanalId?.ToString() ?? "",
            ArrayPg(t.CanaisVisiveis), ArrayPg(t.TenantsCanal));
    }

    /// <summary>Literal de array uuid do PostgreSQL ("{a,b}"); vazio quando não há itens.</summary>
    private static string ArrayPg(IReadOnlyList<Guid> ids) => ids.Count == 0 ? "" : "{" + string.Join(',', ids) + "}";

    private async Task AplicarAsync(DbConnection conn, DbContext? ctx, DbTransaction? tx, CancellationToken ct)
    {
        var (tenant, bypass, canal, canais, tenants) = Valores(ctx);
        var migracao = (ctx as PersonaliPontoDbContext)?.TenantContext is Tenancy.RequestContext { Migracao: true };
        var chave = string.Join('|', tenant, bypass, canal, canais, tenants, migracao);
        var estado = Estados.GetOrCreateValue(conn);
        if (estado.Chave == chave) return;

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        var role = migracao ? "RESET ROLE; " : string.IsNullOrWhiteSpace(papelAplicacao) ? "" : $"SET ROLE {papelAplicacao}; ";
        cmd.CommandText = role + "SELECT set_config('app.tenant_id', @t, false), set_config('app.bypass_rls', @b, false), "
            + "set_config('app.canal_id', @c, false), set_config('app.canais_canal', @cs, false), set_config('app.tenants_canal', @ts, false);";
        cmd.Parameters.Add(new NpgsqlParameter("t", tenant));
        cmd.Parameters.Add(new NpgsqlParameter("b", bypass));
        cmd.Parameters.Add(new NpgsqlParameter("c", canal));
        cmd.Parameters.Add(new NpgsqlParameter("cs", canais));
        cmd.Parameters.Add(new NpgsqlParameter("ts", tenants));
        await cmd.ExecuteNonQueryAsync(ct);
        estado.Chave = chave;
    }

    private void Aplicar(DbConnection conn, DbContext? ctx, DbTransaction? tx) =>
        AplicarAsync(conn, ctx, tx, CancellationToken.None).GetAwaiter().GetResult();

    // Conexão aberta: estado desconhecido (pool reseta a sessão) — força reaplicação.
    public void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        Estados.AddOrUpdate(connection, new Estado());
        Aplicar(connection, eventData.Context, null);
    }

    public async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Estados.AddOrUpdate(connection, new Estado());
        await AplicarAsync(connection, eventData.Context, null, cancellationToken);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Aplicar(command.Connection!, eventData.Context, command.Transaction);
        return result;
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        await AplicarAsync(command.Connection!, eventData.Context, command.Transaction, cancellationToken);
        return result;
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Aplicar(command.Connection!, eventData.Context, command.Transaction);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        await AplicarAsync(command.Connection!, eventData.Context, command.Transaction, cancellationToken);
        return result;
    }

    // Comandos que mexem na sessão (set_config, SET/RESET ROLE, DISCARD) invalidam o estado conhecido:
    // o próximo comando reaplica papel e tenant.
    private static void Invalidar(DbCommand command)
    {
        var t = command.CommandText;
        if (t.Contains("set_config", StringComparison.OrdinalIgnoreCase) || t.Contains("role", StringComparison.OrdinalIgnoreCase)
            || t.Contains("app.", StringComparison.OrdinalIgnoreCase) || t.Contains("discard", StringComparison.OrdinalIgnoreCase)
            || t.Contains("session", StringComparison.OrdinalIgnoreCase))
        {
            if (command.Connection is { } c && Estados.TryGetValue(c, out var e)) e.Chave = null;
        }
    }

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Invalidar(command);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        Invalidar(command);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Invalidar(command);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Invalidar(command);
        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        Invalidar(command);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
    {
        Invalidar(command);
        return ValueTask.FromResult(result);
    }

    // ROLLBACK desfaz set_config feito dentro da transação: o estado conhecido deixa de valer.
    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => InvalidarContexto(eventData.Context);

    private static void InvalidarContexto(DbContext? ctx)
    {
        if (ctx is null) return;
        var c = ctx.Database.GetDbConnection();
        if (Estados.TryGetValue(c, out var e)) e.Chave = null;
    }

    public Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        TransactionRolledBack(transaction, eventData);
        return Task.CompletedTask;
    }

    public void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData) => InvalidarContexto(eventData.Context);

    public Task TransactionFailedAsync(DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        TransactionFailed(transaction, eventData);
        return Task.CompletedTask;
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        if (command.Connection is { } c && Estados.TryGetValue(c, out var e)) e.Chave = null;
    }

    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        CommandFailed(command, eventData);
        return Task.CompletedTask;
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Aplicar(command.Connection!, eventData.Context, command.Transaction);
        return result;
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        await AplicarAsync(command.Connection!, eventData.Context, command.Transaction, cancellationToken);
        return result;
    }
}
