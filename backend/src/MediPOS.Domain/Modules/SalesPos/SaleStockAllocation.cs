using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Inventory;

namespace MediPOS.Domain.Modules.SalesPos;

public sealed record SaleLotAllocation(Guid SaleLineId, Guid InventoryLotId, Guid BusinessProductId, decimal QuantityBase);

public static class SaleStockAllocation
{
    public static decimal RequiredQuantity(IEnumerable<SaleLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines.Aggregate(0m, (total, line) => StockQuantity.Add(total, line.BaseQuantity));
    }

    // Pure planning over the locked rows. Residual stock is shared by all presentations/price kinds of a product.
    public static IReadOnlyList<SaleLotAllocation> Plan(Sale sale, Guid productId, ProductType productType,
        IReadOnlyList<InventoryLot> lots, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(sale);
        ArgumentNullException.ThrowIfNull(lots);
        sale.EnsureDraft();
        if (!Enum.IsDefined(productType) || lots.Any(lot => lot.TenantId != sale.TenantId || lot.BranchId != sale.BranchId || lot.BusinessProductId != productId) ||
            lots.Select(lot => lot.Id).Distinct().Count() != lots.Count)
            throw new ArgumentException("Allocation requires distinct lots owned by the sale branch/product.");
        var valid = lots.Where(lot => lot.QuantityAvailableBase > 0 && (productType == ProductType.Retail || lot.ExpirationDate >= today));
        var ordered = (productType == ProductType.Medicine
            ? valid.OrderBy(lot => lot.ExpirationDate).ThenBy(lot => lot.CreatedAt).ThenBy(lot => lot.Id)
            : valid.OrderBy(lot => lot.CreatedAt).ThenBy(lot => lot.Id)).ToArray();
        var residual = ordered.ToDictionary(lot => lot.Id, lot => lot.QuantityAvailableBase);
        var result = new List<SaleLotAllocation>();
        foreach (var line in sale.Lines.Where(line => line.BusinessProductId == productId).OrderBy(line => line.Id))
        {
            var remaining = line.BaseQuantity;
            foreach (var lot in ordered)
            {
                var take = Math.Min(residual[lot.Id], remaining);
                if (take <= 0) continue;
                result.Add(new(line.Id, lot.Id, productId, take));
                residual[lot.Id] = StockQuantity.Add(residual[lot.Id], -take);
                remaining = StockQuantity.Add(remaining, -take);
                if (remaining == 0) break;
            }
            if (remaining != 0) throw new InsufficientStockException();
        }
        return result;
    }
}
