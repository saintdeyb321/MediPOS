using MediPOS.Application.Modules.Transfers;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.Transfers.Persistence;

internal sealed class TransferDirectoryReader(MediPosDbContext context) : ITransferDirectoryReader
{
    public async Task<IReadOnlyList<TransferSummary>> ReadAsync(GetTransfersQuery query, CancellationToken token)
    {
        context.SelectTenant(query.TenantId);
        return await (from transfer in context.Transfers.AsNoTracking()
                      join source in context.Branches.AsNoTracking() on transfer.SourceBranchId equals source.Id
                      join destination in context.Branches.AsNoTracking() on transfer.DestinationBranchId equals destination.Id
                      where (!query.BranchId.HasValue || transfer.SourceBranchId == query.BranchId || transfer.DestinationBranchId == query.BranchId) && (!query.Status.HasValue || transfer.Status == query.Status)
                      orderby transfer.RequestedAt descending, transfer.Id descending
                      select new TransferSummary(transfer.Id, source.Id, source.Name, destination.Id, destination.Name, transfer.Status, transfer.RequestedAt, transfer.UpdatedAt))
            .Skip(query.Offset).Take(query.Limit).ToListAsync(token).ConfigureAwait(false);
    }
}
