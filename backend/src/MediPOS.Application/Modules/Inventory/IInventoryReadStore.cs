using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.Application.Modules.Inventory;

// Explicit inventory collaboration with licensing, catalog and the historical purchase cost.
public interface IInventoryReadStore
{
    Task<License?> FindLicenseAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<IReadOnlyList<InventoryLot>> FindAvailableMedicineLotsAsync(Guid tenantId, Guid branchId, Guid productId,
        DateOnly today, CancellationToken cancellationToken);
    Task<IReadOnlyList<ExpiringLotSource>> FindExpiringLotsAsync(Guid tenantId, Guid? branchId, DateOnly today,
        DateOnly through, CancellationToken cancellationToken);
}

public sealed record ExpiringLotSource(Guid TenantId, Guid InventoryLotId, Guid BranchId, Guid BusinessProductId, string ProductName,
    decimal QuantityAvailableBase, DateOnly ExpirationDate, decimal? UnitCost, decimal? ConversionToBaseSnapshot, decimal RetailPrice);
