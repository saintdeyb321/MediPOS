using MediPOS.Application.Tenancy;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediPOS.Recovery;

public static class RuntimeRecoveryProbe
{
    public static async Task ValidateAsync(RecoveryConfiguration config, NpgsqlConnection owner, CancellationToken token)
    {
        var tenants = await RecoveryInspection.StringsAsync(owner, null, "SELECT id::text FROM tenants ORDER BY id LIMIT 2", token).ConfigureAwait(false);
        if (tenants.Length < 2) throw new InvalidOperationException("Two preserved tenants are required to demonstrate runtime RLS isolation; use a representative synthetic rehearsal.");
        var tables = await RecoveryInspection.StringsAsync(owner, null, """
            SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE n.nspname='public' AND c.relkind='r' AND EXISTS(SELECT 1 FROM pg_attribute a WHERE a.attrelid=c.oid AND a.attname='tenant_id' AND NOT a.attisdropped)
            ORDER BY c.relname
            """, token).ConfigureAwait(false);
        foreach (var tenant in tenants)
        {
            var scope = new TenantDataContext(); scope.SelectTenant(Guid.Parse(tenant));
            await using var context = new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>()
                .UseNpgsql(config.Runtime!.Connection(config.Passfile, config.RootCertificate))
                .AddInterceptors(new RuntimeDatabaseRoleInterceptor()).Options, scope);
            await context.Database.OpenConnectionAsync(token).ConfigureAwait(false); // The actual commercial guard runs here.
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, token).ConfigureAwait(false);
            await RecoveryInspection.ExecuteAsync(connection, transaction, "SET TRANSACTION READ ONLY", token).ConfigureAwait(false);
            foreach (var table in tables)
            {
                var quoted = "public." + RecoveryInspection.Quote(table);
                await using var expected = new NpgsqlCommand($"SELECT count(*) FROM {quoted} WHERE tenant_id=@tenant", owner);
                expected.Parameters.AddWithValue("tenant", Guid.Parse(tenant));
                var ownRows = (long)(await expected.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                await using var actual = new NpgsqlCommand($"SELECT count(*),count(*) FILTER(WHERE tenant_id<>@tenant) FROM {quoted}", connection, transaction);
                actual.Parameters.AddWithValue("tenant", Guid.Parse(tenant));
                await using var reader = await actual.ExecuteReaderAsync(token).ConfigureAwait(false);
                if (!await reader.ReadAsync(token).ConfigureAwait(false) || reader.GetInt64(0) != ownRows || reader.GetInt64(1) != 0)
                    throw new InvalidOperationException("Restored runtime role cannot read exactly its tenant's private rows.");
            }
            await transaction.RollbackAsync(token).ConfigureAwait(false);
        }
    }
}
