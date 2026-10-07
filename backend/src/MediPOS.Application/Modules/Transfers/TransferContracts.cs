using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.Transfers;

namespace MediPOS.Application.Modules.Transfers;

public sealed record TransferLineInput(Guid BusinessProductId, Guid ProductUnitId, decimal Quantity);
public sealed record TransferRequestData(bool BranchesValid, IReadOnlyList<BusinessProduct> Products, IReadOnlyList<ProductUnit> Units,
    string SourceBranchName, string DestinationBranchName);
public sealed record TransferSnapshot(Transfer Transfer, IReadOnlyList<TransferEvent> Events, IReadOnlyList<TransferLotAllocation> Allocations,
    string SourceBranchName, string DestinationBranchName);
public sealed record TransferLineDetails(Guid Id, Guid BusinessProductId, Guid ProductUnitIdSnapshot, decimal RequestedQuantity,
    decimal RequestedBaseQuantity, string UnitNameSnapshot, decimal ConversionToBaseSnapshot);
public sealed record TransferEventDetails(Guid Id, string EventType, Guid ActorId, DateTimeOffset OccurredAt, string? Reason);
public sealed record TransferAllocationDetails(Guid Id, Guid TransferLineId, Guid SourceInventoryLotId, Guid SourcePurchaseLineId,
    Guid BusinessProductId, string? BatchNumber, DateOnly? ExpirationDate, decimal DispatchedQuantityBase, decimal? ReceivedQuantityBase, decimal? DifferenceBase);
public sealed record TransferDetails(Guid Id, Guid TenantId, Guid SourceBranchId, string SourceBranchName, Guid DestinationBranchId,
    string DestinationBranchName, string Status, DateTimeOffset RequestedAt, DateTimeOffset UpdatedAt,
    IReadOnlyList<TransferLineDetails> Lines, IReadOnlyList<TransferEventDetails> Events, IReadOnlyList<TransferAllocationDetails> Allocations)
{
    public static TransferDetails From(TransferSnapshot snapshot)
    {
        var t = snapshot.Transfer;
        TransferHistory.Validate(t, snapshot.Events, snapshot.Allocations);
        return new(t.Id, t.TenantId, t.SourceBranchId, snapshot.SourceBranchName, t.DestinationBranchId, snapshot.DestinationBranchName,
            TransferStatusCodes.ToCode(t.Status), t.RequestedAt, t.UpdatedAt,
            Array.AsReadOnly(t.Lines.OrderBy(l => l.Id).Select(l => new TransferLineDetails(l.Id, l.BusinessProductId, l.ProductUnitIdSnapshot,
                l.RequestedQuantity, l.RequestedBaseQuantity, l.UnitNameSnapshot, l.ConversionToBaseSnapshot)).ToArray()),
            Array.AsReadOnly(snapshot.Events.OrderBy(e => TransferEventCodes.Order(e.EventType)).Select(e => new TransferEventDetails(e.Id,
                TransferEventCodes.ToCode(e.EventType), e.ActorId, e.OccurredAt, e.Reason)).ToArray()),
            Array.AsReadOnly(snapshot.Allocations.OrderBy(a => a.Id).Select(a => new TransferAllocationDetails(a.Id, a.TransferLineId, a.SourceInventoryLotId,
                a.SourcePurchaseLineId, a.BusinessProductId, a.BatchNumberSnapshot, a.ExpirationDateSnapshot,
                a.DispatchedQuantityBase, a.ReceivedQuantityBase, a.DifferenceBase)).ToArray()));
    }
}
public interface ITransferRequestStore
{
    Task<TransferRequestData> LoadAsync(Guid tenantId, Guid sourceBranchId, Guid destinationBranchId,
        IReadOnlyList<Guid> productIds, IReadOnlyList<Guid> unitIds, CancellationToken cancellationToken);
    Task CreateAsync(Transfer transfer, TransferEvent requested, AuditLog audit, CancellationToken cancellationToken);
}
public interface ITransferReader
{
    Task<TransferSnapshot?> FindAsync(Guid tenantId, Guid transferId, CancellationToken cancellationToken);
}
public interface ITransferTransaction
{
    Task<ITransferScope> BeginAsync(Guid tenantId, Guid transferId, CancellationToken cancellationToken);
}
public interface ITransferScope : IAsyncDisposable
{
    TransferSnapshot Snapshot { get; }
    Task<IReadOnlyList<InventoryLot>> LockSourceLotsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken);
    Task CompleteAsync(IReadOnlyList<TransferLotAllocation> newAllocations, IReadOnlyList<InventoryLot> newLots,
        IReadOnlyList<StockMovement> movements, TransferEvent transition, AuditLog audit, CancellationToken cancellationToken);
}
