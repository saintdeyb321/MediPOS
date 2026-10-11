using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.OfflineSync;

namespace MediPOS.Application.Modules.OfflineSync;

public sealed class RegisterSyncDeviceHandler(ResolveAccessContextHandler resolver, ISyncDeviceStore store, TimeProvider clock)
{
    public async Task<SyncDeviceDetails> HandleAsync(RegisterSyncDeviceCommand command, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.BranchId == Guid.Empty) throw SyncAccess.Error("sync.identity_invalid");
        var access = await SyncAccess.RequireAsync(resolver, command.TenantId, command.BranchId, clock, token).ConfigureAwait(false);
        SyncDevice device;
        try { device = SyncDevice.Register(access.TenantId, access.MembershipId, command.BranchId, command.DeviceLabel, clock.GetUtcNow()); }
        catch (ArgumentException) { throw SyncAccess.Error("sync.device_label_invalid"); }
        await using var scope = await store.BeginAsync(access.TenantId, null, token).ConfigureAwait(false);
        await scope.SaveAsync(device, AuditTrail.Record(access.TenantId, access.UserId, AuditAction.SyncDeviceRegistered,
            device.Id, device.RegisteredAtUtc, null, SyncAccess.AuditState(device)), token).ConfigureAwait(false);
        var current = await SyncAccess.RequireAsync(resolver, access.TenantId, device.BranchId, clock, token).ConfigureAwait(false);
        if (current.MembershipId != access.MembershipId) throw SyncAccess.Error("sync.device_denied", Errors.ErrorCategory.Forbidden);
        await scope.CompleteAsync(token).ConfigureAwait(false);
        return SyncDeviceDetails.From(device);
    }
}

public sealed class RevokeSyncDeviceHandler(ResolveAccessContextHandler resolver, ISyncDeviceStore store, TimeProvider clock)
{
    public async Task<SyncDeviceDetails> HandleAsync(RevokeSyncDeviceCommand command, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.DeviceId == Guid.Empty) throw SyncAccess.Error("sync.identity_invalid");
        await SyncAccess.RequireAsync(resolver, command.TenantId, null, clock, token, allowBranchLookup: true).ConfigureAwait(false);
        var found = await store.FindAsync(command.TenantId, command.DeviceId, token).ConfigureAwait(false)
            ?? throw SyncAccess.Error("sync.device_not_found", Errors.ErrorCategory.NotFound);
        var access = await SyncAccess.RequireAsync(resolver, command.TenantId, found.BranchId, clock, token).ConfigureAwait(false);
        SyncAccess.RequireDevice(found, access, allowRevoked: true);
        await using var scope = await store.BeginAsync(access.TenantId, command.DeviceId, token).ConfigureAwait(false);
        SyncAccess.RequireDevice(scope.Device, access, allowRevoked: true);
        var device = scope.Device!; var before = SyncAccess.AuditState(device);
        if (device.Revoke(access.MembershipId, clock.GetUtcNow()))
            await scope.SaveAsync(device, AuditTrail.Record(access.TenantId, access.UserId, AuditAction.SyncDeviceRevoked,
                device.Id, device.RevokedAtUtc!.Value, before, SyncAccess.AuditState(device)), token).ConfigureAwait(false);
        var current = await SyncAccess.RequireAsync(resolver, access.TenantId, device.BranchId, clock, token).ConfigureAwait(false);
        SyncAccess.RequireDevice(device, current, allowRevoked: true);
        await scope.CompleteAsync(token).ConfigureAwait(false);
        return SyncDeviceDetails.From(device);
    }
}
