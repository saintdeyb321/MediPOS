using MediPOS.Application.Modules.Inventory;
using MediPOS.Application.Modules.Notifications;
using MediPOS.Domain.Modules.Notifications;

namespace MediPOS.UnitTests.Modules.Notifications;

public sealed class ExpirationJobTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly DateOnly Today = new(2026, 10, 6);

    [Fact]
    public async Task BoundedTenantEnumerationContinuesAfterOneFailureAndUsesLimaCalendar()
    {
        var system = new SystemPort(); var clock = new Clock();
        var result = await new RunExpirationAlertsJob(system, clock).HandleAsync(Token);
        Assert.Equal(102, result.ProcessedTenants); Assert.Equal(1, result.FailedTenants); Assert.Equal(102, result.InsertedNotifications);
        Assert.Equal(2, system.Pages); Assert.Equal(Today, system.Today); Assert.Single(system.Failures);
        clock.Utc = clock.Utc.AddMinutes(1); await new RunExpirationAlertsJob(system, clock).HandleAsync(Token);
        Assert.Equal(Today.AddDays(1), system.Today);
    }
    [Fact]
    public async Task CancellationStopsEnumerationAndIsNotSwallowedAsTenantFailure()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var system = new SystemPort { Cancel = cancellation };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RunExpirationAlertsJob(system, new Clock()).HandleAsync(cancellation.Token));
        Assert.Empty(system.Failures);
    }
    [Fact]
    public async Task ScannerUsesBoundedTransactionsAndRepeatScanReturnsNoNewEvents()
    {
        var tenant = Guid.NewGuid(); var store = new Store(tenant); var scanner = new GenerateTenantExpirationAlerts(store, new Clock());
        Assert.Equal(3, await scanner.HandleAsync(tenant, Today, 2, Token));
        Assert.Equal(2, store.Commits); Assert.Equal(0, await scanner.HandleAsync(tenant, Today, 2, Token)); Assert.Equal(3, store.History.Count);
        store.LicenseValid = false; Assert.Equal(0, await scanner.HandleAsync(tenant, Today, 2, Token));
    }
    [Fact]
    public async Task ForeignLotAbortsBatchWithoutCommit()
    {
        var tenant = Guid.NewGuid(); var store = new Store(tenant); store.Lots[1] = store.Lots[1] with { TenantId = Guid.NewGuid() };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new GenerateTenantExpirationAlerts(store, new Clock()).HandleAsync(tenant, Today, 3, Token));
        Assert.Empty(store.History); Assert.Equal(0, store.Commits);
    }
    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Utc { get; set; } = new(2026, 10, 7, 4, 59, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Utc;
    }
    private sealed class SystemPort : IExpirationAlertsSystem
    {
        private readonly Guid[] _tenants = Enumerable.Range(0, 103).Select(_ => Guid.CreateVersion7()).Order().ToArray();
        internal int Pages { get; private set; }
        internal DateOnly Today { get; private set; }
        internal List<Guid> Failures { get; } = []; internal CancellationTokenSource? Cancel { get; set; }
        public Task<IReadOnlyList<Guid>> ReadTenantBatchAsync(Guid? afterTenantId, int limit, CancellationToken token)
        { Pages++; return Task.FromResult<IReadOnlyList<Guid>>(_tenants.Where(id => !afterTenantId.HasValue || id.CompareTo(afterTenantId.Value) > 0).Take(limit).ToArray()); }
        public Task<int> ProcessTenantAsync(Guid tenantId, DateOnly today, int lotBatchSize, CancellationToken token)
        {
            if (Cancel is not null) { Cancel.Cancel(); token.ThrowIfCancellationRequested(); }
            Today = today; Assert.Equal(100, lotBatchSize);
            if (tenantId == _tenants[0]) throw new InvalidOperationException("tenant failed");
            return Task.FromResult(1);
        }
        public void TenantFailed(Guid tenantId, Exception exception) => Failures.Add(tenantId);
        public void RunCompleted(int processedTenants, int failedTenants, int insertedNotifications) { }
    }
    // Application port double: verifies coordination/rollback intent, not PostgreSQL atomicity.
    private sealed class Store : IExpirationAlertsStore
    {
        internal Store(Guid tenant) => Lots = Enumerable.Range(0, 3).Select(_ => new InventoryExpirationLot
        { TenantId = tenant, BranchId = Guid.NewGuid(), BusinessProductId = Guid.NewGuid(), InventoryLotId = Guid.CreateVersion7(), ExpirationDate = Today, QuantityAvailableBase = 1m }).OrderBy(l => l.InventoryLotId).ToArray();
        internal InventoryExpirationLot[] Lots { get; }
        internal List<Notification> History { get; } = [];
        internal bool LicenseValid { get; set; } = true; internal int Commits { get; private set; }
        public Task<IExpirationAlertsBatch?> BeginAsync(Guid tenantId, DateTimeOffset at, CancellationToken token) => Task.FromResult<IExpirationAlertsBatch?>(LicenseValid ? new Batch(this) : null);
        private sealed class Batch(Store store) : IExpirationAlertsBatch
        {
            private readonly List<Notification> _pending = [];
            public Task<IReadOnlyList<InventoryExpirationLot>> ReadAsync(Guid? afterLotId, DateOnly today, int limit, CancellationToken token) =>
                Task.FromResult<IReadOnlyList<InventoryExpirationLot>>(store.Lots.Where(l => !afterLotId.HasValue || l.InventoryLotId.CompareTo(afterLotId.Value) > 0).Take(limit).ToArray());
            public Task<int> InsertAsync(Notification notification, CancellationToken token)
            {
                if (store.History.Any(n => n.TenantId == notification.TenantId && n.InventoryLotId == notification.InventoryLotId && n.Type == notification.Type && n.ExpirationDate == notification.ExpirationDate)) return Task.FromResult(0);
                _pending.Add(notification); return Task.FromResult(1);
            }
            public Task CommitAsync(CancellationToken token) { store.History.AddRange(_pending); store.Commits++; return Task.CompletedTask; }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
