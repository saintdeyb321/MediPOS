using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace MediPOS.Recovery;

public static class RecoveryDatabaseGuard
{
    public static string ClusterHash(string identifier) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identifier)));

    public static void RequireCluster(string actual, string expected, string source)
    {
        if (actual != expected || actual == source)
            throw new InvalidOperationException("Recovery target is not the attested separate PostgreSQL cluster.");
    }

    public static async Task<(NpgsqlConnection Connection, string Cluster, int Version)> OpenAsync(RecoveryConfiguration config,
        RecoveryEndpoint endpoint, string? sourceCluster, bool emptyTarget, CancellationToken token)
    {
        var connection = new NpgsqlConnection(endpoint.Connection(config.Passfile, config.RootCertificate));
        try
        {
            await connection.OpenAsync(token).ConfigureAwait(false);
            await using (var command = new NpgsqlCommand("SELECT current_database(), session_user, current_setting('server_version_num')::int, system_identifier::text FROM pg_control_system()", connection))
            await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(token).ConfigureAwait(false) || reader.GetString(0) != endpoint.Database ||
                    reader.GetString(1) != endpoint.Username || reader.GetInt32(2) / 10000 != 18)
                    throw new InvalidOperationException("Unexpected PostgreSQL endpoint, identity or major version.");
                var cluster = ClusterHash(reader.GetString(3));
                var version = reader.GetInt32(2);
                await reader.DisposeAsync().ConfigureAwait(false);
                if (sourceCluster is not null)
                {
                    RequireCluster(cluster, config.IsolatedClusterSha256!, sourceCluster);
                    await CheckTargetAsync(connection, config, emptyTarget, token).ConfigureAwait(false);
                }
                else
                {
                    if (cluster != config.ExpectedSourceClusterSha256)
                        throw new InvalidOperationException("Backup source is not the separately approved cluster.");
                    await using var environment = new NpgsqlCommand("SELECT current_setting('medipos.backup_environment',true)=@environment", connection);
                    environment.Parameters.AddWithValue("environment", config.Environment);
                    if (await environment.ExecuteScalarAsync(token).ConfigureAwait(false) is not true)
                        throw new InvalidOperationException("Backup source server environment disagrees with operator configuration.");
                    await RequireAsync(connection, """
                        SELECT r.rolbypassrls AND NOT r.rolsuper AND NOT r.rolcreatedb AND NOT r.rolcreaterole
                          AND NOT EXISTS (SELECT 1 FROM pg_roles elevated WHERE pg_has_role(session_user,elevated.oid,'MEMBER')
                            AND (elevated.rolsuper OR elevated.rolcreatedb OR elevated.rolcreaterole))
                          AND NOT has_schema_privilege(session_user,'public','CREATE')
                          AND NOT EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                            WHERE n.nspname='public' AND c.relkind IN ('r','p') AND pg_has_role(session_user,c.relowner,'MEMBER'))
                          AND NOT EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                            WHERE n.nspname='public' AND c.relkind IN ('r','p') AND
                              has_table_privilege(session_user,c.oid,'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER'))
                        FROM pg_roles r WHERE r.rolname=session_user
                        """, token).ConfigureAwait(false);
                }
                return (connection, cluster, version);
            }
        }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private static async Task CheckTargetAsync(NpgsqlConnection connection, RecoveryConfiguration config, bool empty, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("""
            SELECT current_setting('medipos.recovery_environment',true)='isolated-v1'
              AND current_setting('medipos.external_effects',true)='disabled'
              AND shobj_description(d.oid,'pg_database')=@marker
              AND d.datdba=r.oid AND r.rolbypassrls AND NOT r.rolsuper AND NOT r.rolcreatedb AND NOT r.rolcreaterole
              AND NOT EXISTS (SELECT 1 FROM pg_roles elevated WHERE pg_has_role(session_user,elevated.oid,'MEMBER')
                AND (elevated.rolsuper OR elevated.rolcreatedb OR elevated.rolcreaterole))
              AND NOT EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid())
              AND NOT EXISTS (SELECT 1 FROM aclexplode(coalesce(d.datacl,acldefault('d',d.datdba))) a
                LEFT JOIN pg_roles gr ON gr.oid=a.grantee WHERE a.privilege_type='CONNECT'
                  AND (a.grantee=0 OR gr.rolname NOT IN (session_user,@runtime)))
            FROM pg_database d JOIN pg_roles r ON r.rolname=session_user WHERE d.datname=current_database()
            """, connection);
        command.Parameters.AddWithValue("marker", "MediPOS isolated recovery v1:" + config.RunId);
        command.Parameters.AddWithValue("runtime", config.Runtime!.Username);
        if (await command.ExecuteScalarAsync(token).ConfigureAwait(false) is not true)
            throw new InvalidOperationException("Recovery isolation, exclusive owner, disabled effects or connection ACL guard failed.");
        if (empty)
            await RequireAsync(connection, """
                SELECT NOT EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                    WHERE n.nspname NOT IN ('pg_catalog','information_schema') AND n.nspname NOT LIKE 'pg_toast%' AND c.relkind IN ('r','p','v','m','S','f'))
                  AND NOT EXISTS (SELECT 1 FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='public')
                  AND NOT EXISTS (SELECT 1 FROM pg_extension WHERE extname<>'plpgsql')
                """, token).ConfigureAwait(false);
    }

    public static async Task RequireAsync(NpgsqlConnection connection, string sql, CancellationToken token, NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        if (await command.ExecuteScalarAsync(token).ConfigureAwait(false) is not true)
            throw new InvalidOperationException("Recovery database safety or integrity check failed.");
    }
}
