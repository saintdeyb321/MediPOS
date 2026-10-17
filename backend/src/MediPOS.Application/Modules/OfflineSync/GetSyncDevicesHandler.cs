using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;

namespace MediPOS.Application.Modules.OfflineSync;

public interface ISyncDeviceDirectory
{
    Task<IReadOnlyList<SyncDeviceDetails>> ReadAsync(Guid tenantId, Guid branchId, Guid membershipId, int offset, int limit, CancellationToken token);
}

// Read-only reconciliation of registration/revocation. Never exposes another member's devices.
public sealed class GetSyncDevicesHandler(ResolveAccessContextHandler resolver, ISyncDeviceDirectory devices, TimeProvider clock)
{
    public async Task<IReadOnlyList<SyncDeviceDetails>> HandleAsync(Guid tenantId, Guid branchId, int offset, int limit, CancellationToken token)
    {
        if (offset < 0 || limit is < 1 or > 100) throw SyncAccess.Error("sync.device_filter_invalid");
        var access = await SyncAccess.RequireAsync(resolver, tenantId, branchId, clock, token).ConfigureAwait(false);
        var rows = await devices.ReadAsync(tenantId, branchId, access.MembershipId, offset, limit, token).ConfigureAwait(false);
        var current = await SyncAccess.RequireAsync(resolver, tenantId, branchId, clock, token).ConfigureAwait(false);
        if (current.MembershipId != access.MembershipId) throw SyncAccess.Error("sync.device_denied", Errors.ErrorCategory.Forbidden);
        return rows;
    }
}
