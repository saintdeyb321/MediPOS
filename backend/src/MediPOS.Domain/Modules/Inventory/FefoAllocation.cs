namespace MediPOS.Domain.Modules.Inventory;

public sealed record LotAllocation(Guid InventoryLotId, decimal QuantityBase);

public static class FefoAllocation
{
    public static IReadOnlyList<LotAllocation> Plan(IEnumerable<InventoryLot> lots, decimal requested, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(lots);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requested);
        var remaining = requested;
        var plan = new List<LotAllocation>();
        foreach (var lot in lots.Where(value => value.QuantityAvailableBase > 0 && value.ExpirationDate >= today)
            .OrderBy(value => value.ExpirationDate).ThenBy(value => value.CreatedAt).ThenBy(value => value.Id))
        {
            var quantity = Math.Min(lot.QuantityAvailableBase, remaining);
            plan.Add(new(lot.Id, quantity));
            remaining = StockQuantity.Add(remaining, -quantity);
            if (remaining == 0) return plan;
        }
        throw new InsufficientStockException();
    }
}
