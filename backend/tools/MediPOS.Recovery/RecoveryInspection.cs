using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MediPOS.Application.Tenancy;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace MediPOS.Recovery;

public static class RecoveryInspection
{
    public static MediPosDbContext ModelContext(string? connection = null) => new(
        (connection is null ? new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql() :
            new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql(connection)).Options, new TenantDataContext());

    public static void Compare(RecoverySnapshot expected, RecoverySnapshot actual)
    {
        if (!expected.Migrations.SequenceEqual(actual.Migrations) || expected.SchemaSha256 != actual.SchemaSha256 || expected.DatabaseLocale != actual.DatabaseLocale ||
            expected.Tables.Count != actual.Tables.Count || expected.Tables.Any(pair =>
                !actual.Tables.TryGetValue(pair.Key, out var value) || value != pair.Value))
            throw new InvalidOperationException("Restored schema, data, binary evidence or append-only history differs from the backup snapshot.");
    }

    public static void RequireMigrationPrefix(string[] expected, string[] applied, bool current)
    {
        if (applied.Length == 0 || applied.Length > expected.Length || !applied.SequenceEqual(expected.Take(applied.Length)) ||
            (current && applied.Length != expected.Length))
            throw new InvalidOperationException("Schema migration history is unknown, incomplete or not current.");
    }

    public static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    public static async Task<RecoverySnapshot> CaptureAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
    {
        await ExecuteAsync(connection, transaction, "SET LOCAL timezone='UTC'; SET LOCAL datestyle='ISO, YMD'; SET LOCAL extra_float_digits=3; SET LOCAL row_security=off; SET LOCAL search_path=public,pg_catalog", token).ConfigureAwait(false);
        // Explicitly fail when a future storage shape is not covered by this procedure's data fingerprints.
        await RecoveryDatabaseGuard.RequireAsync(connection, """
            SELECT NOT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname NOT IN ('public','pg_catalog','information_schema')
                AND nspname NOT LIKE 'pg_toast%' AND nspname NOT LIKE 'pg_temp%')
              AND NOT EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname='public' AND c.relkind IN ('p','f','S'))
              AND NOT EXISTS (SELECT 1 FROM pg_largeobject_metadata)
            """, token, transaction).ConfigureAwait(false);
        var migrations = await StringsAsync(connection, transaction, "SELECT \"MigrationId\" FROM public.\"__EFMigrationsHistory\" ORDER BY \"MigrationId\"", token).ConfigureAwait(false);
        await using var model = ModelContext();
        RequireMigrationPrefix(model.Database.GetMigrations().ToArray(), migrations, current: false);
        var tables = await StringsAsync(connection, transaction, "SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind='r' ORDER BY c.relname COLLATE \"C\"", token).ConfigureAwait(false);
        var digests = new Dictionary<string, TableDigest>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            await using var command = new NpgsqlCommand($"SELECT row_to_json(t)::text FROM public.{Quote(table)} t ORDER BY row_to_json(t)::text COLLATE \"C\"", connection, transaction);
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, token).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long rows = 0;
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                Append(hash, reader.GetString(0)); rows++;
            }
            digests.Add(table, new(rows, Convert.ToHexString(hash.GetHashAndReset())));
        }
        using var schemaHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var entry in await StringsAsync(connection, transaction, SchemaSql, token).ConfigureAwait(false)) Append(schemaHash, entry);
        return new(migrations, digests, Convert.ToHexString(schemaHash.GetHashAndReset()), await DatabaseLocaleAsync(connection, transaction, token).ConfigureAwait(false));
    }

    public static async Task<string> DatabaseLocaleAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, CancellationToken token) =>
        (await StringsAsync(connection, transaction, "SELECT jsonb_build_object('encoding',pg_encoding_to_char(encoding),'provider',datlocprovider,'collate',datcollate,'ctype',datctype,'locale',datlocale,'rules',daticurules,'version',datcollversion)::text FROM pg_database WHERE datname=current_database()", token).ConfigureAwait(false)).Single();

    private static void Append(IncrementalHash hash, string value)
    {
        // UTF-8 with a byte-length prefix: no concatenation ambiguity, original text is never written to reports.
        var bytes = Encoding.UTF8.GetBytes(value); Span<byte> length = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(length, bytes.Length);
        hash.AppendData(length); hash.AppendData(bytes);
    }

    public static async Task ValidateAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, RecoverySnapshot snapshot, bool current, CancellationToken token)
    {
        await using var model = ModelContext();
        RequireMigrationPrefix(model.Database.GetMigrations().ToArray(), snapshot.Migrations, current);
        await RecoveryDatabaseGuard.RequireAsync(connection, """
            SELECT NOT EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname='public' AND c.relkind='r' AND EXISTS(SELECT 1 FROM pg_attribute a WHERE a.attrelid=c.oid AND a.attname='tenant_id' AND NOT a.attisdropped)
                AND (NOT c.relrowsecurity OR NOT c.relforcerowsecurity OR NOT EXISTS(SELECT 1 FROM pg_policy p WHERE p.polrelid=c.oid)))
              AND NOT EXISTS (SELECT 1 FROM pg_constraint k JOIN pg_namespace n ON n.oid=k.connamespace WHERE n.nspname='public' AND NOT k.convalidated)
              AND NOT EXISTS (SELECT 1 FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname='public' AND t.tgenabled='D')
              AND NOT EXISTS (SELECT 1 FROM pg_index i JOIN pg_class c ON c.oid=i.indrelid JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname='public' AND (NOT i.indisvalid OR NOT i.indisready))
            """, token, transaction).ConfigureAwait(false);
        // Validate every stored CHECK and FK, including historical tables: restored data is not repaired.
        foreach (var query in await StringsAsync(connection, transaction, ConstraintQueries, token).ConfigureAwait(false))
            await RecoveryDatabaseGuard.RequireAsync(connection, query, token, transaction).ConfigureAwait(false);
        foreach (var rule in RecoveryRules.All)
        {
            if (!rule.Tables.All(snapshot.Tables.ContainsKey))
            {
                if (current) throw new InvalidOperationException("A required operational table is absent.");
                continue; // Earlier known migration prefix: explicitly checked after upgrade, never fabricated.
            }
            await RecoveryDatabaseGuard.RequireAsync(connection, rule.Sql, token, transaction).ConfigureAwait(false);
        }
        if (!current) return;
        foreach (var table in model.GetService<IDesignTimeModel>().Model.GetRelationalModel().Tables)
        {
            if (!snapshot.Tables.ContainsKey(table.Name)) throw new InvalidOperationException("Mapped table is absent.");
            var names = table.CheckConstraints.Select(value => value.Name).Concat(table.ForeignKeyConstraints.Select(value => value.Name))
                .Concat(table.UniqueConstraints.Select(value => value.Name)).ToArray();
            await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_constraint WHERE conrelid=to_regclass(@table) AND conname=ANY(@names)", connection, transaction);
            command.Parameters.AddWithValue("table", "public." + Quote(table.Name)); command.Parameters.AddWithValue("names", names);
            if (Convert.ToInt64(await command.ExecuteScalarAsync(token).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) != names.Length)
                throw new InvalidOperationException("An EF constraint or tenant-safe foreign key is absent.");
        }
        // Named SQL objects are obtained offline from the ordered migration script, not from an already damaged backup.
        var script = model.GetService<IMigrator>().GenerateScript();
        var functions = new HashSet<string>(StringComparer.Ordinal); var triggers = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(script, @"\b(CREATE(?:\s+OR\s+REPLACE)?|DROP)\s+(?:CONSTRAINT\s+)?(FUNCTION|TRIGGER)\s+(?:IF\s+EXISTS\s+)?(?:public\.)?""?([a-z_][a-z0-9_]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var set = match.Groups[2].Value.Equals("FUNCTION", StringComparison.OrdinalIgnoreCase) ? functions : triggers;
            if (match.Groups[1].Value.Equals("DROP", StringComparison.OrdinalIgnoreCase)) set.Remove(match.Groups[3].Value);
            else set.Add(match.Groups[3].Value);
        }
        foreach (var (names, sql) in new[] {
            (functions, "SELECT DISTINCT p.proname FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='public'"),
            (triggers, "SELECT DISTINCT t.tgname FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND NOT t.tgisinternal") })
            if (!names.IsSubsetOf(await StringsAsync(connection, transaction, sql, token).ConfigureAwait(false)))
                throw new InvalidOperationException("Required migration function or trigger is absent.");
        await RecoveryDatabaseGuard.RequireAsync(connection, "SELECT count(*)=2 FROM pg_extension WHERE extname IN ('pg_trgm','unaccent')", token, transaction).ConfigureAwait(false);
        await RecoveryDatabaseGuard.RequireAsync(connection, """
            SELECT count(*)=3 AND bool_and(NOT rolcanlogin AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb AND NOT rolcreaterole)
            FROM pg_roles WHERE rolname IN ('medipos_catalog_importer','medipos_catalog_reviewer','medipos_catalog_publisher')
            """, token, transaction).ConfigureAwait(false);
    }

    public static async Task<string[]> StringsAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var values = new List<string>(); while (await reader.ReadAsync(token).ConfigureAwait(false)) values.Add(reader.GetString(0));
        return values.ToArray();
    }

    public static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken token)
    { await using var command = new NpgsqlCommand(sql, connection, transaction); await command.ExecuteNonQueryAsync(token).ConfigureAwait(false); }

    private const string SchemaSql = """
        SELECT entry FROM (
          SELECT jsonb_build_array('extension',extname,extversion)::text entry FROM pg_extension
          UNION ALL SELECT jsonb_build_array('table',c.relname,c.relrowsecurity,c.relforcerowsecurity)::text FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind='r'
          UNION ALL SELECT jsonb_build_array('column',c.relname,row_number() OVER(PARTITION BY c.oid ORDER BY a.attnum),a.attname,format_type(a.atttypid,a.atttypmod),a.attnotnull,a.attidentity,a.attgenerated,pg_get_expr(d.adbin,d.adrelid))::text FROM pg_attribute a JOIN pg_class c ON c.oid=a.attrelid JOIN pg_namespace n ON n.oid=c.relnamespace LEFT JOIN pg_attrdef d ON d.adrelid=c.oid AND d.adnum=a.attnum WHERE n.nspname='public' AND a.attnum>0 AND NOT a.attisdropped
          UNION ALL SELECT jsonb_build_array('constraint',c.relname,k.conname,pg_get_constraintdef(k.oid),k.convalidated)::text FROM pg_constraint k JOIN pg_class c ON c.oid=k.conrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public'
          UNION ALL SELECT jsonb_build_array('index',c.relname,pg_get_indexdef(i.indexrelid),i.indisvalid)::text FROM pg_index i JOIN pg_class c ON c.oid=i.indrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public'
          UNION ALL SELECT jsonb_build_array('function',p.proname,pg_get_function_identity_arguments(p.oid),pg_get_functiondef(p.oid))::text FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='public' AND p.prokind IN ('f','p')
          UNION ALL SELECT jsonb_build_array('policy',c.relname,p.polname,p.polcmd,p.polpermissive,(SELECT array_agg(coalesce(r.rolname,'PUBLIC') ORDER BY coalesce(r.rolname,'PUBLIC')) FROM unnest(p.polroles) x LEFT JOIN pg_roles r ON r.oid=x),pg_get_expr(p.polqual,p.polrelid),pg_get_expr(p.polwithcheck,p.polrelid))::text FROM pg_policy p JOIN pg_class c ON c.oid=p.polrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public'
          UNION ALL SELECT jsonb_build_array('trigger',c.relname,t.tgname,pg_get_triggerdef(t.oid),t.tgenabled)::text FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND NOT t.tgisinternal
          UNION ALL SELECT jsonb_build_array('acl',c.relname,r.rolname,a.privilege_type,a.is_grantable)::text FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace CROSS JOIN LATERAL aclexplode(c.relacl) a LEFT JOIN pg_roles r ON r.oid=a.grantee WHERE n.nspname='public' AND a.grantee<>c.relowner
          UNION ALL SELECT jsonb_build_array('column_acl',c.relname,att.attname,r.rolname,a.privilege_type,a.is_grantable)::text FROM pg_attribute att JOIN pg_class c ON c.oid=att.attrelid JOIN pg_namespace n ON n.oid=c.relnamespace CROSS JOIN LATERAL aclexplode(att.attacl) a LEFT JOIN pg_roles r ON r.oid=a.grantee WHERE n.nspname='public' AND a.grantee<>c.relowner
          UNION ALL SELECT jsonb_build_array('function_acl',p.proname,pg_get_function_identity_arguments(p.oid),r.rolname,a.privilege_type,a.is_grantable)::text FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace CROSS JOIN LATERAL aclexplode(p.proacl) a LEFT JOIN pg_roles r ON r.oid=a.grantee WHERE n.nspname='public' AND a.grantee<>p.proowner
          UNION ALL SELECT jsonb_build_array('schema_acl',n.nspname,r.rolname,a.privilege_type,a.is_grantable)::text FROM pg_namespace n CROSS JOIN LATERAL aclexplode(n.nspacl) a LEFT JOIN pg_roles r ON r.oid=a.grantee WHERE n.nspname='public' AND a.grantee<>n.nspowner
        ) objects ORDER BY entry COLLATE "C"
        """;

    private const string ConstraintQueries = """
        SELECT format('SELECT NOT EXISTS (SELECT 1 FROM %I.%I WHERE NOT (%s))',n.nspname,c.relname,pg_get_expr(k.conbin,k.conrelid))
        FROM pg_constraint k JOIN pg_class c ON c.oid=k.conrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND k.contype='c'
        UNION ALL
        SELECT format('SELECT NOT EXISTS (SELECT 1 FROM %I.%I child WHERE %s AND NOT EXISTS (SELECT 1 FROM %I.%I parent WHERE %s))',n.nspname,c.relname,
          (SELECT string_agg(format('child.%I IS NOT NULL',a.attname),' AND ' ORDER BY x.ordinality) FROM unnest(k.conkey) WITH ORDINALITY x(key,ordinality) JOIN pg_attribute a ON a.attrelid=c.oid AND a.attnum=x.key),pn.nspname,pc.relname,
          (SELECT string_agg(format('child.%I=parent.%I',a.attname,pa.attname),' AND ' ORDER BY x.ordinality) FROM unnest(k.conkey,k.confkey) WITH ORDINALITY x(key,pkey,ordinality) JOIN pg_attribute a ON a.attrelid=c.oid AND a.attnum=x.key JOIN pg_attribute pa ON pa.attrelid=pc.oid AND pa.attnum=x.pkey))
        FROM pg_constraint k JOIN pg_class c ON c.oid=k.conrelid JOIN pg_namespace n ON n.oid=c.relnamespace JOIN pg_class pc ON pc.oid=k.confrelid JOIN pg_namespace pn ON pn.oid=pc.relnamespace WHERE n.nspname='public' AND k.contype='f'
        """;
}
