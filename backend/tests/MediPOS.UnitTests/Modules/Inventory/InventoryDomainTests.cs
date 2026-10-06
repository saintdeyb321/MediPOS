using MediPOS.Application.Modules.Inventory;
using MediPOS.Domain.Modules.Inventory;

namespace MediPOS.UnitTests.Modules.Inventory;

public sealed class InventoryDomainTests
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 6, 14, 0, 0, TimeSpan.Zero);
    internal static readonly DateOnly Today = new(2026, 10, 6);

    [Fact]
    public void ReceiptInitializesProjectionAndItsPositiveLedgerDelta()
    {
        var lot = Lot(25m, Today.AddDays(10));
        var receipt = StockMovement.Receive(lot, 25m, Guid.NewGuid(), Now);
        Assert.Equal(25m, lot.QuantityAvailableBase);
        Assert.Equal(lot.QuantityAvailableBase, receipt.QuantityDeltaBase);
        Assert.Equal(lot.SourcePurchaseLineId, receipt.SourcePurchaseLineId);
        Assert.Null(receipt.Reason);
    }

    [Theory]
    [InlineData(5, 15)]
    [InlineData(-3, 7)]
    [InlineData(-10, 0)]
    public void AdjustmentPreservesSignedDeltaAndNeverSilentlyAssignsBalance(decimal delta, decimal expected)
    {
        var lot = Lot(10m, Today);
        var adjustment = StockMovement.Adjust(lot, delta, "  Conteo físico  ", Guid.NewGuid(), Now);
        Assert.Equal(10m, lot.QuantityAvailableBase); // Creating a movement does not mutate the projection.
        Assert.Equal("Conteo físico", adjustment.Reason);
        Assert.Null(adjustment.SourcePurchaseLineId);
        Assert.Equal("adjustment", StockMovementCodes.ToCode(adjustment.MovementType));
        lot.ApplyAdjustment(adjustment);
        Assert.Equal(expected, lot.QuantityAvailableBase);
    }

    [Fact]
    public void ZeroAdjustmentAndExcessDecrementCannotMutateLot()
    {
        var lot = Lot(1m, Today);
        Assert.Throws<ArgumentException>(() => StockMovement.Adjust(lot, 0m, "Conteo", Guid.NewGuid(), Now));
        Assert.Throws<InsufficientStockException>(() => StockMovement.Adjust(lot, -2m, "Conteo", Guid.NewGuid(), Now));
        Assert.Equal(1m, lot.QuantityAvailableBase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AdjustmentRequiresReason(string? reason) => Assert.ThrowsAny<ArgumentException>(() =>
        StockMovement.Adjust(Lot(1m, Today), 1m, reason!, Guid.NewGuid(), Now));

    [Fact]
    public void ForeignMovementAndLongReasonAreRejected()
    {
        var first = Lot(1m, Today);
        var second = Lot(1m, Today);
        Assert.Throws<ArgumentException>(() => first.ApplyAdjustment(StockMovement.Adjust(second, 1m, "Conteo", Guid.NewGuid(), Now)));
        Assert.Throws<ArgumentException>(() => StockMovement.Adjust(first, 1m, new string('x', 513), Guid.NewGuid(), Now));
        Assert.Equal(1m, first.QuantityAvailableBase);
    }

    [Fact]
    public void QuantityAdditionCannotLoseDecimalPrecision()
    {
        var lot = Lot(decimal.MaxValue, Today);
        Assert.ThrowsAny<ArithmeticException>(() => lot.PreviewAdjustment(0.000000000001m));
        Assert.Equal(decimal.MaxValue, lot.QuantityAvailableBase);
    }

    [Fact]
    public void FefoSplitsInExpirationOrderAndLeavesProjectionUnchanged()
    {
        var later = Lot(5m, Today.AddDays(20));
        var early = Lot(3m, Today.AddDays(10));
        var result = FefoAllocation.Plan([later, early], 6m, Today);
        Assert.Equal(new[] { new LotAllocation(early.Id, 3m), new LotAllocation(later.Id, 3m) }, result);
        Assert.Equal(3m, early.QuantityAvailableBase);
        Assert.Equal(5m, later.QuantityAvailableBase);
    }

    [Fact]
    public void FefoBreaksTiesByCreationThenUuidAndIgnoresExpiredEmptyOrUndatedLots()
    {
        var earlier = Lot(2m, Today, Now.AddHours(-1));
        var low = Lot(3m, Today);
        var high = Lot(4m, Today);
        typeof(InventoryLot).GetProperty(nameof(InventoryLot.Id))!.SetValue(low, new Guid("00000000-0000-7000-8000-000000000001"));
        typeof(InventoryLot).GetProperty(nameof(InventoryLot.Id))!.SetValue(high, new Guid("00000000-0000-7000-8000-000000000002"));
        var empty = Lot(100m, Today.AddDays(-5));
        empty.ApplyAdjustment(StockMovement.Adjust(empty, -100m, "Agotado", Guid.NewGuid(), Now));
        var expired = Lot(100m, Today.AddDays(-1));
        var undated = Lot(100m, null);
        var result = FefoAllocation.Plan([expired, high, undated, low, empty, earlier], 6m, Today);
        Assert.Equal(new[] { new LotAllocation(earlier.Id, 2m), new LotAllocation(low.Id, 3m), new LotAllocation(high.Id, 1m) }, result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void FefoRequiresPositiveRequest(decimal requested) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FefoAllocation.Plan([], requested, Today));

    [Fact]
    public void FefoRejectsShortageIncludingExpiredStock() => Assert.Throws<InsufficientStockException>(() =>
        FefoAllocation.Plan([Lot(1m, Today), Lot(10m, Today.AddDays(-1))], 2m, Today));

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(30, true)]
    [InlineData(31, false)]
    public void ExpirationWindowIncludesTodayThroughExactlyThirtyDays(int days, bool expected)
    {
        Assert.Equal(expected, InventoryCalendar.IsExpiring(Today.AddDays(days), 1m, Today));
        Assert.False(InventoryCalendar.IsExpiring(Today.AddDays(days), 0m, Today));
        Assert.False(InventoryCalendar.IsExpiring(null, 1m, Today));
    }

    [Fact]
    public void CalendarDerivesLimaDateAtUtcMidnightBoundary()
    {
        Assert.Equal(new DateOnly(2026, 10, 6), InventoryCalendar.Today(new Clock(new(2026, 10, 7, 4, 59, 0, TimeSpan.Zero))));
        Assert.Equal(new DateOnly(2026, 10, 7), InventoryCalendar.Today(new Clock(new(2026, 10, 7, 5, 0, 0, TimeSpan.Zero))));
    }

    [Theory]
    [InlineData("1", "1", "3", "0.3333")]
    [InlineData("6", "1", "3", "2")]
    [InlineData("1", "0.00005", "1", "0")]
    [InlineData("1", "0.00015", "1", "0.0002")]
    [InlineData("2.5", "12.125", "10", "3.0312")]
    public void ValuationUsesSnapshotsWithOnlyFinalFourPlaceToEvenRounding(string available, string cost, string factor, string expected)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        Assert.Equal(decimal.Parse(expected, culture), InventoryValuation.CostValue(decimal.Parse(available, culture),
            decimal.Parse(cost, culture), decimal.Parse(factor, culture)));
    }

    [Fact]
    public void ValuationHandlesLargeIntermediateProductsAndRepresentsUnknownCost()
    {
        Assert.Equal(decimal.MaxValue, InventoryValuation.CostValue(4m, decimal.MaxValue, 4m));
        Assert.Equal(decimal.MaxValue, InventoryValuation.PotentialSaleValue(decimal.MaxValue, 1m));
        Assert.Null(InventoryValuation.CostValue(10m, null, null));
        Assert.Equal(24.975m, InventoryValuation.PotentialSaleValue(2.5m, 9.99m));
        Assert.Throws<OverflowException>(() => InventoryValuation.PotentialSaleValue(decimal.MaxValue, 2m));
    }

    internal static InventoryLot Lot(decimal quantity, DateOnly? expiration, DateTimeOffset? created = null) =>
        InventoryLot.Receive(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), quantity, "L", expiration, created ?? Now);

    internal sealed class Clock(DateTimeOffset at) : TimeProvider { public override DateTimeOffset GetUtcNow() => at; }
}
