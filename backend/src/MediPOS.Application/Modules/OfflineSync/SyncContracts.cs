using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.OfflineSync;

namespace MediPOS.Application.Modules.OfflineSync;

public sealed record RegisterSyncDeviceCommand(Guid TenantId, Guid BranchId, string DeviceLabel);
public sealed record RevokeSyncDeviceCommand(Guid TenantId, Guid DeviceId);
public sealed record SyncEventEnvelope(Guid EventId, Guid DeviceId, string EventType, int SchemaVersion,
    DateTimeOffset ClientOccurredAtUtc, DateOnly ClientLocalDate, string ClientTimeZoneId, string Payload, string PayloadHash);
public sealed record ReserveSyncEventCommand(Guid TenantId, SyncEventEnvelope Event);
public sealed record SyncDeviceDetails(Guid DeviceId, Guid BranchId, string DeviceLabel, string Status,
    DateTimeOffset RegisteredAtUtc, DateTimeOffset? RevokedAtUtc)
{
    public static SyncDeviceDetails From(SyncDevice device) => new(device.Id, device.BranchId, device.DeviceLabel,
        device.Status, device.RegisteredAtUtc, device.RevokedAtUtc);
}

public interface ISyncDeviceStore
{
    Task<SyncDevice?> FindAsync(Guid tenantId, Guid deviceId, CancellationToken token);
    Task<ISyncDeviceScope> BeginAsync(Guid tenantId, Guid? deviceId, CancellationToken token);
}
public interface ISyncDeviceScope : IAsyncDisposable
{
    SyncDevice? Device { get; }
    Task SaveAsync(SyncDevice device, AuditLog audit, CancellationToken token);
    Task CompleteAsync(CancellationToken token);
}

// Opens, but does not commit, one transaction: Device SHARE -> Event UPDATE -> (B5.3)
// CashSession -> Sale -> InventoryLots in deterministic product/lot order.
// B5.3 must enlist sale application in this same scoped persistence transaction, never commit it separately.
// A recovered pending event is reserved again under its row lock; it does not imply a sale was applied.
public interface ISyncEventInbox
{
    Task<ISyncEventScope> BeginAsync(Guid tenantId, Guid deviceId, CancellationToken token);
}
public interface ISyncEventScope : IAsyncDisposable
{
    SyncDevice? Device { get; }
    Task<SyncEvent> ReserveOrReadAsync(SyncEvent candidate, CancellationToken token);
    Task SaveResultAsync(SyncEvent syncEvent, CancellationToken token);
    Task CompleteAsync(CancellationToken token);
}
public interface ISyncEventReservation : IAsyncDisposable
{
    SyncEvent Receipt { get; }
    bool IsNew { get; }
    bool IsIdentityConflict { get; }
    // Null keeps pending. Terminal retries return the saved result without rewriting it.
    Task CompleteAsync(string? terminalStatus, string? resultCode, Guid? resultReferenceId, CancellationToken token);
}
