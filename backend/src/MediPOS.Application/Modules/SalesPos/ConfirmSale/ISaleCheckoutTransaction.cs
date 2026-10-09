using MediPOS.Application.Modules.Commissions;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Commissions;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.SalesPos;

namespace MediPOS.Application.Modules.SalesPos.ConfirmSale;

// Infrastructure owns one transaction and the CashSession -> Sale -> ordered InventoryLots locks.
// All identifiers except the requested sale/branch come from authenticated access and the server draft.
public interface ISaleCheckoutTransaction
{
    Task<ISaleCheckoutScope> BeginAsync(Guid tenantId, Guid branchId, Guid membershipId,
        Guid cashSessionId, Guid saleId, CancellationToken cancellationToken);
}

public interface ISaleCheckoutScope : IAsyncDisposable
{
    CashSession CashSession { get; }
    Sale Sale { get; }
    uint Version { get; }
    Task<CommissionConfigurationSnapshot> ReadCommissionConfigurationAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<InventoryLot>> LockLotsAsync(Guid productId, ProductType productType, decimal requested,
        DateOnly today, CancellationToken cancellationToken);
    Task<uint> CompleteAsync(IReadOnlyList<SalePayment> payments, IReadOnlyList<StockMovement> movements,
        IReadOnlyList<CommissionEntry> commissions, AuditLog audit, CancellationToken cancellationToken);
}
