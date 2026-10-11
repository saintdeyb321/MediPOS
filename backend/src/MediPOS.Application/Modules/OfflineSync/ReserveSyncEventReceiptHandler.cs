using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.OfflineSync;

namespace MediPOS.Application.Modules.OfflineSync;

public sealed class ReserveSyncEventReceiptHandler(ResolveAccessContextHandler resolver, ISyncDeviceStore devices,
    ISyncEventInbox inbox, TimeProvider clock)
{
    public async Task<ISyncEventReservation> HandleAsync(ReserveSyncEventCommand command, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(command); ArgumentNullException.ThrowIfNull(command.Event);
        if (command.Event.DeviceId == Guid.Empty || command.Event.EventId == Guid.Empty) throw SyncAccess.Error("sync.identity_invalid");
        // Session/license/membership before private lookup. Client time never participates in authorization.
        await SyncAccess.RequireAsync(resolver, command.TenantId, null, clock, token, allowBranchLookup: true).ConfigureAwait(false);
        var device = await devices.FindAsync(command.TenantId, command.Event.DeviceId, token).ConfigureAwait(false)
            ?? throw SyncAccess.Error("sync.device_not_found", ErrorCategory.NotFound);
        var access = await SyncAccess.RequireAsync(resolver, command.TenantId, device.BranchId, clock, token).ConfigureAwait(false);
        SyncAccess.RequireDevice(device, access);
        // Device ownership is mandatory even for Owner when submitting a device's offline events.
        if (device.RegisteredByMembershipId != access.MembershipId) throw SyncAccess.Error("sync.device_denied", ErrorCategory.Forbidden);
        var candidate = SyncAccess.Receive(access.TenantId, command.Event, clock.GetUtcNow());
        var scope = await inbox.BeginAsync(access.TenantId, device.Id, token).ConfigureAwait(false);
        try
        {
            SyncAccess.RequireDevice(scope.Device, access);
            var persisted = await scope.ReserveOrReadAsync(candidate, token).ConfigureAwait(false);
            var current = await SyncAccess.RequireAsync(resolver, access.TenantId, device.BranchId, clock, token).ConfigureAwait(false);
            if (current.MembershipId != device.RegisteredByMembershipId) throw SyncAccess.Error("sync.device_denied", ErrorCategory.Forbidden);
            return new Reservation(scope, persisted, persisted.Id == candidate.Id, !persisted.HasSameIdentity(candidate), resolver, clock);
        }
        catch { await scope.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private sealed class Reservation(ISyncEventScope scope, SyncEvent syncEvent, bool isNew, bool conflict,
        ResolveAccessContextHandler resolver, TimeProvider clock) : ISyncEventReservation
    {
        private bool _completed;
        // Identity conflicts expose only the conflict flag, never another device's stored payload/result.
        public SyncEvent Receipt => conflict ? throw SyncAccess.Error("sync.identity_conflict", ErrorCategory.Conflict) : syncEvent;
        public bool IsNew => isNew;
        public bool IsIdentityConflict => conflict;
        public async Task CompleteAsync(string? terminalStatus, string? resultCode, Guid? resultReferenceId, CancellationToken token)
        {
            if (_completed) throw new InvalidOperationException("Reservation already completed.");
            var access = await SyncAccess.RequireAsync(resolver, syncEvent.TenantId, scope.Device!.BranchId, clock, token).ConfigureAwait(false);
            SyncAccess.RequireDevice(scope.Device, access);
            if (access.MembershipId != scope.Device.RegisteredByMembershipId) throw SyncAccess.Error("sync.device_denied", ErrorCategory.Forbidden);
            if (conflict && terminalStatus is not null) throw SyncAccess.Error("sync.identity_conflict", ErrorCategory.Conflict);
            if (!conflict && syncEvent.ProcessingStatus == "pending" && terminalStatus is not null)
            {
                try { syncEvent.Finish(terminalStatus, resultCode ?? "", resultReferenceId, clock.GetUtcNow()); }
                catch (ArgumentException error) { throw SyncAccess.Error(error.Message); }
                await scope.SaveResultAsync(syncEvent, token).ConfigureAwait(false);
            }
            else if (resultCode is not null || resultReferenceId.HasValue || terminalStatus is not null)
                throw SyncAccess.Error("sync.event_finalized", ErrorCategory.Conflict);
            await scope.CompleteAsync(token).ConfigureAwait(false); _completed = true;
        }
        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
}
