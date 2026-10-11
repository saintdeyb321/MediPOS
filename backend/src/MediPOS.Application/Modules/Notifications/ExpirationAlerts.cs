using MediPOS.Application.Modules.Inventory;
using MediPOS.Domain.Modules.Notifications;

namespace MediPOS.Application.Modules.Notifications;

// System-only ports: not registered as authenticated HTTP use cases and never accept client authority.
public interface IExpirationAlertsSystem
{
    Task<IReadOnlyList<Guid>> ReadTenantBatchAsync(Guid? afterTenantId, int limit, CancellationToken token);
    Task<int> ProcessTenantAsync(Guid tenantId, DateOnly today, int lotBatchSize, CancellationToken token);
    void TenantFailed(Guid tenantId, Exception exception);
    void RunCompleted(int processedTenants, int failedTenants, int insertedNotifications);
}
public interface IExpirationAlertsBatch : IAsyncDisposable
{
    Task<IReadOnlyList<InventoryExpirationLot>> ReadAsync(Guid? afterLotId, DateOnly today, int limit, CancellationToken token);
    Task<int> InsertAsync(Notification notification, CancellationToken token);
    Task CommitAsync(CancellationToken token);
}
public interface IExpirationAlertsStore
{
    // A null batch means the current tenant license does not allow operation at server time.
    Task<IExpirationAlertsBatch?> BeginAsync(Guid tenantId, DateTimeOffset at, CancellationToken token);
}
public sealed class GenerateTenantExpirationAlerts(IExpirationAlertsStore store, TimeProvider clock)
{
    public async Task<int> HandleAsync(Guid tenantId, DateOnly today, int batchSize, CancellationToken token)
    {
        if (tenantId == Guid.Empty || batchSize is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(batchSize));
        Guid? after = null; var inserted = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            await using var batch = await store.BeginAsync(tenantId, clock.GetUtcNow(), token).ConfigureAwait(false);
            if (batch is null) return inserted;
            var lots = await batch.ReadAsync(after, today, batchSize, token).ConfigureAwait(false);
            foreach (var lot in lots)
            {
                if (lot.TenantId != tenantId || !InventoryCalendar.IsExpiring(lot.ExpirationDate, lot.QuantityAvailableBase, today))
                    throw new InvalidOperationException("Expiration scan returned an ineligible or foreign lot.");
                inserted += await batch.InsertAsync(Notification.ExpiringLot(tenantId, lot.BranchId, lot.InventoryLotId,
                    lot.BusinessProductId, lot.ExpirationDate, clock.GetUtcNow()), token).ConfigureAwait(false);
            }
            await batch.CommitAsync(token).ConfigureAwait(false);
            if (lots.Count < batchSize) return inserted;
            after = lots[^1].InventoryLotId;
        }
    }
}
public sealed record ExpirationAlertsRunResult(int ProcessedTenants, int FailedTenants, int InsertedNotifications);
public sealed class RunExpirationAlertsJob(IExpirationAlertsSystem system, TimeProvider clock)
{
    public async Task<ExpirationAlertsRunResult> HandleAsync(CancellationToken token)
    {
        var today = InventoryCalendar.Today(clock);
        Guid? after = null; var processed = 0; var failed = 0; var inserted = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var tenants = await system.ReadTenantBatchAsync(after, 100, token).ConfigureAwait(false);
            foreach (var tenant in tenants)
            {
                try
                {
                    inserted += await system.ProcessTenantAsync(tenant, today, 100, token).ConfigureAwait(false);
                    processed++;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception error) { failed++; system.TenantFailed(tenant, error); }
            }
            if (tenants.Count < 100) break;
            after = tenants[^1];
        }
        system.RunCompleted(processed, failed, inserted);
        return new(processed, failed, inserted);
    }
}
