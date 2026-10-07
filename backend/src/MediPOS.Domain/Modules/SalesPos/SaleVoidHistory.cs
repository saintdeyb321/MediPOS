using MediPOS.Domain.Modules.Inventory;

namespace MediPOS.Domain.Modules.SalesPos;

public static class SaleVoidHistory
{
    public static void ValidateReversals(Sale sale, IReadOnlyList<SalePayment> originals, IReadOnlyList<StockMovement> consumptions,
        IReadOnlyList<SalePaymentReversal> payments, IReadOnlyList<StockMovement> movements)
    {
        Validate(sale, originals, consumptions);
        if (sale.Status != SaleStatus.Voided || payments.Count != originals.Count || movements.Count != consumptions.Count ||
            payments.Select(payment => payment.SalePaymentId).Distinct().Count() != payments.Count ||
            movements.Select(movement => movement.ReversesStockMovementId).Distinct().Count() != movements.Count)
            throw new ArgumentException("Void requires exactly one counterpart for every original effect.");
        var originalPayments = originals.ToDictionary(payment => payment.Id);
        var originalMovements = consumptions.ToDictionary(movement => movement.Id);
        foreach (var payment in payments)
        {
            if (!originalPayments.TryGetValue(payment.SalePaymentId, out var original) || payment.ActorId != sale.VoidedByActorId || payment.OccurredAt != sale.VoidedAt)
                throw new ArgumentException("Payment reversal must match the void actor/time and original payment.");
            payment.ValidateAgainst(sale, original);
        }
        foreach (var movement in movements)
        {
            if (!movement.ReversesStockMovementId.HasValue || !originalMovements.TryGetValue(movement.ReversesStockMovementId.Value, out var original) ||
                movement.ActorId != sale.VoidedByActorId || movement.OccurredAt != sale.VoidedAt)
                throw new ArgumentException("Stock reversal must match the void actor/time and original movement.");
            movement.ValidateSaleReversal(original);
        }
    }

    public static void Validate(Sale sale, IReadOnlyList<SalePayment> payments, IReadOnlyList<StockMovement> movements)
    {
        ArgumentNullException.ThrowIfNull(sale);
        ArgumentNullException.ThrowIfNull(payments);
        ArgumentNullException.ThrowIfNull(movements);
        if (sale.Status is not (SaleStatus.Confirmed or SaleStatus.Voided)) throw new ArgumentException("Confirmed history is required.");
        sale.ValidateForCheckout();
        sale.ValidatePayments(payments);
        var lines = sale.Lines.ToDictionary(line => line.Id);
        if (movements.Count == 0 || movements.Select(movement => movement.Id).Distinct().Count() != movements.Count ||
            movements.Any(movement => movement.Id == Guid.Empty || movement.MovementType != StockMovementType.Sale || movement.TenantId != sale.TenantId ||
                movement.BranchId != sale.BranchId || movement.QuantityDeltaBase >= 0 || movement.SourcePurchaseLineId.HasValue ||
                movement.ReversesStockMovementId.HasValue || movement.Reason is not null || movement.InventoryLotId == Guid.Empty ||
                movement.ActorId == Guid.Empty || movement.OccurredAt != sale.ConfirmedAt || movement.OccurredAt.Offset != TimeSpan.Zero ||
                !movement.SourceSaleLineId.HasValue || !lines.TryGetValue(movement.SourceSaleLineId.Value, out var line) || line.BusinessProductId != movement.BusinessProductId))
            throw new ArgumentException("Original sale movements must preserve their confirmation ownership and time.");
        foreach (var line in sale.Lines)
            if (movements.Where(movement => movement.SourceSaleLineId == line.Id).Aggregate(0m, (total, movement) => StockQuantity.Add(total, -movement.QuantityDeltaBase)) != line.BaseQuantity)
                throw new ArgumentException("Every original line must have its exact full stock consumption.");
    }
}
