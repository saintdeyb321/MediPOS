using MediPOS.Application.Modules.Reporting.Replenishment;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.Reporting.Persistence;

internal sealed class OwnerReplenishmentReader(MediPosDbContext context) : IOwnerReplenishmentReader
{
    public Task<ReplenishmentPage> ReadAsync(ReplenishmentReadRequest request, CancellationToken token) =>
        OperationalReportSnapshot.ReadAsync(context, request.Scope, async () =>
        {
            var sources = OwnerReplenishmentQueries.Candidates(context.Branches.AsNoTracking(), context.BusinessProducts.AsNoTracking(),
                context.ProductUnits.AsNoTracking(), context.InventoryLots.AsNoTracking(), context.BranchProductStockThresholds.AsNoTracking(),
                OwnerReplenishmentQueries.Consumption(context.Sales.AsNoTracking(), context.SaleLines.AsNoTracking(), request),
                OwnerReplenishmentQueries.Incoming(context.Transfers.AsNoTracking(), context.TransferLotAllocations.AsNoTracking(), request.Scope), request);
            var filtered = OwnerReplenishmentQueries.Filter(sources, request);
            var total = await filtered.LongCountAsync(token).ConfigureAwait(false);
            var rows = await OwnerReplenishmentQueries.Order(filtered, request).Skip(request.Offset).Take(request.Limit).ToArrayAsync(token).ConfigureAwait(false);
            return new ReplenishmentPage(Array.AsReadOnly(rows), total);
        }, token);
}
