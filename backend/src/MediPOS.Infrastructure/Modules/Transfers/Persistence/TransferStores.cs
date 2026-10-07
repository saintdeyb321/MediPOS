using System.Data;
using MediPOS.Application.Modules.Transfers;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Transfers;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.Transfers.Persistence;

internal sealed class TransferRequestStore(MediPosDbContext context) : ITransferRequestStore
{
    public async Task<TransferRequestData> LoadAsync(Guid tenantId, Guid sourceBranchId, Guid destinationBranchId,
        IReadOnlyList<Guid> productIds, IReadOnlyList<Guid> unitIds, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        var branches = await context.Branches.AsNoTracking().Where(b => b.TenantId == tenantId && (b.Id == sourceBranchId || b.Id == destinationBranchId))
            .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken).ConfigureAwait(false);
        var products = await context.BusinessProducts.AsNoTracking().Where(p => p.TenantId == tenantId && productIds.Contains(p.Id))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var units = await context.ProductUnits.AsNoTracking().Where(u => u.TenantId == tenantId && unitIds.Contains(u.Id) && productIds.Contains(u.BusinessProductId))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return new(branches.Count == 2, products, units, branches.GetValueOrDefault(sourceBranchId, ""), branches.GetValueOrDefault(destinationBranchId, ""));
    }
    public async Task CreateAsync(Transfer transfer, TransferEvent requested, AuditLog audit, CancellationToken cancellationToken)
    {
        context.SelectTenant(transfer.TenantId);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            context.Transfers.Add(transfer); context.TransferEvents.Add(requested);
            context.AddAudit(audit, transfer.TenantId, AuditAction.TransferRequested, transfer.Id);
            await context.SaveAuditedChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch { context.ChangeTracker.Clear(); throw; }
    }
}
internal sealed class TransferReader(MediPosDbContext context) : ITransferReader
{
    public async Task<TransferSnapshot?> FindAsync(Guid tenantId, Guid transferId, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        // Fixed query count and bounded children; read-only snapshot prevents mixing concurrent transitions.
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken).ConfigureAwait(false);
        var transfer = await context.Transfers.AsNoTracking().Include(t => t.Lines.OrderBy(l => l.Id).Take(Transfer.MaximumLines + 1)).AsSingleQuery()
            .SingleOrDefaultAsync(t => t.TenantId == tenantId && t.Id == transferId, cancellationToken).ConfigureAwait(false);
        var snapshot = transfer is null ? null : await LoadChildrenAsync(context, transfer, trackedAllocations: false, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return snapshot;
    }
    internal static async Task<TransferSnapshot> LoadChildrenAsync(MediPosDbContext context, Transfer transfer, bool trackedAllocations, CancellationToken token)
    {
        var events = await context.TransferEvents.AsNoTracking().Where(e => e.TenantId == transfer.TenantId && e.TransferId == transfer.Id)
            .OrderBy(e => e.Id).Take(6).ToArrayAsync(token).ConfigureAwait(false);
        var query = context.TransferLotAllocations.Where(a => a.TenantId == transfer.TenantId && a.TransferId == transfer.Id);
        var allocations = await (trackedAllocations ? query : query.AsNoTracking()).OrderBy(a => a.Id).Take(Transfer.MaximumAllocations + 1).ToArrayAsync(token).ConfigureAwait(false);
        var names = await context.Branches.AsNoTracking().Where(b => b.TenantId == transfer.TenantId && (b.Id == transfer.SourceBranchId || b.Id == transfer.DestinationBranchId))
            .ToDictionaryAsync(b => b.Id, b => b.Name, token).ConfigureAwait(false);
        return new(transfer, events, allocations, names.GetValueOrDefault(transfer.SourceBranchId, ""), names.GetValueOrDefault(transfer.DestinationBranchId, ""));
    }
}
