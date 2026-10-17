using MediPOS.Application.Tenancy;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediPOS.Recovery;

public static class RuntimeRecoveryProbe
{
    public static Task<RuntimeRecoveryEvidence> ValidateAsync(RecoveryConfiguration config, NpgsqlConnection owner, CancellationToken token) =>
        ValidateAsync(config.Runtime!.Connection(config.Passfile, config.RootCertificate), owner, token);

    public static async Task<RuntimeRecoveryEvidence> ValidateAsync(string runtimeConnection, NpgsqlConnection owner, CancellationToken token)
    {
        await using var ownerTransaction = await owner.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, token).ConfigureAwait(false);
        await RecoveryInspection.ExecuteAsync(owner, ownerTransaction, "SET TRANSACTION READ ONLY", token).ConfigureAwait(false);
        await using var count = new NpgsqlCommand("SELECT count(*) FROM tenants", owner, ownerTransaction);
        var available = (long)(await count.ExecuteScalarAsync(token).ConfigureAwait(false))!;
        var tenants = await RecoveryInspection.StringsAsync(owner, ownerTransaction, "SELECT id::text FROM tenants ORDER BY id LIMIT 2", token).ConfigureAwait(false);
        var tables = await RecoveryInspection.StringsAsync(owner, ownerTransaction, """
            SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE n.nspname='public' AND c.relkind='r' AND EXISTS(SELECT 1 FROM pg_attribute a WHERE a.attrelid=c.oid AND a.attname='tenant_id' AND NOT a.attisdropped)
            ORDER BY c.relname
            """, token).ConfigureAwait(false);
        var checks = tables.ToDictionary(table => table, _ => new List<RuntimeVisibility>(), StringComparer.Ordinal);
        // Empty databases still open the real guarded runtime, with an empty tenant scope, never a fabricated identity.
        for (var index = 0; index < Math.Max(1, tenants.Length); index++)
        {
            Guid? tenant = tenants.Length == 0 ? null : Guid.Parse(tenants[index]);
            await using var context = CreateRuntimeContext(runtimeConnection, tenant);
            await context.Database.OpenConnectionAsync(token).ConfigureAwait(false); // The actual commercial guard runs here.
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, token).ConfigureAwait(false);
            await RecoveryInspection.ExecuteAsync(connection, transaction, "SET TRANSACTION READ ONLY", token).ConfigureAwait(false);
            foreach (var table in tables)
            {
                var quoted = "public." + RecoveryInspection.Quote(table);
                long ownRows = 0;
                if (tenant is not null)
                {
                    await using var expected = new NpgsqlCommand($"SELECT count(*) FROM {quoted} WHERE tenant_id=@tenant", owner, ownerTransaction);
                    expected.Parameters.AddWithValue("tenant", tenant.Value);
                    ownRows = (long)(await expected.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                }
                var sql = tenant is null ? $"SELECT count(*),count(*) FROM {quoted}" :
                    $"SELECT count(*),count(*) FILTER(WHERE tenant_id IS DISTINCT FROM @tenant) FROM {quoted}";
                await using var actual = new NpgsqlCommand(sql, connection, transaction);
                if (tenant is not null) actual.Parameters.AddWithValue("tenant", tenant.Value);
                await using var reader = await actual.ExecuteReaderAsync(token).ConfigureAwait(false);
                if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new InvalidOperationException("Runtime visibility result absent.");
                var check = new RuntimeVisibility(tenant is null ? null : index + 1, ExpectedActorlessRows(table, ownRows), reader.GetInt64(0), reader.GetInt64(1));
                RequireVisibility(check);
                checks[table].Add(check);
            }
            await transaction.RollbackAsync(token).ConfigureAwait(false);
        }
        await ownerTransaction.RollbackAsync(token).ConfigureAwait(false);
        return RuntimeRecoveryEvidence.FromChecks(available, tables.Select(table => new RuntimeTableEvidence(table, checks[table].ToArray())).ToArray());
    }

    public static MediPosDbContext CreateRuntimeContext(string connection, Guid? tenant)
    {
        var scope = new TenantDataContext();
        if (tenant is not null) scope.SelectTenant(tenant.Value);
        return new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>()
            .UseNpgsql(connection).AddInterceptors(new RuntimeDatabaseRoleInterceptor()).Options, scope);
    }

    public static void RequireVisibility(RuntimeVisibility check)
    {
        if (check.ExpectedRows < 0 || check.VisibleRows != check.ExpectedRows || check.ForeignRows != 0)
            throw new InvalidOperationException("Restored runtime role cannot read exactly its tenant's private rows.");
    }

    public static long ExpectedActorlessRows(string table, long storedTenantRows)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(storedTenantRows);
        // Snapshot comparison verifies stored rows. This probe never fabricates an Owner or ticket scope.
        return table == "employee_invitations" ? 0 : storedTenantRows;
    }
}

// Ordinals identify only the examined contexts, not tenant identities. Null denotes the empty scope.
public sealed record RuntimeVisibility(int? TenantOrdinal, long ExpectedRows, long VisibleRows, long ForeignRows);
public sealed record RuntimeTableEvidence(string Table, RuntimeVisibility[] Checks)
{
    public string ReadScope => Table == "employee_invitations" ? "ACTORLESS_DENIAL_ONLY" : "TENANT_ROWS";
    public string CrossTenantCoverage => ReadScope == "ACTORLESS_DENIAL_ONLY" ? "ACTOR_CONTEXT_NOT_EXERCISED" : Checks.Length == 2 ?
        (Checks.All(check => check.ExpectedRows > 0) ? "SELECTED_PAIR_ROWS_PRESENT" : "SELECTED_PAIR_ROWS_INCOMPLETE") :
        (Checks[0].TenantOrdinal is null ? "NO_TENANTS" : "SINGLE_TENANT_ONLY");
}

public sealed record RuntimeRecoveryEvidence(long TenantsAvailable, int TenantsExamined, string TenantIsolation,
    string IsolationScope, RuntimeTableEvidence[] Tables, string[] PendingVerifications)
{
    public string RuntimeRole { get; } = "CHECKED";

    public static RuntimeRecoveryEvidence FromChecks(long available, RuntimeTableEvidence[] tables)
    {
        if (available < 0 || tables.Length == 0 || tables.Select(table => table.Table).Distinct(StringComparer.Ordinal).Count() != tables.Length)
            throw new InvalidOperationException("Runtime evidence requires a complete private-table inventory.");
        var examined = (int)Math.Min(2, available);
        foreach (var table in tables)
        {
            if (table.Checks.Length != Math.Max(1, examined)) throw new InvalidOperationException("Runtime evidence has an incorrect examined-context count.");
            for (var index = 0; index < table.Checks.Length; index++)
            {
                var check = table.Checks[index];
                if (check.TenantOrdinal != (examined == 0 ? (int?)null : index + 1) || (examined == 0 && check.ExpectedRows != 0) ||
                    (table.ReadScope == "ACTORLESS_DENIAL_ONLY" && check.ExpectedRows != 0))
                    throw new InvalidOperationException("Runtime evidence has an incorrect tenant context.");
                RuntimeRecoveryProbe.RequireVisibility(check);
            }
        }
        var gaps = tables.Any(table => table.CrossTenantCoverage != "SELECTED_PAIR_ROWS_PRESENT");
        var pending = new List<string> { "EXTERNAL_SECURITY_AND_TRANSACTION_REGRESSIONS" };
        if (tables.Any(table => table.ReadScope == "ACTORLESS_DENIAL_ONLY")) pending.Add("INVITATION_OWNER_AND_TICKET_RLS");
        if (examined < 2) pending.Add("REPRESENTATIVE_TWO_TENANT_REHEARSAL");
        else if (gaps) pending.Add("PRIVATE_TABLE_ROW_COVERAGE");
        if (available > examined) pending.Add("ADDITIONAL_TENANTS");
        var isolation = examined switch
        {
            0 => "NOT_DEMONSTRATED_NO_TENANTS",
            1 => "NOT_DEMONSTRATED_SINGLE_TENANT",
            _ => gaps ? "SELECTED_PAIR_READ_CHECKED_WITH_GAPS" : "SELECTED_PAIR_READ_CHECKED"
        };
        var scope = examined switch { 0 => "EMPTY_TENANT_CONTEXT_READ", 1 => "SINGLE_TENANT_READ", _ => "SELECTED_PAIR_READ" };
        return new(available, examined, isolation, scope, tables, pending.ToArray());
    }
}
