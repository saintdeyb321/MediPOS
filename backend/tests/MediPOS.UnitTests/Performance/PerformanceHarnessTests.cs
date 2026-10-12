using System.Diagnostics;
using MediPOS.Application.Errors;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Performance;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.UnitTests.Performance;

public sealed class PerformanceHarnessTests
{
    private static PerformanceOptions Options => new()
    { EnvironmentLabel = "isolated-test", ServerHardware = "synthetic CPU/RAM/storage", OutputDirectory = Path.Combine(Path.GetTempPath(), "medipos-performance-tests") };

    [Fact]
    public void NearestRankHasCorrectTailAndMedianWithoutInterpolation()
    {
        var values = PerformanceMetrics.Calculate(Enumerable.Range(1, 1000).Reverse().Select(value => (double)value));
        Assert.Equal(1, values.Minimum); Assert.Equal(500, values.P50); Assert.Equal(950, values.P95);
        Assert.Equal(990, values.P99); Assert.Equal(1000, values.Maximum); Assert.Equal(1000, values.Samples);
        var ties = PerformanceMetrics.Calculate([0, 2, 2, 4]);
        Assert.Equal(2, ties.P50); Assert.Equal(4, ties.P95); Assert.Equal(4, ties.P99);
        var one = PerformanceMetrics.Calculate([7]); Assert.Equal(7, one.P95);
        Assert.Null(PerformanceMetrics.Calculate([]).P95);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidLatenciesAreRejected(double value) => Assert.Throws<ArgumentException>(() => PerformanceMetrics.Calculate([value]));

    [Theory]
    [InlineData(499, "PASS")]
    [InlineData(500, "FAIL")]
    [InlineData(501, "FAIL")]
    public void TargetIsStrictAndConflictsDoNotBecomeIndependentSuccessfulSales(double latency, string expected)
    {
        var success = new OperationSample(latency, OutcomeKind.Success, null, new Dictionary<string, double> { ["sql"] = 3 }, 2, 1, 1);
        var conflict = success with { Milliseconds = 1, Outcome = OutcomeKind.ExpectedConflict, Code = "sale.concurrent_edit" };
        var samples = Enumerable.Repeat(success, 100).Concat(Enumerable.Repeat(conflict, 20)).ToArray();
        var result = PerformanceMetrics.Summarize("synthetic", 2, 10, samples, 2, 500, 2);
        Assert.Equal(expected, result.TargetResult); Assert.Equal(latency, result.SuccessLatency.P95);
        Assert.Equal(1, result.ConflictLatency.P95); Assert.Equal(100, result.Successful);
        Assert.Equal(20, result.ExpectedConflicts); Assert.Equal(50, result.SuccessfulPerSecond);
        Assert.Equal(60, result.AttemptsPerSecond); Assert.Equal(20d / 120, result.ContentionRate);
        Assert.Equal(240, result.SqlCommands); Assert.Equal(120, result.ConnectionOpens);
    }

    [Fact]
    public void InsufficientSamplesOrUnexpectedErrorsNeverApproveATarget()
    {
        var sample = new OperationSample(1, OutcomeKind.Success, null, new Dictionary<string, double>(), 0, 0, 0);
        Assert.Equal("NOT_MEASURED", PerformanceMetrics.Summarize("empty", 1, 10, [], 0, 500, 0).TargetResult);
        Assert.Equal("NOT_MEASURED", PerformanceMetrics.Summarize("small", 1, 10, Enumerable.Repeat(sample, 99).ToArray(), 1, 500, 0).TargetResult);
        Assert.Equal("NOT_MEASURED", PerformanceMetrics.Summarize("errors", 1, 10,
            Enumerable.Repeat(sample, 100).Append(sample with { Outcome = OutcomeKind.Error, Code = "Unexpected" }).ToArray(), 1, 500, 0).TargetResult);
        Assert.Equal("NOT_MEASURED", MediPOS.Performance.Program.Aggregate([]));
    }

    [Fact]
    public void OnlyExplicitScenarioBusinessConflictsAreExpectedAndMessagesAreNotCaptured()
    {
        var expected = new HashSet<string>(StringComparer.Ordinal) { "inventory.insufficient_stock" };
        var denied = new ApplicationErrorException(new("inventory.insufficient_stock", ErrorCategory.Conflict, "SECRET payload"));
        Assert.Equal(OutcomeKind.ExpectedConflict, PerformanceMetrics.Classify(denied, expected, out var code));
        Assert.Equal("inventory.insufficient_stock", code);
        Assert.Equal(OutcomeKind.Error, PerformanceMetrics.Classify(denied, new HashSet<string>(), out _));
        Assert.Equal(OutcomeKind.Error, PerformanceMetrics.Classify(new InvalidOperationException("password=SECRET"), expected, out code));
        Assert.Equal(nameof(InvalidOperationException), code);
    }

    [Theory]
    [InlineData(1, 2000)]
    [InlineData(3, 10000)]
    [InlineData(5, 30000)]
    public void SyntheticProfilesAndScenarioMatrixAreBounded(int branches, int products)
    {
        var options = Options with { Branches = branches, Products = products, OperatorsPerBranch = 4 };
        options.Validate();
        var searches = PerformanceRunner.Searches(options);
        Assert.Equal(searches.Count, searches.Select(value => value.Name).Distinct().Count());
        Assert.Contains(searches, value => value.Name == "search.equivalent");
        Assert.Contains(searches, value => value.Name == "search.literal-wildcards");
        Assert.All(searches, value => Assert.InRange(value.Limit, 1, 50));
        Assert.Contains(PerformanceRunner.Sales, value => value.MultipleLots);
        Assert.Contains(PerformanceRunner.Sales, value => value.FullWorkflow);
        Assert.Contains(PerformanceRunner.Sales, value => value.Commissions);
    }

    [Fact]
    public void SeedContentIsDeterministicAndPreservesMedicineUnitsAndSpanishUnicode()
    {
        var first = Enumerable.Range(0, 2000).Select(i => SyntheticCatalog.Product(6282, i)).ToArray();
        Assert.Equal(first, Enumerable.Range(0, 2000).Select(i => SyntheticCatalog.Product(6282, i)));
        Assert.NotEqual(first[100].Brand, SyntheticCatalog.Product(6283, 100).Brand);
        Assert.Contains("Ácido", first[4].Name, StringComparison.Ordinal);
        Assert.Contains("Ñandú", first[4].Brand, StringComparison.Ordinal);
        Assert.Equal("25 µg", first[0].Strength);
        var original = MedicineData.Create([MedicineComponent.Create(first[0].Ingredient!, first[0].Strength!)], "Tableta", "Oral");
        var equivalent = MedicineData.Create([MedicineComponent.Create(first[2].Ingredient!, first[2].Strength!)], "Tableta", "Oral");
        Assert.Equal(original.EquivalenceKey, equivalent.EquivalenceKey);
        Assert.Equal(first.Length, first.Select(value => value.Code).Distinct().Count());
        Assert.Contains(first, value => value.Type == ProductType.Retail && value.Ingredient is null);
    }

    [Theory]
    [InlineData("opt-in")]
    [InlineData("environment")]
    [InlineData("database")]
    [InlineData("same-role")]
    [InlineData("multiplexing")]
    [InlineData("server")]
    [InlineData("detail")]
    public void ExecutionBoundaryRejectsUnsafeConfigurationBeforeAnyConnection(string defect)
    {
        var options = Options;
        var control = "Host=localhost;Database=postgres;Username=fixture_admin;Password=SECRET";
        var runtime = $"Host=localhost;Database={options.DatabaseName};Username=fixture_runtime;Password=SECRET";
        var environment = "Performance"; var optIn = "CREATE_DISPOSABLE_DATABASE";
        switch (defect)
        {
            case "opt-in": optIn = ""; break;
            case "environment": environment = "Production"; break;
            case "database": runtime = runtime.Replace(options.DatabaseName, "customer_production", StringComparison.Ordinal); break;
            case "same-role": runtime = runtime.Replace("fixture_runtime", "fixture_admin", StringComparison.Ordinal); break;
            case "multiplexing": runtime += ";Multiplexing=true"; break;
            case "server": runtime = runtime.Replace("localhost", "foreign-server", StringComparison.Ordinal); break;
            case "detail": runtime += ";Include Error Detail=true"; break;
        }
        var error = Assert.Throws<InvalidOperationException>(() => DisposablePerformanceDatabase.ValidateBoundary(options, optIn, environment, control, runtime));
        Assert.DoesNotContain("SECRET", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidRunIdentifiersAndResourceLimitsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => (Options with { RunId = "../production" }).Validate());
        Assert.Throws<ArgumentException>(() => (Options with { Products = 30001 }).Validate());
        Assert.Throws<ArgumentException>(() => (Options with { Samples = 10001 }).Validate());
        Assert.Throws<ArgumentException>(() => (Options with { Tenants = 1, Branches = 1, OperatorsPerBranch = 1 }).Validate());
        Assert.Throws<ArgumentException>(() => (Options with { Limits = [51] }).Validate());
        Assert.Throws<ArgumentException>(() => (Options with { OutputDirectory = Environment.CurrentDirectory }).Validate());
    }

    [Fact]
    public async Task ActivitySamplesStayIndependentAcrossConcurrentAsyncOperators()
    {
        using var observation = new PerformanceObservation();
        using var source = new ActivitySource("MediPOS.Performance.Unit");
        async Task<OperationTrace> RunAsync(string phase)
        {
            var trace = observation.Begin(Guid.NewGuid());
            using (source.StartActivity(phase)) await Task.Yield();
            observation.End();
            return trace;
        }
        var traces = await Task.WhenAll(RunAsync("worker.one"), RunAsync("worker.two"));
        Assert.Equal(["worker.one"], traces[0].Phases.Keys);
        Assert.Equal(["worker.two"], traces[1].Phases.Keys);
        Assert.NotEqual(traces[0].TenantId, traces[1].TenantId);
        Assert.All(traces, trace => Assert.True(trace.Phases.Values.Single() >= 0));
    }

    [Fact]
    public async Task BatchAwaitsIndependentOperationsAndExcludesVerificationFromMeasuredWork()
    {
        await using var services = new ServiceCollection().AddScoped<ScopeMarker>().BuildServiceProvider();
        using var observation = new PerformanceObservation();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0; var verified = 0;
        var firstScope = services.CreateAsyncScope(); var secondScope = services.CreateAsyncScope();
        Assert.NotSame(firstScope.ServiceProvider.GetRequiredService<ScopeMarker>(), secondScope.ServiceProvider.GetRequiredService<ScopeMarker>());
        async Task WorkAsync(CancellationToken token)
        {
            if (Interlocked.Increment(ref entered) == 2) ready.SetResult();
            await ready.Task.WaitAsync(token);
        }
        Task VerifyAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); Interlocked.Increment(ref verified); return Task.CompletedTask; }
        await using var first = new PreparedOperation(Guid.NewGuid(), firstScope, WorkAsync, VerifyAsync);
        await using var second = new PreparedOperation(Guid.NewGuid(), secondScope, async token =>
        {
            await WorkAsync(token);
            throw new ApplicationErrorException(new("sale.concurrent_edit", ErrorCategory.Conflict, "Synthetic conflict"));
        }, VerifyAsync);
        var result = await PerformanceRunner.MeasureBatchAsync(observation, [first, second],
            new HashSet<string>(StringComparer.Ordinal) { "sale.concurrent_edit" }, TestContext.Current.CancellationToken);
        Assert.Equal(2, entered); Assert.Equal(0, verified);
        Assert.Single(result.Samples, sample => sample.Outcome == OutcomeKind.Success);
        Assert.Single(result.Samples, sample => sample.Outcome == OutcomeKind.ExpectedConflict);
        Assert.True(result.Seconds >= 0);
        await first.VerifyAsync(TestContext.Current.CancellationToken); await second.VerifyAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, verified);
    }

    public sealed class ScopeMarker;
}
