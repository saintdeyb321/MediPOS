using MediPOS.Application.Modules.Cash;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.Cash.Persistence;

internal sealed class CashTransferReader(MediPosDbContext context) : ICashTransferReader
{
    public Task<CashTransfer?> FindAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        return context.CashTransfers.AsNoTracking().SingleOrDefaultAsync(t => t.TenantId == tenantId && t.Id == id, cancellationToken);
    }
    public async Task<IReadOnlyList<CashTransfer>> PendingAsync(Guid tenantId, Guid? destinationBranchId, int offset, int limit, CancellationToken cancellationToken)
    {
        if (offset is < 0 or > 10000 || limit is < 1 or > 101) throw new ArgumentOutOfRangeException(nameof(limit));
        context.SelectTenant(tenantId);
        return await context.CashTransfers.AsNoTracking().Where(t => t.TenantId == tenantId && t.Status == CashTransferStatus.InTransit &&
                (!destinationBranchId.HasValue || t.DestinationBranchId == destinationBranchId))
            .OrderBy(t => t.DispatchedAt).ThenBy(t => t.Id).Skip(offset).Take(limit).ToArrayAsync(cancellationToken).ConfigureAwait(false);
    }
}
