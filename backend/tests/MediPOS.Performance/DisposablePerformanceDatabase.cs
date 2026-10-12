using MediPOS.Application.Tenancy;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediPOS.Performance;

// External execution only. This project never starts containers and never accepts an existing database for seeding.
public sealed class DisposablePerformanceDatabase : IAsyncDisposable
{
    private readonly PerformanceOptions _options;
    private readonly NpgsqlConnection _control;
    private readonly string _migrationConnection;
    private bool _created;
    private int _generationClaimed;
    public string RuntimeConnection { get; }
    public string RunId => _options.RunId;
    public string PostgreSqlVersion { get; private set; } = "";
    public IReadOnlyDictionary<string, string> Settings { get; private set; } = new Dictionary<string, string>();

    private DisposablePerformanceDatabase(PerformanceOptions options, string control, string runtime)
    {
        ValidateBoundary(options, Environment.GetEnvironmentVariable("MEDIPOS_PERF_OPT_IN"),
            Environment.GetEnvironmentVariable("MEDIPOS_ENVIRONMENT"), control, runtime);
        _options = options;
        _control = new NpgsqlConnection(control);
        RuntimeConnection = new NpgsqlConnectionStringBuilder(runtime) { ApplicationName = "MediPOS.Performance", IncludeErrorDetail = false }.ConnectionString;
        _migrationConnection = new NpgsqlConnectionStringBuilder(control) { Database = options.DatabaseName, IncludeErrorDetail = false }.ConnectionString;
    }

    public static void ValidateBoundary(PerformanceOptions options, string? optIn, string? environment, string control, string runtime)
    {
        options.Validate();
        var administrator = new NpgsqlConnectionStringBuilder(control);
        var commercial = new NpgsqlConnectionStringBuilder(runtime);
        if (optIn != "CREATE_DISPOSABLE_DATABASE" || environment != "Performance" || administrator.Database != "postgres" ||
            commercial.Database != options.DatabaseName || administrator.Host != commercial.Host || administrator.Port != commercial.Port ||
            string.IsNullOrWhiteSpace(administrator.Username) || string.IsNullOrWhiteSpace(commercial.Username) ||
            administrator.Username == commercial.Username || administrator.Multiplexing || commercial.Multiplexing ||
            administrator.IncludeErrorDetail || commercial.IncludeErrorDetail)
            throw new InvalidOperationException("Performance execution requires explicit opt-in, Performance environment, a new marked database and separate migration/runtime credentials on the same server.");
    }

    public static async Task<DisposablePerformanceDatabase> CreateAsync(PerformanceOptions options, CancellationToken token)
    {
        var control = Environment.GetEnvironmentVariable("MEDIPOS_PERF_CONTROL_CONNECTION") ?? throw new InvalidOperationException("Missing performance control credential.");
        var runtime = Environment.GetEnvironmentVariable("MEDIPOS_PERF_RUNTIME_CONNECTION") ?? throw new InvalidOperationException("Missing performance runtime credential.");
        var database = new DisposablePerformanceDatabase(options, control, runtime);
        try
        {
            await database._control.OpenAsync(token).ConfigureAwait(false);
            database.PostgreSqlVersion = (string)(await database.ScalarAsync("SHOW server_version", token).ConfigureAwait(false))!;
            var version = Convert.ToInt32(await database.ScalarAsync("SHOW server_version_num", token).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
            if (version is < 180000 or >= 190000) throw new InvalidOperationException("Performance scenarios require PostgreSQL 18.");
            // CREATE, not reuse: an existing name fails without touching its contents.
            await database.ExecuteAsync("CREATE DATABASE " + Quote(options.DatabaseName) + " TEMPLATE template0", token).ConfigureAwait(false);
            database._created = true;
            await database.ExecuteAsync("COMMENT ON DATABASE " + Quote(options.DatabaseName) + " IS '" + Marker(options.RunId) + "'", token).ConfigureAwait(false);
            await using (var migration = new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>()
                .UseNpgsql(database._migrationConnection).Options, new TenantDataContext()))
                await migration.Database.MigrateAsync(token).ConfigureAwait(false);
            await database.GrantRuntimeAsync(token).ConfigureAwait(false);
            // Validate the real commercial guard before any generated row; never bypass it for benchmarks.
            await using (var runtimeContext = new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>()
                .UseNpgsql(database.RuntimeConnection).AddInterceptors(new RuntimeDatabaseRoleInterceptor()).Options, new TenantDataContext()))
            {
                await runtimeContext.Database.OpenConnectionAsync(token).ConfigureAwait(false);
                await using (var settings = runtimeContext.Database.GetDbConnection().CreateCommand())
                {
                    settings.CommandText = "SELECT name,setting,COALESCE(unit,'') FROM pg_settings WHERE name IN ('shared_buffers','work_mem','effective_cache_size','max_connections','random_page_cost','track_io_timing','synchronous_commit','autovacuum') ORDER BY name";
                    var values = new Dictionary<string, string>(StringComparer.Ordinal);
                    await using var reader = await settings.ExecuteReaderAsync(token).ConfigureAwait(false);
                    while (await reader.ReadAsync(token).ConfigureAwait(false)) values.Add(reader.GetString(0), reader.GetString(1) + reader.GetString(2));
                    database.Settings = values;
                }
                await runtimeContext.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
            return database;
        }
        catch
        {
            // Cleanup is attempted only for the exact database this instance successfully created and marked.
            try { await database.DisposeAsync().ConfigureAwait(false); } catch { /* Original failure is reported without secrets. */ }
            throw;
        }
    }

    private async Task GrantRuntimeAsync(CancellationToken token)
    {
        await using var connection = new NpgsqlConnection(_migrationConnection);
        await connection.OpenAsync(token).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var role = Quote(new NpgsqlConnectionStringBuilder(RuntimeConnection).Username!);
        command.CommandText = $"""
            REVOKE CREATE ON SCHEMA public FROM PUBLIC, {role};
            GRANT USAGE ON SCHEMA public TO {role};
            GRANT SELECT ON "__EFMigrationsHistory", categories, global_products, medicine_profiles TO {role};
            GRANT SELECT, INSERT, UPDATE ON tenants, users, licenses, legal_entities, branches,
                memberships, purchases, inventory_lots TO {role};
            GRANT SELECT, INSERT ON audit_logs, stock_movements, sale_payments, commission_entries,
                license_changes, business_products, product_units, suppliers,
                branch_catalog_sync_states, branch_catalog_changes TO {role};
            GRANT SELECT, INSERT, UPDATE ON cash_sessions, sales, tenant_commission_settings, commission_rules TO {role};
            GRANT SELECT, INSERT, DELETE ON membership_branches, work_schedules, purchase_lines, sale_lines TO {role};
            GRANT UPDATE (current_version) ON branch_catalog_sync_states TO {role};
            GRANT UPDATE (version, operation, generated_at_utc, projection) ON branch_catalog_changes TO {role};
            """;
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    internal async Task<Guid> CreateCategoryAsync(CancellationToken token)
    {
        var category = MediPOS.Domain.Modules.Catalog.Category.Create("Synthetic performance", DateTimeOffset.UtcNow);
        await using var context = new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>()
            .UseNpgsql(_migrationConnection).Options, new TenantDataContext());
        context.Categories.Add(category);
        await context.SaveChangesAsync(token).ConfigureAwait(false);
        return category.Id;
    }

    internal void ClaimGeneration(string runId)
    {
        if (!_created || runId != _options.RunId || Interlocked.Exchange(ref _generationClaimed, 1) != 0)
            throw new InvalidOperationException("Synthetic generation requires this newly created disposable database, once per run.");
    }

    internal async Task AnalyzeAsync(CancellationToken token)
    {
        if (!_created) throw new InvalidOperationException("Statistics require the active disposable run.");
        await using var connection = new NpgsqlConnection(_migrationConnection);
        await connection.OpenAsync(token).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "ANALYZE business_products, product_units, inventory_lots, stock_movements, memberships, work_schedules, branches, licenses, cash_sessions";
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_created && !_options.KeepDatabase) await DropMarkedAsync(_control, _options.RunId, RuntimeConnection, _migrationConnection).ConfigureAwait(false);
        }
        finally { _created = false; await _control.DisposeAsync().ConfigureAwait(false); }
    }

    public static async Task CleanupAsync(PerformanceOptions options, CancellationToken token)
    {
        var control = Environment.GetEnvironmentVariable("MEDIPOS_PERF_CONTROL_CONNECTION") ?? throw new InvalidOperationException("Missing performance control credential.");
        var runtime = Environment.GetEnvironmentVariable("MEDIPOS_PERF_RUNTIME_CONNECTION") ?? throw new InvalidOperationException("Missing performance runtime credential.");
        ValidateBoundary(options, Environment.GetEnvironmentVariable("MEDIPOS_PERF_OPT_IN"), Environment.GetEnvironmentVariable("MEDIPOS_ENVIRONMENT"), control, runtime);
        await using var connection = new NpgsqlConnection(control);
        await connection.OpenAsync(token).ConfigureAwait(false);
        await DropMarkedAsync(connection, options.RunId, runtime,
            new NpgsqlConnectionStringBuilder(control) { Database = options.DatabaseName }.ConnectionString).ConfigureAwait(false);
    }

    private static async Task DropMarkedAsync(NpgsqlConnection control, string runId, string runtime, string migration)
    {
        await using var verify = control.CreateCommand();
        verify.CommandText = "SELECT shobj_description(d.oid,'pg_database') FROM pg_database d JOIN pg_roles r ON r.oid=d.datdba WHERE d.datname=@name AND r.rolname=current_user";
        verify.Parameters.AddWithValue("name", "medipos_perf_" + runId);
        if (await verify.ExecuteScalarAsync().ConfigureAwait(false) is not string marker || marker != Marker(runId))
            throw new InvalidOperationException("Cleanup requires the exact performance marker and database ownership.");
        using var runtimePool = new NpgsqlConnection(runtime); NpgsqlConnection.ClearPool(runtimePool);
        using var migrationPool = new NpgsqlConnection(migration); NpgsqlConnection.ClearPool(migrationPool);
        await using var drop = control.CreateCommand();
        drop.CommandText = "DROP DATABASE " + Quote("medipos_perf_" + runId) + " WITH (FORCE)";
        await drop.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static string Marker(string runId) => "MediPOS disposable performance v1:" + runId;
    private static string Quote(string identifier) => new NpgsqlCommandBuilder().QuoteIdentifier(identifier);
    private async Task ExecuteAsync(string sql, CancellationToken token)
    { await using var command = _control.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync(token).ConfigureAwait(false); }
    private async Task<object?> ScalarAsync(string sql, CancellationToken token)
    { await using var command = _control.CreateCommand(); command.CommandText = sql; return await command.ExecuteScalarAsync(token).ConfigureAwait(false); }
}
