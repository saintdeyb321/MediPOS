using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MediPOS.Infrastructure.Persistence;

// Installed only by the commercial composition root, never by migrations or the restricted catalog host.
// FORCE RLS does not protect a connection that can become a bypass role or disable the policy as owner.
public sealed class RuntimeDatabaseRoleInterceptor : DbConnectionInterceptor
{
    private static readonly ActivitySource Activities = new("MediPOS.Performance.Connections");
    private const string Check = """
        SELECT NOT EXISTS (
            SELECT 1 FROM pg_roles r
            WHERE pg_has_role(session_user, r.oid, 'MEMBER')
              AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole
                   OR r.rolname IN ('medipos_catalog_importer','medipos_catalog_reviewer','medipos_catalog_publisher'))
        ) AND NOT EXISTS (
            SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relkind IN ('r','p')
              AND pg_has_role(session_user, c.relowner, 'MEMBER')
        ) AND NOT has_schema_privilege(session_user, 'public', 'CREATE')
        """;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var measurement = Activities.StartActivity("connection.runtime-role-guard");
        using var command = connection.CreateCommand();
        command.CommandText = Check;
        RequireRestricted(command.ExecuteScalar());
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        using var measurement = Activities.StartActivity("connection.runtime-role-guard");
        await using var command = connection.CreateCommand();
        command.CommandText = Check;
        RequireRestricted(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private static void RequireRestricted(object? result)
    {
        if (result is not true)
            throw new InvalidOperationException("The commercial database runtime must use a restricted role without RLS bypass, schema ownership or platform catalog capabilities.");
    }
}
