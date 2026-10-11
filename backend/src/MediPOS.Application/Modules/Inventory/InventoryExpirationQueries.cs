using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Inventory;

namespace MediPOS.Application.Modules.Inventory;

public sealed record InventoryExpirationLot
{
    public Guid TenantId { get; init; }
    public Guid InventoryLotId { get; init; }
    public Guid BranchId { get; init; }
    public Guid BusinessProductId { get; init; }
    public Guid SourcePurchaseLineId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string? BatchNumber { get; init; }
    public DateOnly ExpirationDate { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public decimal QuantityAvailableBase { get; init; }
    public decimal RetailPrice { get; init; }
}

// Shared SQL selection for inventory reads, operational reports and expiration notifications.
public static class InventoryExpirationQueries
{
    public static IQueryable<InventoryExpirationLot> InRange(IQueryable<InventoryLot> lots, IQueryable<BusinessProduct> products,
        Guid tenantId, Guid? branchId, DateOnly fromDate, DateOnly through) =>
        from lot in lots
        where lot.TenantId == tenantId && (!branchId.HasValue || lot.BranchId == branchId) &&
            lot.QuantityAvailableBase > 0 && lot.ExpirationDate >= fromDate && lot.ExpirationDate <= through
        join product in products.Where(product => product.TenantId == tenantId && product.ProductType == ProductType.Medicine)
            on new { lot.TenantId, Id = lot.BusinessProductId } equals new { product.TenantId, product.Id }
        select new InventoryExpirationLot
        {
            TenantId = lot.TenantId,
            InventoryLotId = lot.Id,
            BranchId = lot.BranchId,
            BusinessProductId = product.Id,
            SourcePurchaseLineId = lot.SourcePurchaseLineId,
            ProductName = product.Name,
            BatchNumber = lot.BatchNumber,
            ExpirationDate = lot.ExpirationDate!.Value,
            QuantityAvailableBase = lot.QuantityAvailableBase,
            CreatedAt = lot.CreatedAt,
            RetailPrice = product.RetailPrice,
        };
}
