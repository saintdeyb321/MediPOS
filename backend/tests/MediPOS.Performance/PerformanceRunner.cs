using System.Diagnostics;
using MediPOS.Application.Modules.Catalog.SearchProducts;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.ConfirmSale;
using MediPOS.Application.Modules.SalesPos.CreateSaleDraft;
using MediPOS.Application.Modules.SalesPos.ReplaceSaleLines;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.Performance;

public sealed class PreparedOperation(Guid tenantId, AsyncServiceScope scope, Func<CancellationToken, Task> execute,
    Func<CancellationToken, Task>? verify = null) : IAsyncDisposable
{
    public Guid TenantId { get; } = tenantId;
    public Task ExecuteAsync(CancellationToken token) => execute(token);
    public Task VerifyAsync(CancellationToken token) => verify?.Invoke(token) ?? Task.CompletedTask;
    public ValueTask DisposeAsync() => scope.DisposeAsync();
}

public sealed record SearchScenario(string Name, string Text, int Limit);
public sealed record SaleScenario(string Name, int Lines = 1, bool Retail = false, bool Mixed = false,
    bool Fractional = false, bool MultipleLots = false, bool FullWorkflow = false, bool Commissions = false, bool Package = false);

public sealed class PerformanceRunner(PerformanceOptions options, PerformanceDataset dataset)
{
    private static readonly IReadOnlySet<string> NoConflicts = new HashSet<string>(StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> RaceConflicts = new HashSet<string>(StringComparer.Ordinal)
        { "sale.concurrent_edit", "inventory.insufficient_stock" };
    private static readonly IReadOnlySet<string> RetryConflicts = new HashSet<string>(StringComparer.Ordinal)
        { "sale.concurrent_edit", "sale.not_draft" };
    private readonly List<ScenarioResult> _results = [];
    public IReadOnlyList<ScenarioResult> Results => _results;

    public static IReadOnlyList<SearchScenario> Searches(PerformanceOptions options) => new SearchScenario[]
    {
        new("search.barcode-exact", SyntheticCatalog.Product(options.Seed, 4).Barcode, 10),
        new("search.internal-code-exact", "P000004", 10), new("search.name-exact", "Ácido Sintético 500 mg", 10),
        new("search.prefix", "Medicamento sint", 10), new("search.accents", "acido sintetico", 10),
        new("search.typo", "Medicamento sinttico", 10), new("search.ingredient", "Ingrediente sintético", 10),
        new("search.brand-laboratory", "Laboratorio Ñandú", 10), new("search.local-stock", "P000004", 10),
        new("search.no-local-stock", "P000000", 10), new("search.equivalent", "Microgénico sin stock", 10),
        new("search.other-branches", "P000065", 10), new("search.no-results", "zzqvxyzabsent", 10),
        new("search.short", "m", 10), new("search.frequent", "Medicamento sintético", 10),
        new("search.literal-wildcards", "%_\\", 10), new("search.catalog-wide", "sintético", 50),
    }.Concat(options.Limits.Select(limit => new SearchScenario("search.prefix.limit-" + limit, "Medicamento", limit))).ToArray();

    public static IReadOnlyList<SaleScenario> Sales { get; } = new SaleScenario[]
    {
        new("sale.one-line"), new("sale.multiple-lines", Lines:5), new("sale.mixed-payment", Mixed:true),
        new("sale.multiple-lots-fefo", MultipleLots:true), new("sale.retail", Retail:true),
        new("sale.fractional-unit", Fractional:true), new("sale.commissions-enabled", Commissions:true),
        new("sale.package-presentation", Package:true),
        new("sale.commissions-disabled"), new("sale.multiple-sellers-sessions-branches-tenants"),
        new("sale.full-draft-lines-confirm", Lines:3, Mixed:true, FullWorkflow:true),
    };

    public async Task RunAsync(CancellationToken token)
    {
        if (options.Suite is "all" or "search")
            foreach (var concurrency in options.Concurrency)
                foreach (var search in Searches(options))
                    await ScenarioAsync(search.Name, concurrency, 500, NoConflicts,
                        (i, t) => PrepareSearchAsync(dataset.Operators[i % dataset.Operators.Count], search, t), token).ConfigureAwait(false);
        if (options.Suite is "all" or "sales")
        {
            foreach (var sale in Sales)
            {
                await dataset.SetCommissionsAsync(sale.Commissions, token).ConfigureAwait(false);
                foreach (var concurrency in options.Concurrency)
                    await ScenarioAsync(sale.Name, concurrency, sale.FullWorkflow ? null : 1000, NoConflicts,
                        (i, t) => PrepareSaleAsync(dataset.Operators[i % dataset.Operators.Count], sale, t), token).ConfigureAwait(false);
            }
            await dataset.SetCommissionsAsync(false, token).ConfigureAwait(false);
            foreach (var concurrency in options.Concurrency)
                await ScenarioAsync("sale.insufficient-stock", concurrency, null, new HashSet<string>(StringComparer.Ordinal) { "inventory.insufficient_stock" },
                    (i, t) => PrepareSaleAsync(dataset.Operators[i % dataset.Operators.Count], new("shortage"), t, productIndex: 0), token).ConfigureAwait(false);
            if (options.OperatorsPerBranch >= 2)
            {
                await PairsAsync("sale.two-without-contention", contended: false, retry: false, token).ConfigureAwait(false);
                await PairsAsync("sale.competing-for-last-lot", contended: true, retry: false, token).ConfigureAwait(false);
                await PairsAsync("sale.concurrent-same-confirmation", contended: false, retry: true, token).ConfigureAwait(false);
            }
            else
                foreach (var name in new[] { "sale.two-without-contention", "sale.competing-for-last-lot", "sale.concurrent-same-confirmation" })
                    _results.Add(PerformanceMetrics.Summarize(name + ".requires-two-operators-per-branch", 2, 0, [], 0, null, 0));
        }
        if (options.Suite is "all" or "mixed")
        {
            await dataset.SetCommissionsAsync(false, token).ConfigureAwait(false);
            foreach (var concurrency in options.Concurrency)
                await ScenarioAsync("mixed.80-search-20-confirm", concurrency, null, NoConflicts,
                    (i, t) => i % 5 == 0 ? PrepareSaleAsync(dataset.Operators[i % dataset.Operators.Count], new("mixed-sale"), t) :
                        PrepareSearchAsync(dataset.Operators[i % dataset.Operators.Count], new("mixed-search", "Medicamento sintético", 10), t), token).ConfigureAwait(false);
        }
        await VerifyLedgerAsync(token).ConfigureAwait(false);
    }

    public Task<PreparedOperation> PrepareSearchAsync(BenchmarkOperator employee, SearchScenario scenario, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var scope = dataset.Scope(employee.UserId);
        var handler = scope.ServiceProvider.GetRequiredService<SearchProductsHandler>();
        SearchProductsResult? result = null;
        var text = scenario.Name == "search.other-branches" ?
            SyntheticCatalog.Product(options.Seed, 65 + Array.IndexOf(dataset.Tenant(employee.TenantId).Branches, employee.BranchId) * 2).Code : scenario.Text;
        return Task.FromResult(new PreparedOperation(employee.TenantId, scope, async t =>
        {
            result = await handler.HandleAsync(
                new(employee.TenantId, employee.BranchId, text, scenario.Limit), t).ConfigureAwait(false);
        }, t => VerifySearchAsync(employee, scenario, result, t)));
    }

    private async Task<SaleDraftDetails> DraftAsync(BenchmarkOperator employee, SaleScenario scenario, IServiceProvider provider,
        int? productIndex, CancellationToken token)
    {
        var draft = await provider.GetRequiredService<CreateSaleDraftHandler>().HandleAsync(new(employee.TenantId, employee.BranchId), token).ConfigureAwait(false);
        var products = dataset.Tenant(employee.TenantId).Products;
        var start = productIndex ?? (scenario.MultipleLots ? 8 + employee.Slot % 4 * 2 : 16 + employee.Slot % 4 * 12 + (scenario.Retail ? 1 : 0));
        var lines = Enumerable.Range(0, scenario.Lines).Select(line =>
        {
            var product = products[start + line * 2];
            return new SaleLineInput(product.Id, scenario.Package ? product.PackageUnit : scenario.Fractional ? product.FractionUnit : product.BaseUnit,
                scenario.MultipleLots ? 2m : scenario.Fractional ? 3m : 1m, PriceKind.Retail);
        }).ToArray();
        return await provider.GetRequiredService<ReplaceSaleLinesHandler>().HandleAsync(
            new(employee.TenantId, employee.BranchId, draft.SaleId, draft.Version, lines), token).ConfigureAwait(false);
    }

    private static IReadOnlyList<SalePaymentInput> Payments(SaleDraftDetails sale, bool mixed) => mixed ?
        [new(PaymentMethod.Cash, sale.TotalAmount / 2m), new(PaymentMethod.Yape, sale.TotalAmount / 2m)] : [new(PaymentMethod.Cash, sale.TotalAmount)];

    public async Task<PreparedOperation> PrepareSaleAsync(BenchmarkOperator employee, SaleScenario scenario, CancellationToken token,
        int? productIndex = null, SaleDraftDetails? existing = null)
    {
        if (scenario.MultipleLots)
            await dataset.ResetLotsAsync(employee, dataset.Tenant(employee.TenantId).Products[8 + employee.Slot % 4 * 2], 2, 1m, token).ConfigureAwait(false);
        var draft = existing;
        if (!scenario.FullWorkflow && draft is null)
        {
            await using var preparation = dataset.Scope(employee.UserId);
            draft = await DraftAsync(employee, scenario, preparation.ServiceProvider, productIndex, token).ConfigureAwait(false);
        }
        var execution = dataset.Scope(employee.UserId);
        var confirmation = execution.ServiceProvider.GetRequiredService<ConfirmSaleHandler>();
        var confirmed = false;
        return new(employee.TenantId, execution, async t =>
        {
            if (scenario.FullWorkflow) draft = await DraftAsync(employee, scenario, execution.ServiceProvider, productIndex, t).ConfigureAwait(false);
            var sale = draft ?? throw new InvalidOperationException("Draft preparation failed.");
            await confirmation.HandleAsync(
                new(employee.TenantId, employee.BranchId, sale.SaleId, sale.Version, Payments(sale, scenario.Mixed)), t).ConfigureAwait(false);
            confirmed = true;
        }, t => draft is null ? Task.CompletedTask : VerifySaleAsync(employee.TenantId, draft.SaleId, scenario, confirmed, existing is not null, t));
    }

    private async Task ScenarioAsync(string name, int concurrency, double? target, IReadOnlySet<string> expected,
        Func<int, CancellationToken, Task<PreparedOperation>> prepare, CancellationToken token)
    {
        var samples = new List<OperationSample>(options.Samples);
        double window = 0;
        dataset.Observation.ResetPeak();
        for (var phase = 0; phase < 2; phase++)
        {
            var count = phase == 0 ? options.Warmup : options.Samples;
            for (var offset = 0; offset < count; offset += concurrency)
            {
                var operations = new List<PreparedOperation>();
                try
                {
                    // Preparation is not part of handler latency or measured throughput windows. Sequential
                    // preparation also avoids collisions in the domain's (seller, cash session) draft policy.
                    for (var i = offset; i < Math.Min(offset + concurrency, count); i++) operations.Add(await prepare(i, token).ConfigureAwait(false));
                    var batch = await MeasureBatchAsync(dataset.Observation, operations, expected, token).ConfigureAwait(false);
                    if (phase != 0) { samples.AddRange(batch.Samples); window += batch.Seconds; }
                    else if (batch.Samples.Any(sample => sample.Outcome == OutcomeKind.Error))
                        throw new InvalidOperationException("Warmup encountered an unexpected error; baseline is invalid.");
                    foreach (var operation in operations) await operation.VerifyAsync(token).ConfigureAwait(false);
                }
                finally { foreach (var operation in operations) await operation.DisposeAsync().ConfigureAwait(false); }
            }
            if (phase == 0) dataset.Observation.ResetPeak();
        }
        var result = PerformanceMetrics.Summarize(name, concurrency, options.Warmup, samples, window, target, dataset.Observation.Peak);
        _results.Add(result);
        Console.WriteLine($"{name}: attempts={result.Attempts}, success={result.Successful}, conflicts={result.ExpectedConflicts}, errors={result.Errors}, target={result.TargetResult}");
    }

    private async Task PairsAsync(string name, bool contended, bool retry, CancellationToken token)
    {
        var samples = new List<OperationSample>(options.Samples);
        double seconds = 0;
        var employees = dataset.Tenants[0].Operators.Where(employee => employee.BranchId == dataset.Tenants[0].Branches[0]).Take(2).ToArray();
        dataset.Observation.ResetPeak();
        for (var phase = 0; phase < 2; phase++)
        {
            var count = (phase == 0 ? options.Warmup : options.Samples) / 2;
            for (var pair = 0; pair < count; pair++)
            {
                var product = dataset.Tenants[0].Products[4];
                if (contended) await dataset.ResetLotsAsync(employees[0], product, 1, 1m, token).ConfigureAwait(false);
                var scenario = new SaleScenario(name);
                await using var preparation = dataset.Scope(employees[0].UserId);
                var common = retry ? await DraftAsync(employees[0], scenario, preparation.ServiceProvider, null, token).ConfigureAwait(false) : null;
                await using var first = await PrepareSaleAsync(employees[0], scenario, token, contended ? 4 : null, common).ConfigureAwait(false);
                await using var second = await PrepareSaleAsync(retry ? employees[0] : employees[1], scenario, token, contended ? 4 : null, common).ConfigureAwait(false);
                var batch = await MeasureBatchAsync(dataset.Observation, [first, second], retry ? RetryConflicts : contended ? RaceConflicts : NoConflicts, token).ConfigureAwait(false);
                if (phase == 0 && batch.Samples.Any(sample => sample.Outcome == OutcomeKind.Error))
                    throw new InvalidOperationException("Pair warmup encountered an unexpected error; baseline is invalid.");
                if ((contended || retry) && (batch.Samples.Count(value => value.Outcome == OutcomeKind.Success) != 1 ||
                    batch.Samples.Count(value => value.Outcome == OutcomeKind.ExpectedConflict) != 1))
                    throw new InvalidOperationException("Contention scenario must commit exactly one sale and reject its competitor.");
                if (phase != 0) { samples.AddRange(batch.Samples); seconds += batch.Seconds; }
                await first.VerifyAsync(token).ConfigureAwait(false); await second.VerifyAsync(token).ConfigureAwait(false);
            }
            if (phase == 0) dataset.Observation.ResetPeak();
        }
        _results.Add(PerformanceMetrics.Summarize(name, 2, options.Warmup, samples, seconds, 1000, dataset.Observation.Peak));
    }

    private async Task VerifySearchAsync(BenchmarkOperator employee, SearchScenario scenario, SearchProductsResult? result, CancellationToken token)
    {
        if (result is null) return; // Unexpected handler errors remain errors in the metrics.
        if (result.DirectMatches.Count > scenario.Limit || result.EquivalentInBranch.Count > scenario.Limit || result.OtherBranchAvailability.Count > scenario.Limit ||
            result.DirectMatches.Select(value => value.BusinessProductId).Distinct().Count() != result.DirectMatches.Count ||
            result.EquivalentInBranch.Any(value => value.Product.ProductType != MediPOS.Domain.Modules.Catalog.ProductType.Medicine ||
                value.Product.QuantityAvailableBase <= 0 || value.ForDirectProducts.Count == 0) ||
            result.OtherBranchAvailability.Any(value => value.BranchId == employee.BranchId || value.QuantityAvailableBase <= 0 ||
                !dataset.Tenant(employee.TenantId).Branches.Contains(value.BranchId)))
            throw new InvalidOperationException("Measured search violated bounds/equivalence/branch invariants.");
        var ids = result.DirectMatches.Select(value => value.BusinessProductId)
            .Concat(result.EquivalentInBranch.Select(value => value.Product.BusinessProductId))
            .Concat(result.OtherBranchAvailability.Select(value => value.BusinessProductId)).Distinct().ToArray();
        await using var verification = dataset.Scope();
        var context = verification.ServiceProvider.GetRequiredService<MediPosDbContext>(); context.SelectTenant(employee.TenantId);
        if (await context.BusinessProducts.CountAsync(product => ids.Contains(product.Id) && product.IsActive, token).ConfigureAwait(false) != ids.Length)
            throw new InvalidOperationException("Measured search returned a foreign/inactive product.");
        if (scenario.Name is "search.barcode-exact" or "search.internal-code-exact" or "search.name-exact")
            if (result.DirectMatches.Count == 0 || result.DirectMatches[0].BusinessProductId != dataset.Tenant(employee.TenantId).Products[4].Id)
                throw new InvalidOperationException("Exact search ranking changed.");
        if (scenario.Name == "search.equivalent" && !result.EquivalentInBranch.Any(value => value.Product.BusinessProductId == dataset.Tenant(employee.TenantId).Products[2].Id))
            throw new InvalidOperationException("Synthetic structured equivalent was not returned.");
        if (scenario.Name == "search.other-branches" && options.Branches > 1 && result.OtherBranchAvailability.Count == 0)
            throw new InvalidOperationException("Synthetic other-branch stock was not returned.");
        if (scenario.Name is "search.no-results" or "search.literal-wildcards" && ids.Length != 0)
            throw new InvalidOperationException("Synthetic no-match/literal wildcard search changed.");
    }

    public static async Task<(OperationSample[] Samples, double Seconds)> MeasureBatchAsync(PerformanceObservation observation, IReadOnlyList<PreparedOperation> operations,
        IReadOnlySet<string> expected, CancellationToken token)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = operations.Select(operation => MeasureAsync(observation, operation, ready.Task, expected, token)).ToArray();
        var start = Stopwatch.GetTimestamp(); ready.SetResult();
        var samples = await Task.WhenAll(tasks).ConfigureAwait(false);
        return (samples, Stopwatch.GetElapsedTime(start).TotalSeconds);
    }

    private static async Task<OperationSample> MeasureAsync(PerformanceObservation observation, PreparedOperation operation, Task release, IReadOnlySet<string> expected, CancellationToken token)
    {
        await release.WaitAsync(token).ConfigureAwait(false);
        var trace = observation.Begin(operation.TenantId);
        var start = Stopwatch.GetTimestamp();
        var outcome = OutcomeKind.Success; string? code = null;
        try { await operation.ExecuteAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException)
        { outcome = PerformanceMetrics.Classify(error, expected, out var stable); code = stable; }
        finally { observation.End(); }
        return new(PerformanceMetrics.ElapsedMilliseconds(start), outcome, code, trace.Phases, trace.SqlCommands, trace.ConnectionOpens, observation.Peak);
    }

    public static void ValidateSaleOutcome(SaleStatus status, bool successful, bool sharedRetry = false)
    {
        if (successful ? status != SaleStatus.Confirmed : status != SaleStatus.Draft && !(sharedRetry && status == SaleStatus.Confirmed))
            throw new InvalidOperationException("Confirmation outcome disagrees with persisted sale status.");
    }

    private async Task VerifySaleAsync(Guid tenantId, Guid saleId, SaleScenario scenario, bool successful, bool sharedRetry, CancellationToken token)
    {
        await using var scope = dataset.Scope();
        var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>(); context.SelectTenant(tenantId);
        var sale = await context.Sales.AsNoTracking().Include(value => value.Lines).SingleAsync(value => value.Id == saleId, token).ConfigureAwait(false);
        ValidateSaleOutcome(sale.Status, successful, sharedRetry);
        var payments = await context.SalePayments.AsNoTracking().Where(value => value.SaleId == saleId).ToListAsync(token).ConfigureAwait(false);
        var commissions = await context.CommissionEntries.AsNoTracking().Where(value => value.SaleId == saleId).ToListAsync(token).ConfigureAwait(false);
        var lines = sale.Lines.Select(line => line.Id).ToArray();
        var movements = await context.StockMovements.AsNoTracking().Where(value => value.SourceSaleLineId.HasValue && lines.Contains(value.SourceSaleLineId.Value)).ToListAsync(token).ConfigureAwait(false);
        var audits = await context.AuditLogs.CountAsync(value => value.EntityId == saleId && value.Action == AuditAction.SaleConfirmed, token).ConfigureAwait(false);
        if (sale.Status == SaleStatus.Confirmed)
        {
            if (payments.Sum(value => value.Amount) != sale.TotalAmount || payments.Select(value => value.Method).Distinct().Count() != payments.Count ||
                commissions.Count != sale.CommissionEntryCount || commissions.Any(value => value.Amount <= 0) || audits != 1 ||
                (scenario.Commissions && commissions.Count != sale.Lines.Count) ||
                (scenario.MultipleLots && movements.Select(value => value.InventoryLotId).Distinct().Count() != 2) ||
                sale.Lines.Any(line => movements.Where(value => value.SourceSaleLineId == line.Id).Sum(value => -value.QuantityDeltaBase) != line.BaseQuantity) ||
                movements.Any(value => value.MovementType != StockMovementType.Sale))
                throw new InvalidOperationException("Measured confirmation violated payment/stock/commission/audit invariants.");
            var lotIds = movements.Select(value => value.InventoryLotId).Distinct().ToArray();
            if (await context.InventoryLots.AnyAsync(lot => lotIds.Contains(lot.Id) && lot.ExpirationDate < DateOnly.FromDateTime(PerformanceDataset.BusinessTime.Date), token).ConfigureAwait(false))
                throw new InvalidOperationException("Measured sale consumed expired stock.");
        }
        else if (payments.Count != 0 || commissions.Count != 0 || movements.Count != 0 || audits != 0)
            throw new InvalidOperationException("Rejected confirmation left financial or stock effects.");
    }

    private async Task VerifyLedgerAsync(CancellationToken token)
    {
        foreach (var tenant in dataset.Tenants)
        {
            await using var scope = dataset.Scope();
            var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>(); context.SelectTenant(tenant.Id);
            if (await context.InventoryLots.AnyAsync(lot => lot.QuantityAvailableBase < 0 || lot.QuantityAvailableBase !=
                context.StockMovements.Where(movement => movement.InventoryLotId == lot.Id).Sum(movement => movement.QuantityDeltaBase), token).ConfigureAwait(false))
                throw new InvalidOperationException("Synthetic stock balances diverged from their movement ledger.");
        }
    }
}
