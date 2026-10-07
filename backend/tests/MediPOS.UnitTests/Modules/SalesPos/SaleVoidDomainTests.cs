using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.SalesPos;

namespace MediPOS.UnitTests.Modules.SalesPos;

public sealed class SaleVoidDomainTests
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 6, 14, 1, 0, TimeSpan.Zero);

    [Fact]
    public void ConfirmedSaleVoidsOnceWithTrimmedReasonActorUtcAndUnchangedSnapshots()
    {
        var history = History();
        var confirmedAt = history.Sale.ConfirmedAt;
        var line = Assert.Single(history.Sale.Lines);
        history.Sale.Void("  Error de cobro  ", history.Actor, Now.ToOffset(TimeSpan.FromHours(-5)));
        Assert.Equal(SaleStatus.Voided, history.Sale.Status);
        Assert.Equal("voided", SaleStatusCodes.ToCode(history.Sale.Status));
        Assert.Equal(SaleStatus.Voided, SaleStatusCodes.FromCode("voided"));
        Assert.Equal("Error de cobro", history.Sale.VoidReason);
        Assert.Equal(history.Actor, history.Sale.VoidedByActorId);
        Assert.Equal(Now, history.Sale.VoidedAt);
        Assert.Equal(TimeSpan.Zero, history.Sale.VoidedAt!.Value.Offset);
        Assert.Equal(confirmedAt, history.Sale.ConfirmedAt);
        Assert.Same(line, Assert.Single(history.Sale.Lines));
        Assert.Throws<InvalidOperationException>(() => history.Sale.Void("Otra vez", history.Actor, Now));
        Assert.Throws<InvalidOperationException>(() => history.Sale.ReplaceLines([], Now));
        Assert.Throws<InvalidOperationException>(() => history.Sale.Confirm(history.Payments, Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t\n ")]
    [InlineData(null)]
    public void BlankReasonDoesNotChangeConfirmedSale(string? reason)
    {
        var history = History();
        Assert.Throws<ArgumentException>(() => history.Sale.Void(reason!, history.Actor, Now));
        Assert.Equal(SaleStatus.Confirmed, history.Sale.Status);
        Assert.Null(history.Sale.VoidedAt);
    }

    [Fact]
    public void DraftLongReasonMissingActorAndEarlierTimestampAreRejected()
    {
        var history = History();
        Assert.Throws<InvalidOperationException>(() => SaleDomainTests.Draft().Void("Motivo", history.Actor, Now));
        Assert.Throws<ArgumentException>(() => history.Sale.Void(new string('x', 513), history.Actor, Now));
        Assert.Throws<ArgumentException>(() => history.Sale.Void("Motivo", Guid.Empty, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => history.Sale.Void("Motivo", history.Actor, Now.AddHours(-1)));
        Assert.Equal(SaleStatus.Confirmed, history.Sale.Status);
    }

    [Fact]
    public void AllFivePaymentReversalsCopyExactOriginalMethodAmountAndRemainPositive()
    {
        var history = History();
        var reversals = history.Payments.Select(payment => SalePaymentReversal.Reverse(history.Sale, payment, history.Actor, Now)).ToArray();
        foreach (var original in history.Payments)
        {
            var reversal = Assert.Single(reversals, value => value.SalePaymentId == original.Id);
            Assert.Equal(7, reversal.Id.Version);
            Assert.Equal(original.Method, reversal.Method);
            Assert.Equal(original.Amount, reversal.Amount);
            Assert.True(original.Amount > 0);
            Assert.Equal(history.Sale.Id, reversal.SaleId);
            Assert.Equal(history.Sale.TenantId, reversal.TenantId);
            Assert.Equal(Now, reversal.OccurredAt);
        }
        Assert.Throws<ArgumentException>(() => SalePaymentReversal.Reverse(history.Sale, history.Payments[0], Guid.Empty, Now));
        var foreign = SalePayment.Create(SaleDomainTests.Draft(), PaymentMethod.Cash, 1m);
        Assert.Throws<ArgumentException>(() => SalePaymentReversal.Reverse(history.Sale, foreign, history.Actor, Now));
    }

    [Fact]
    public void SplitConsumptionRestoresExactOriginalLotsEvenWhenExpiredWithOneCounterpartEach()
    {
        var history = History();
        var reversals = history.Movements.Select(original => StockMovement.ReverseSale(history.Lots.Single(lot => lot.Id == original.InventoryLotId), history.Sale, original, history.Actor, Now)).ToArray();
        foreach (var original in history.Movements)
        {
            var reversal = Assert.Single(reversals, movement => movement.ReversesStockMovementId == original.Id);
            Assert.Equal(StockMovementType.SaleReversal, reversal.MovementType);
            Assert.Equal("sale_reversal", StockMovementCodes.ToCode(reversal.MovementType));
            Assert.Equal(StockMovementType.SaleReversal, StockMovementCodes.FromCode("sale_reversal"));
            Assert.Equal(original.SourceSaleLineId, reversal.SourceSaleLineId);
            Assert.Equal(original.InventoryLotId, reversal.InventoryLotId);
            Assert.Equal(-original.QuantityDeltaBase, reversal.QuantityDeltaBase);
            Assert.Null(reversal.SourcePurchaseLineId);
            Assert.Null(reversal.Reason);
            var lot = history.Lots.Single(lot => lot.Id == original.InventoryLotId);
            lot.ApplySaleReversal(reversal, original);
            Assert.Equal(-original.QuantityDeltaBase, lot.QuantityAvailableBase);
            Assert.Equal(0m, original.QuantityDeltaBase + reversal.QuantityDeltaBase);
        }
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("line")]
    [InlineData("branch")]
    [InlineData("delta")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    public void CorruptOriginalHistoryIsRejectedBeforeCompensation(string corrupt)
    {
        var history = History();
        IReadOnlyList<StockMovement> movements = history.Movements;
        switch (corrupt)
        {
            case "amount": typeof(SalePayment).GetProperty(nameof(SalePayment.Amount))!.SetValue(history.Payments[0], 1m); break;
            case "line": typeof(StockMovement).GetProperty(nameof(StockMovement.SourceSaleLineId))!.SetValue(history.Movements[0], Guid.NewGuid()); break;
            case "branch": typeof(StockMovement).GetProperty(nameof(StockMovement.BranchId))!.SetValue(history.Movements[0], Guid.NewGuid()); break;
            case "delta": typeof(StockMovement).GetProperty(nameof(StockMovement.QuantityDeltaBase))!.SetValue(history.Movements[0], -0.5m); break;
            case "duplicate": movements = [history.Movements[0], history.Movements[0]]; break;
            case "missing": movements = [history.Movements[0]]; break;
        }
        Assert.Throws<ArgumentException>(() => SaleVoidHistory.Validate(history.Sale, history.Payments, movements));
        Assert.Equal(SaleStatus.Confirmed, history.Sale.Status);
        Assert.All(history.Lots, lot => Assert.Equal(0m, lot.QuantityAvailableBase));
    }

    [Fact]
    public void ReversalAddsItsOwnDeltaToCurrentBalanceRatherThanResettingHistoricalReceipt()
    {
        var history = History();
        var lot = history.Lots[0];
        lot.ApplyAdjustment(StockMovement.Adjust(lot, 2m, "Ingreso adicional", history.Actor, Now));
        var reversal = StockMovement.ReverseSale(lot, history.Sale, history.Movements[0], history.Actor, Now);
        lot.ApplySaleReversal(reversal, history.Movements[0]);
        Assert.Equal(2.75m, lot.QuantityAvailableBase);
    }

    [Theory]
    [InlineData("payment")]
    [InlineData("stock")]
    public void DuplicateCounterpartFailsFullVoidValidation(string duplicate)
    {
        var history = History();
        var payments = history.Payments.Select(payment => SalePaymentReversal.Reverse(history.Sale, payment, history.Actor, Now)).ToArray();
        var movements = history.Movements.Select(movement => StockMovement.ReverseSale(history.Lots.Single(lot => lot.Id == movement.InventoryLotId), history.Sale, movement, history.Actor, Now)).ToArray();
        history.Sale.Void("Motivo", history.Actor, Now);
        SaleVoidHistory.ValidateReversals(history.Sale, history.Payments, history.Movements, payments, movements);
        if (duplicate == "payment") payments[1] = payments[0];
        else movements[1] = movements[0];
        Assert.Throws<ArgumentException>(() => SaleVoidHistory.ValidateReversals(history.Sale, history.Payments, history.Movements, payments, movements));
    }

    [Fact]
    public void AReversalCannotUseAnotherLotOrAnAdjustmentOrChangeTheOppositeDelta()
    {
        var history = History();
        var original = history.Movements[0];
        Assert.Throws<ArgumentException>(() => StockMovement.ReverseSale(history.Lots[1], history.Sale, original, history.Actor, Now));
        var adjustment = StockMovement.Adjust(history.Lots[0], 1m, "Manual", history.Actor, Now);
        Assert.Throws<ArgumentException>(() => StockMovement.ReverseSale(history.Lots[0], history.Sale, adjustment, history.Actor, Now));
        var reversal = StockMovement.ReverseSale(history.Lots[0], history.Sale, original, history.Actor, Now);
        typeof(StockMovement).GetProperty(nameof(StockMovement.QuantityDeltaBase))!.SetValue(reversal, 2m);
        Assert.Throws<ArgumentException>(() => history.Lots[0].ApplySaleReversal(reversal, original));
        Assert.Equal(0m, history.Lots[0].QuantityAvailableBase);
    }

    internal static HistoryRows History()
    {
        var (sale, product, line) = SaleConfirmationDomainTests.Cart();
        var actor = Guid.NewGuid();
        var lots = new[] { SaleConfirmationDomainTests.Lot(sale, product, 0.75m, new(2025, 1, 1), Now.AddDays(-1)), SaleConfirmationDomainTests.Lot(sale, product, 1.25m, new(2026, 10, 6), Now.AddDays(-1)) };
        var movements = lots.Select(lot => StockMovement.Sell(lot, sale, line, lot.QuantityAvailableBase, actor, Now.AddMinutes(-1))).ToArray();
        foreach (var movement in movements) lots.Single(lot => lot.Id == movement.InventoryLotId).ApplySale(movement);
        var payments = new[] { SalePayment.Create(sale, PaymentMethod.Cash, 0.5m), SalePayment.Create(sale, PaymentMethod.Yape, 0.5m),
            SalePayment.Create(sale, PaymentMethod.Plin, 0.5m), SalePayment.Create(sale, PaymentMethod.Card, 0.5m), SalePayment.Create(sale, PaymentMethod.Transfer, 2m) };
        sale.Confirm(payments, Now.AddMinutes(-1));
        return new(sale, payments, movements, lots, actor);
    }
    internal sealed record HistoryRows(Sale Sale, SalePayment[] Payments, StockMovement[] Movements, InventoryLot[] Lots, Guid Actor);
}
