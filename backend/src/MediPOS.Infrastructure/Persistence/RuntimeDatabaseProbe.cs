using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.Infrastructure.Persistence;

// A narrow diagnostic port allows HTTP composition tests without pretending to validate PostgreSQL.
public interface IRuntimeDatabaseProbe
{
    Task VerifyAsync(CancellationToken token);
}

public sealed class RuntimeDatabaseProbe(IServiceScopeFactory scopes) : IRuntimeDatabaseProbe
{
    public async Task VerifyAsync(CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>();
        if (context.SelectedTenantId is not null) throw new InvalidOperationException("Readiness requires an empty tenant context.");
        // The commercial composition runs RuntimeDatabaseRoleInterceptor on this open, including on pool checkout.
        await context.Database.OpenConnectionAsync(token).ConfigureAwait(false);
        var connection = context.Database.GetDbConnection();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 2;
        command.CommandText = "SET TRANSACTION READ ONLY";
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        command.CommandText = "SELECT 1";
        if (await command.ExecuteScalarAsync(token).ConfigureAwait(false) is not 1) throw new InvalidOperationException("Readiness probe failed.");
        await transaction.RollbackAsync(token).ConfigureAwait(false);
    }
}
