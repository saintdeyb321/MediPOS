using System.Data;
using System.Data.Common;
using MediPOS.Application.Tenancy;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MediPOS.Infrastructure.Persistence;

// Reassert even an empty scope on pool checkout. Never depend solely on the driver's reset behavior.
internal sealed class TenantConnectionInterceptor(ITenantDataContext tenantContext) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        TenantSession.Configure(connection, null, tenantContext);

    public override Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default) =>
        TenantSession.ConfigureAsync(connection, null, tenantContext, cancellationToken);
}

// Also covers connections opened for global identity before tenant selection, and transaction rollback.
// Setting commands use ADO directly, so they do not recursively enter EF interception.
internal sealed class TenantCommandInterceptor(ITenantDataContext tenantContext) : DbCommandInterceptor
{
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Configure(command);
        return result;
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await ConfigureAsync(command, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Configure(command);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await ConfigureAsync(command, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Configure(command);
        return result;
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        await ConfigureAsync(command, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private void Configure(DbCommand command) =>
        TenantSession.Configure(command.Connection ?? throw new InvalidOperationException("An open connection is required."),
            command.Transaction, tenantContext);

    private Task ConfigureAsync(DbCommand command, CancellationToken cancellationToken) =>
        TenantSession.ConfigureAsync(command.Connection ?? throw new InvalidOperationException("An open connection is required."),
            command.Transaction, tenantContext, cancellationToken);
}

internal static class TenantSession
{
    internal static void Configure(DbConnection connection, DbTransaction? transaction, ITenantDataContext tenantContext)
    {
        using var command = CreateCommand(connection, transaction, tenantContext);
        command.ExecuteNonQuery();
    }

    internal static async Task ConfigureAsync(
        DbConnection connection, DbTransaction? transaction, ITenantDataContext tenantContext, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, tenantContext);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static DbCommand CreateCommand(DbConnection connection, DbTransaction? transaction, ITenantDataContext tenantContext)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT set_config('medipos.tenant_id', @tenant_id, false)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "tenant_id";
        parameter.DbType = DbType.String;
        parameter.Value = tenantContext.TenantId?.ToString("D") ?? string.Empty;
        command.Parameters.Add(parameter);
        return command;
    }
}
