using MediPOS.Application.Modules.OfflineSync;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.OfflineSync.Persistence;

internal sealed class SyncDeviceDirectory(MediPosDbContext context) : ISyncDeviceDirectory
{
    public async Task<IReadOnlyList<SyncDeviceDetails>> ReadAsync(Guid tenantId, Guid branchId, Guid membershipId, int offset, int limit, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        return await context.SyncDevices.AsNoTracking().Where(row => row.TenantId == tenantId && row.BranchId == branchId && row.RegisteredByMembershipId == membershipId)
            .OrderBy(row => row.Id).Skip(offset).Take(limit)
            .Select(row => new SyncDeviceDetails(row.Id, row.BranchId, row.DeviceLabel, row.Status, row.RegisteredAtUtc, row.RevokedAtUtc)).ToListAsync(token).ConfigureAwait(false);
    }
}
