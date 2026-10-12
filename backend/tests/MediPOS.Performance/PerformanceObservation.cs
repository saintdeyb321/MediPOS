using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using MediPOS.Application.Tenancy;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace MediPOS.Performance;

public sealed class OperationTrace(Guid tenantId)
{
    public Guid TenantId { get; } = tenantId;
    public Dictionary<string, double> Phases { get; } = new(StringComparer.Ordinal);
    public int SqlCommands { get; set; }
    public int ConnectionOpens { get; set; }
    public void Add(string phase, double milliseconds) => Phases[phase] = Phases.GetValueOrDefault(phase) + milliseconds;
}

public sealed class PerformanceObservation : DbCommandInterceptor, IDisposable
{
    private readonly AsyncLocal<OperationTrace?> _current = new();
    private readonly ActivityListener _activities;
    private readonly ConcurrentDictionary<Guid, long> _commands = new();
    private readonly ConcurrentDictionary<string, ExplainQuery> _explain = new(StringComparer.Ordinal);
    private int _connections;
    private int _peak;
    public ConnectionObservation Connections { get; }
    public int Peak => Volatile.Read(ref _peak);

    public PerformanceObservation()
    {
        Connections = new(this);
        _activities = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("MediPOS.Performance.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity => activity.SetCustomProperty("perf-start", Stopwatch.GetTimestamp()),
            ActivityStopped = activity =>
            {
                if (_current.Value is { } trace && activity.GetCustomProperty("perf-start") is long start)
                    trace.Add(activity.OperationName, PerformanceMetrics.ElapsedMilliseconds(start));
            },
        };
        ActivitySource.AddActivityListener(_activities);
    }

    public OperationTrace Begin(Guid tenantId)
    {
        if (_current.Value is not null) throw new InvalidOperationException("Performance samples must not overlap within an operator.");
        var trace = new OperationTrace(tenantId);
        _current.Value = trace;
        return trace;
    }
    public void End() => _current.Value = null;
    public void ResetPeak() => Interlocked.Exchange(ref _peak, Volatile.Read(ref _connections));

    private void Start(DbCommand command, CommandEventData eventData)
    {
        if (_current.Value is not { } trace) return;
        _commands[eventData.CommandId] = Stopwatch.GetTimestamp();
        trace.SqlCommands++;
    }
    private void Stop(Guid commandId, DbCommand command)
    {
        if (_commands.TryRemove(commandId, out var start) && _current.Value is { } trace)
        {
            var elapsed = PerformanceMetrics.ElapsedMilliseconds(start);
            var category = SearchCategory(command.CommandText);
            trace.Add("sql." + (category ?? (command.CommandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase) ? "locks" : "other")), elapsed);
            if (category is not null)
            {
                // Bounded diagnostic capture: retain the slowest observed read per category, not every query.
                ExplainQuery Snapshot() => new(category, trace.TenantId, command.CommandText,
                    command.Parameters.Cast<NpgsqlParameter>().Select(parameter => parameter.Clone()).ToArray(), elapsed);
                _explain.AddOrUpdate(category, _ => Snapshot(), (_, previous) => elapsed > previous.ElapsedMilliseconds ? Snapshot() : previous);
            }
        }
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    { Start(command, eventData); return result; }
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    { Start(command, eventData); return ValueTask.FromResult(result); }
    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    { Stop(eventData.CommandId, command); return result; }
    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    { Stop(eventData.CommandId, command); return ValueTask.FromResult(result); }
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    { Start(command, eventData); return ValueTask.FromResult(result); }
    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    { Stop(eventData.CommandId, command); return ValueTask.FromResult(result); }
    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    { _commands.TryRemove(eventData.CommandId, out _); return Task.CompletedTask; }

    internal static string? SearchCategory(string sql) => sql.Contains("WITH matches AS", StringComparison.Ordinal) ? "search-main" :
        sql.Contains("WITH available AS", StringComparison.Ordinal) ? "search-equivalences" :
        sql.Contains("normalize_product_search", StringComparison.Ordinal) ? "search-normalization" :
        sql.Contains("inventory_lots", StringComparison.Ordinal) && sql.Contains("branches", StringComparison.Ordinal) && sql.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase) ? "search-other-branches" :
        sql.Contains("product_units", StringComparison.OrdinalIgnoreCase) && sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ? "presentations" : null;

    public async Task<IReadOnlyList<object>> ExplainAsync(string runtime, CancellationToken token)
    {
        var plans = new List<object>();
        foreach (var query in _explain.Values.OrderBy(value => value.Category, StringComparer.Ordinal))
        {
            var tenant = new TenantDataContext(); tenant.SelectTenant(query.TenantId);
            await using var context = new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>()
                .UseNpgsql(runtime).AddInterceptors(new RuntimeDatabaseRoleInterceptor()).Options, tenant);
            await context.Database.OpenConnectionAsync(token).ConfigureAwait(false);
            await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, token).ConfigureAwait(false);
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = "SET TRANSACTION READ ONLY; SET LOCAL pg_trgm.similarity_threshold=0.3; SET LOCAL pg_trgm.word_similarity_threshold=0.6";
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            command.CommandText = "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + query.Sql;
            command.Parameters.AddRange(query.Parameters.Select(parameter => parameter.Clone()).ToArray());
            var plan = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
            plans.Add(new
            {
                query.Category,
                SlowestObservedCommandMilliseconds = query.ElapsedMilliseconds,
                Interpretation = "Post-workload diagnostic replay; not the recorded sample's original plan/snapshot",
                Plan = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>((string)plan!)
            });
            await transaction.CommitAsync(token).ConfigureAwait(false);
        }
        return plans;
    }

    public void Dispose() => _activities.Dispose();
    private sealed record ExplainQuery(string Category, Guid TenantId, string Sql, NpgsqlParameter[] Parameters, double ElapsedMilliseconds);

    // Registered after the runtime guard: opening includes pool/network wait and the mandatory role check.
    public sealed class ConnectionObservation(PerformanceObservation owner) : DbConnectionInterceptor
    {
        private readonly ConcurrentDictionary<Guid, long> _opens = new();
        private readonly ConcurrentDictionary<Guid, byte> _checkedOut = new();
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        { if (owner._current.Value is not null) _opens[eventData.ConnectionId] = Stopwatch.GetTimestamp(); return ValueTask.FromResult(result); }
        public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            var count = _checkedOut.TryAdd(eventData.ConnectionId, 0) ? Interlocked.Increment(ref owner._connections) : Volatile.Read(ref owner._connections);
            int peak;
            do { peak = Volatile.Read(ref owner._peak); if (count <= peak) break; } while (Interlocked.CompareExchange(ref owner._peak, count, peak) != peak);
            if (_opens.TryRemove(eventData.ConnectionId, out var start) && owner._current.Value is { } trace)
            { trace.ConnectionOpens++; trace.Add("connection.open-pool-and-runtime-guard", PerformanceMetrics.ElapsedMilliseconds(start)); }
            return Task.CompletedTask;
        }
        public override Task ConnectionClosedAsync(DbConnection connection, ConnectionEndEventData eventData)
        { if (_checkedOut.TryRemove(eventData.ConnectionId, out _)) Interlocked.Decrement(ref owner._connections); return Task.CompletedTask; }
        public override Task ConnectionFailedAsync(DbConnection connection, ConnectionErrorEventData eventData, CancellationToken cancellationToken = default)
        { _opens.TryRemove(eventData.ConnectionId, out _); return Task.CompletedTask; }
    }
}
