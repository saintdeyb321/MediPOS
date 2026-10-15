using MediPOS.Application.Modules.Purchasing;
using MediPOS.Application.Modules.Purchasing.CreateSupplier;
using MediPOS.Application.Modules.Purchasing.ReplacePurchaseLines;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.Purchasing.Persistence;

internal sealed class PurchaseDirectoryReader(MediPosDbContext context) : IPurchaseDirectoryReader
{
    public async Task<IReadOnlyList<SupplierDetails>> SuppliersAsync(Guid tenantId, int offset, int limit, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        return await context.Suppliers.AsNoTracking().Where(value => value.IsActive).OrderBy(value => value.Id).Skip(offset).Take(limit)
            .Select(value => new SupplierDetails(value.Id, value.Name, value.Ruc, value.Contact, value.IsActive, value.CreatedAt)).ToListAsync(token).ConfigureAwait(false);
    }
    public async Task<PurchaseView?> PurchaseAsync(Guid tenantId, Guid branchId, Guid purchaseId, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        var purchase = await context.Purchases.AsNoTracking().SingleOrDefaultAsync(value => value.Id == purchaseId && value.BranchId == branchId, token).ConfigureAwait(false);
        if (purchase is null) return null;
        var lines = await context.PurchaseLines.AsNoTracking().Where(value => value.PurchaseId == purchaseId).OrderBy(value => value.Id).ToListAsync(token).ConfigureAwait(false);
        return new(PurchaseDetails.From(purchase), lines.Select(PurchaseLineDetails.From).ToArray());
    }
}
