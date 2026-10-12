using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog.SearchProducts;
using MediPOS.Application.Modules.SalesPos.ConfirmSale;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.Performance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Performance;

// External PostgreSQL regression only; these reuse the existing test fixture. The performance executable
// itself has no Testcontainers dependency and never starts a container. Never execute this class locally.
[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class PerformanceHarnessPostgreSqlTests(PostgreSqlFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RestrictedRuntimeSearchesKeepRankingEquivalenceBranchTenantAndParameterizedExplain()
    {
        await using var harness = await CreateAsync();
        var runner = new PerformanceRunner(harness.Options, harness.Dataset);
        foreach (var scenario in PerformanceRunner.Searches(harness.Options).Where(value =>
            value.Name is "search.barcode-exact" or "search.equivalent" or "search.other-branches" or "search.literal-wildcards" or "search.accents"))
        {
            var employee = harness.Dataset.Operators[0];
            await using var operation = await runner.PrepareSearchAsync(employee, scenario, Token);
            var trace = harness.Observation.Begin(employee.TenantId);
            try { await operation.ExecuteAsync(Token); }
            finally { harness.Observation.End(); }
            await operation.VerifyAsync(Token);
            Assert.Contains("search.access.initial", trace.Phases.Keys);
            Assert.Contains("search.main", trace.Phases.Keys);
            Assert.Contains("connection.runtime-role-guard", trace.Phases.Keys);
            Assert.True(trace.SqlCommands > 0); Assert.True(trace.ConnectionOpens > 0);
        }
        var foreign = harness.Dataset.Tenants[1];
        await using var denied = harness.Dataset.Scope(harness.Dataset.Operators[0].UserId);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => denied.ServiceProvider.GetRequiredService<SearchProductsHandler>()
            .HandleAsync(new(foreign.Id, foreign.Branches[0], "P000004", 10), Token));
        Assert.Equal(ErrorCategory.Forbidden, error.Error.Category);
        var own = harness.Dataset.Tenants[0];
        await using var wrongBranch = harness.Dataset.Scope(harness.Dataset.Operators[0].UserId);
        var branchError = await Assert.ThrowsAsync<ApplicationErrorException>(() => wrongBranch.ServiceProvider.GetRequiredService<SearchProductsHandler>()
            .HandleAsync(new(own.Id, own.Branches[1], "P000004", 10), Token));
        Assert.Equal("access.branch_unassigned", branchError.Error.Code);
        Assert.NotEmpty(await harness.Observation.ExplainAsync(harness.Database.RuntimeConnection, Token));
    }

    [Fact]
    public async Task RealSaleWorkloadsPreserveMixedPaymentsFefoCommissionAndLastLotCompetition()
    {
        await using var harness = await CreateAsync();
        var runner = new PerformanceRunner(harness.Options, harness.Dataset);
        var employees = harness.Dataset.Tenants[0].Operators.Take(2).ToArray();
        await harness.Dataset.SetCommissionsAsync(true, Token);
        await using (var sale = await runner.PrepareSaleAsync(employees[0], new("regression", Mixed: true, MultipleLots: true, Commissions: true), Token))
        { await sale.ExecuteAsync(Token); await sale.VerifyAsync(Token); }
        await harness.Dataset.SetCommissionsAsync(false, Token);
        var product = harness.Dataset.Tenants[0].Products[4];
        await harness.Dataset.ResetLotsAsync(employees[0], product, 1, 1m, Token);
        await using var first = await runner.PrepareSaleAsync(employees[0], new("race"), Token, productIndex: 4);
        await using var second = await runner.PrepareSaleAsync(employees[1], new("race"), Token, productIndex: 4);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> ExecuteAsync(PreparedOperation operation)
        {
            await release.Task;
            try { await operation.ExecuteAsync(Token); return true; }
            catch (ApplicationErrorException error) when (error.Error.Code is "inventory.insufficient_stock" or "sale.concurrent_edit") { return false; }
        }
        var contenders = new[] { ExecuteAsync(first), ExecuteAsync(second) }; release.SetResult();
        var outcomes = await Task.WhenAll(contenders);
        Assert.Single(outcomes, value => value);
        await first.VerifyAsync(Token); await second.VerifyAsync(Token);
        await using var scope = harness.Dataset.Scope();
        var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>(); context.SelectTenant(employees[0].TenantId);
        Assert.False(await context.InventoryLots.AnyAsync(lot => lot.QuantityAvailableBase < 0, Token));
        Assert.Equal(2, await context.Sales.CountAsync(sale => sale.Status == SaleStatus.Confirmed, Token));
    }

    [Fact]
    public async Task GenerationIsClaimedOnceAndExistingDatabaseCannotBeReused()
    {
        await using var harness = await CreateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Dataset.GenerateAsync(Token));
        await using var migration = fixture.CreateConstraintContext();
        var control = new NpgsqlConnectionStringBuilder(migration.Database.GetConnectionString()) { Database = "postgres" }.ConnectionString;
        var runtime = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = harness.Options.DatabaseName }.ConnectionString;
        using var environment = new OptInEnvironment(control, runtime);
        var error = await Assert.ThrowsAsync<PostgresException>(() => DisposablePerformanceDatabase.CreateAsync(harness.Options, Token));
        Assert.Equal(PostgresErrorCodes.DuplicateDatabase, error.SqlState);
        await using var scope = harness.Dataset.Scope();
        var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>(); context.SelectTenant(harness.Dataset.Tenants[0].Id);
        Assert.Equal(128, await context.BusinessProducts.CountAsync(Token));
    }

    private async Task<Harness> CreateAsync()
    {
        var options = new PerformanceOptions
        {
            Products = 128,
            StockedProducts = 128,
            Tenants = 2,
            Branches = 2,
            OperatorsPerBranch = 2,
            Samples = 100,
            Warmup = 10,
            Concurrency = [1, 2],
            EnvironmentLabel = "isolated PostgreSQL regression",
            ServerHardware = "ephemeral regression fixture; not a performance baseline",
            OutputDirectory = Path.Combine(Path.GetTempPath(), "medipos-performance-tests"),
        };
        await using var migration = fixture.CreateConstraintContext();
        var control = new NpgsqlConnectionStringBuilder(migration.Database.GetConnectionString()) { Database = "postgres" }.ConnectionString;
        var runtime = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = options.DatabaseName }.ConnectionString;
        using var environment = new OptInEnvironment(control, runtime);
        var database = await DisposablePerformanceDatabase.CreateAsync(options, Token);
        var observation = new PerformanceObservation();
        var dataset = new PerformanceDataset(options, database, observation);
        try { await dataset.GenerateAsync(Token); return new(options, database, observation, dataset); }
        catch { await dataset.DisposeAsync(); observation.Dispose(); await database.DisposeAsync(); throw; }
    }

    private sealed record Harness(PerformanceOptions Options, DisposablePerformanceDatabase Database,
        PerformanceObservation Observation, PerformanceDataset Dataset) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        { await Dataset.DisposeAsync(); Observation.Dispose(); await Database.DisposeAsync(); }
    }

    // The PostgreSQL collection disables parallel execution; restore process environment after the guarded
    // creation call. No environment override disables the actual runtime role/authorization/RLS checks.
    private sealed class OptInEnvironment : IDisposable
    {
        private readonly Dictionary<string, string?> _previous = new(StringComparer.Ordinal);
        public OptInEnvironment(string control, string runtime)
        {
            Set("MEDIPOS_PERF_OPT_IN", "CREATE_DISPOSABLE_DATABASE"); Set("MEDIPOS_ENVIRONMENT", "Performance");
            Set("MEDIPOS_PERF_CONTROL_CONNECTION", control); Set("MEDIPOS_PERF_RUNTIME_CONNECTION", runtime);
        }
        private void Set(string name, string value) { _previous[name] = Environment.GetEnvironmentVariable(name); Environment.SetEnvironmentVariable(name, value); }
        public void Dispose() { foreach (var pair in _previous) Environment.SetEnvironmentVariable(pair.Key, pair.Value); }
    }
}
