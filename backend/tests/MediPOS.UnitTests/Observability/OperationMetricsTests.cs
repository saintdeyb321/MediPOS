using MediPOS.Application.Modules.Notifications;
using MediPOS.SharedKernel;
using MediPOS.UnitTests.Modules.OfflineSync;
using System.Diagnostics;

namespace MediPOS.UnitTests.Observability;

[Collection("Observability")]
public sealed class OperationMetricsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task JobRetryCountsRealExecutionsWithoutInventingInsertions()
    {
        using var metrics = new MeterProbe();
        var source = new ExpirationSource { FailRead = true };
        var job = new RunExpirationAlertsJob(source, TimeProvider.System);
        await Assert.ThrowsAsync<InvalidOperationException>(() => job.HandleAsync(Token));
        source.FailRead = false;
        Assert.Equal(1, (await job.HandleAsync(Token)).InsertedNotifications);
        Assert.Equal(0, (await job.HandleAsync(Token)).InsertedNotifications);
        Assert.Equal(3, metrics.Of("medipos.operation.executions").Length);
        Assert.Single(metrics.Of("medipos.operation.executions"), point => Equals(point.Tags["outcome"], "failed"));
        Assert.Equal(3, metrics.Of("medipos.operation.duration").Length);
        Assert.Equal(1, metrics.Of("medipos.expiration_alerts.notifications").Sum(point => point.Value));
        Assert.Empty(metrics.Of("medipos.expiration_alerts.tenant_failures"));
        Assert.All(metrics.Of("medipos.operation.duration"), point => Assert.True(point.Value >= 0));
    }

    [Fact]
    public async Task PartialTenantFailureEmitsOneExecutionAndNoDuplicateJobFailure()
    {
        using var metrics = new MeterProbe();
        var source = new ExpirationSource { FailTenant = true };
        var result = await new RunExpirationAlertsJob(source, TimeProvider.System).HandleAsync(Token);
        Assert.Equal(1, result.FailedTenants);
        Assert.Equal("partial_failure", Assert.Single(metrics.Of("medipos.operation.executions")).Tags["outcome"]);
        Assert.Equal(1, Assert.Single(metrics.Of("medipos.expiration_alerts.tenant_failures")).Value);
    }

    [Fact]
    public async Task CancelledScanIsNotReportedAsTenantOrUnexpectedFailure()
    {
        using var metrics = new MeterProbe();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RunExpirationAlertsJob(new ExpirationSource(), TimeProvider.System).HandleAsync(cancellation.Token));
        Assert.Equal("cancelled", Assert.Single(metrics.Of("medipos.operation.executions")).Tags["outcome"]);
        Assert.Empty(metrics.Of("medipos.expiration_alerts.tenant_failures"));
        Assert.Empty(metrics.Of("medipos.application.unexpected_failures"));
    }

    [Fact]
    public async Task SyncRetriesAndDifferentTenantsShareOnlyBoundedAggregateLabels()
    {
        using var metrics = new MeterProbe();
        var first = new OfflineSyncBatchTestSetup(); var second = new OfflineSyncBatchTestSetup();
        var envelope = first.Envelope();
        var result = await first.Execute(envelope, envelope);
        Assert.False(result.Results[0].IsReplay); Assert.True(result.Results[1].IsReplay);
        await second.Execute(second.Envelope());
        var points = metrics.Of("medipos.offline_sync.event_results");
        Assert.Equal(3, points.Length);
        Assert.Single(points, point => Equals(point.Tags["replay"], true));
        Assert.All(points, point => Assert.Equal(["replay", "status"], point.Tags.Keys.Order().ToArray()));
        Assert.All(metrics.Points.SelectMany(point => point.Tags.Values), value => Assert.False(value is Guid));
        Assert.Equal(2, metrics.Of("medipos.operation.executions").Length);
    }

    [Fact]
    public async Task AbortedBatchReportsOnlyResultsReturnedAfterCommit()
    {
        using var metrics = new MeterProbe();
        var source = new OfflineSyncBatchTestSetup();
        source.Data.BeforeCommit = count => { if (count == 2) throw new InvalidOperationException("synthetic commit failure"); };
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.Execute(source.Envelope(), source.Envelope()));
        Assert.Single(metrics.Of("medipos.offline_sync.event_results"));
        Assert.Equal("failed", Assert.Single(metrics.Of("medipos.operation.executions")).Tags["outcome"]);
        Assert.Single(source.Data.CommittedEvents);
    }

    [Fact]
    public async Task BrokenMetricConsumerCannotAbortSourceWork()
    {
        using var observer = new MeterProbe(throwOnMeasurement: true);
        var result = await new RunExpirationAlertsJob(new ExpirationSource(), TimeProvider.System).HandleAsync(Token);
        Assert.Equal(1, result.InsertedNotifications);
        var source = new OfflineSyncBatchTestSetup();
        var response = await source.Execute(source.Envelope());
        Assert.Single(response.Results); Assert.Single(source.Data.CommittedEvents);
    }

    [Fact]
    public void ObservationDisposalIsIdempotentAndLabelsCannotCopyArbitraryStatus()
    {
        using var metrics = new MeterProbe();
        var observation = OperationalTelemetry.Begin(ObservedOperation.OfflineSyncBatch);
        observation.Complete(); observation.Dispose(); observation.Dispose();
        OperationalTelemetry.SyncResult("tenant=confidential", false);
        Assert.Single(metrics.Of("medipos.operation.executions"));
        Assert.Equal("invalid", Assert.Single(metrics.Of("medipos.offline_sync.event_results")).Tags["status"]);
    }

    [Fact]
    public async Task FailingActivityConsumerCannotLoseWorkOrLeaveAnAmbientTraceBehind()
    {
        var original = Activity.Current;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == OperationalTelemetry.MeterName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = _ => throw new InvalidOperationException("synthetic trace observer failure")
        };
        ActivitySource.AddActivityListener(listener);
        var result = await new RunExpirationAlertsJob(new ExpirationSource(), TimeProvider.System).HandleAsync(Token);
        Assert.Equal(1, result.InsertedNotifications);
        Assert.Same(original, Activity.Current);
    }

    private sealed class ExpirationSource : IExpirationAlertsSystem
    {
        private readonly Guid _tenant = Guid.NewGuid();
        private bool _inserted;
        internal bool FailRead { get; set; }
        internal bool FailTenant { get; set; }
        public Task<IReadOnlyList<Guid>> ReadTenantBatchAsync(Guid? afterTenantId, int limit, CancellationToken token) =>
            FailRead ? throw new InvalidOperationException("synthetic job failure") : Task.FromResult<IReadOnlyList<Guid>>([_tenant]);
        public Task<int> ProcessTenantAsync(Guid tenantId, DateOnly today, int lotBatchSize, CancellationToken token)
        {
            if (FailTenant) throw new InvalidOperationException("synthetic tenant failure");
            var count = _inserted ? 0 : 1; _inserted = true; return Task.FromResult(count);
        }
        public void TenantFailed(Guid tenantId, Exception exception) { }
        public void RunCompleted(int processedTenants, int failedTenants, int insertedNotifications) { }
    }
}
