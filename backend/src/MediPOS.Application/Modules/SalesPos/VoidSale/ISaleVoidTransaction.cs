using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.SalesPos;

namespace MediPOS.Application.Modules.SalesPos.VoidSale;

public sealed record SaleVoidSnapshot(Sale Sale, uint Version);
public sealed record SaleVoidEffects(IReadOnlyList<SalePayment> Payments, IReadOnlyList<StockMovement> Movements);

public interface ISaleVoidTransaction
{
    Task<SaleVoidSnapshot?> FindAsync(Guid tenantId, Guid branchId, Guid saleId, CancellationToken cancellationToken);
    Task<ISaleVoidScope> BeginAsync(Guid tenantId, Guid branchId, Guid cashSessionId, Guid saleId, CancellationToken cancellationToken);
}

// One concrete transaction; original effects and affected lot IDs are always read by Infrastructure.
public interface ISaleVoidScope : IAsyncDisposable
{
    CashSession CashSession { get; }
    Sale Sale { get; }
    uint Version { get; }
    Task<SaleVoidEffects> LoadEffectsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<InventoryLot>> LockLotsAsync(CancellationToken cancellationToken);
    Task<uint> CompleteAsync(IReadOnlyList<SalePaymentReversal> payments, IReadOnlyList<StockMovement> movements,
        AuditLog audit, CancellationToken cancellationToken);
}
