using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.SalesPos;

namespace MediPOS.UnitTests.Modules.SalesPos;

public sealed class SaleConfirmationDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 6);
    private static readonly decimal[] FefoQuantities = [1m, 2m, 1m];

    [Theory]
    [InlineData(PaymentMethod.Cash, "cash")]
    [InlineData(PaymentMethod.Yape, "yape")]
    [InlineData(PaymentMethod.Plin, "plin")]
    [InlineData(PaymentMethod.Card, "card")]
    [InlineData(PaymentMethod.Transfer, "transfer")]
    public void AllMethodsConfirmWithoutOperationNumberAndPreserveUuidUtcAndStableCodes(PaymentMethod method, string code)
    {
        var (sale, _, _) = Cart();
        var payment = SalePayment.Create(sale, method, sale.TotalAmount);
        Assert.Equal(7, payment.Id.Version);
        Assert.Equal(code, PaymentMethodCodes.ToCode(method));
        Assert.Equal(method, PaymentMethodCodes.FromCode(code));
        sale.Confirm([payment], Now.ToOffset(TimeSpan.FromHours(-5)));
        Assert.Equal(SaleStatus.Confirmed, sale.Status);
        Assert.Equal(Now, sale.ConfirmedAt);
        Assert.Equal(TimeSpan.Zero, sale.ConfirmedAt!.Value.Offset);
        Assert.Throws<InvalidOperationException>(() => sale.Confirm([payment], Now));
        Assert.Throws<InvalidOperationException>(() => sale.ReplaceLines([], Now));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.00001")]
    [InlineData("100000000000000")]
    public void PaymentsCannotBeNonpositiveOutOfRangeOrRounded(string amount)
    {
        var (sale, _, _) = Cart();
        Assert.Throws<ArgumentOutOfRangeException>(() => SalePayment.Create(sale, PaymentMethod.Cash,
            decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal(SaleStatus.Draft, sale.Status);
    }

    [Theory]
    [InlineData("3.9999")]
    [InlineData("4.0001")]
    public void PaymentTotalHasNoTolerance(string amount)
    {
        var (sale, _, _) = Cart();
        var payment = SalePayment.Create(sale, PaymentMethod.Cash, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Throws<ArgumentException>(() => sale.Confirm([payment], Now));
        Assert.Null(sale.ConfirmedAt);
        Assert.Equal(SaleStatus.Draft, sale.Status);
    }

    [Fact]
    public void MixedPaymentsSumExactlyAndDuplicateMethodsOrForeignPaymentsAreRejected()
    {
        var (sale, _, _) = Cart();
        SalePayment[] payments = [SalePayment.Create(sale, PaymentMethod.Cash, 1.3333m), SalePayment.Create(sale, PaymentMethod.Yape, 2.6667m)];
        sale.ValidatePayments(payments);
        Assert.Throws<ArgumentException>(() => sale.Confirm([SalePayment.Create(sale, PaymentMethod.Cash, 2m), SalePayment.Create(sale, PaymentMethod.Cash, 2m)], Now));
        var (foreign, _, _) = Cart();
        Assert.Throws<ArgumentException>(() => sale.Confirm([SalePayment.Create(foreign, PaymentMethod.Cash, 4m)], Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => SalePayment.Create(sale, (PaymentMethod)99, 4m));
        sale.Confirm(payments, Now);
        Assert.Equal(4m, sale.TotalAmount);
    }

    [Fact]
    public void EmptyCartAndZeroTotalCannotConfirmUnderPositivePaymentRule()
    {
        var empty = SaleDomainTests.Draft();
        Assert.Throws<ArgumentException>(() => empty.Confirm([SalePayment.Create(empty, PaymentMethod.Cash, 1m)], Now));
        var (sale, product, _) = Cart();
        product.UpdatePrices(0m, null);
        sale.ReplaceLines([SaleLine.Create(sale, product, SaleDomainTests.Unit(product, 1m), 1m, PriceKind.Retail)], Now);
        Assert.Throws<ArgumentException>(() => sale.Confirm([SalePayment.Create(sale, PaymentMethod.Cash, 1m)], Now));
        Assert.Throws<ArgumentException>(() => sale.Confirm([], Now));
        Assert.Equal(SaleStatus.Draft, sale.Status);
    }

    [Theory]
    [InlineData("header")]
    [InlineData("line_total")]
    [InlineData("base_quantity")]
    [InlineData("conversion")]
    [InlineData("price")]
    public void CorruptedSnapshotsAndTotalsAreRejectedWithoutRepair(string failure)
    {
        var (sale, _, line) = Cart();
        var payment = SalePayment.Create(sale, PaymentMethod.Cash, 4m);
        if (failure == "header") typeof(Sale).GetProperty(nameof(Sale.TotalAmount))!.SetValue(sale, 5m);
        else
        {
            var property = failure switch
            {
                "line_total" => nameof(SaleLine.LineTotal),
                "base_quantity" => nameof(SaleLine.BaseQuantity),
                "conversion" => nameof(SaleLine.ConversionToBaseSnapshot),
                _ => nameof(SaleLine.UnitPriceSnapshot),
            };
            typeof(SaleLine).GetProperty(property)!.SetValue(line, 3m);
        }
        Assert.ThrowsAny<ArgumentException>(() => sale.Confirm([payment], Now));
        Assert.Null(sale.ConfirmedAt);
        Assert.Equal(SaleStatus.Draft, sale.Status);
    }

    [Fact]
    public void ConfirmationUsesAgreedSnapshotsEvenIfPriceAndPresentationChangeLater()
    {
        var (sale, product, _) = Cart();
        product.UpdatePrices(999m, 888m);
        var replacement = SaleDomainTests.Unit(product, 100m);
        Assert.DoesNotContain(sale.Lines, line => line.ProductUnitIdSnapshot == replacement.Id);
        sale.Confirm([SalePayment.Create(sale, PaymentMethod.Plin, 4m)], Now);
        Assert.Equal(4m, sale.TotalAmount);
        Assert.Equal(2m, sale.Lines[0].UnitPriceSnapshot);
    }

    [Fact]
    public void MedicinePlanIgnoresExpiredIncludesTodayAndSplitsByFefoCreatedThenId()
    {
        var (sale, product, _) = Cart(quantity: 4m);
        var expired = Lot(sale, product, 100m, Today.AddDays(-1), Now.AddDays(-10));
        var later = Lot(sale, product, 10m, Today.AddDays(2), Now.AddDays(-20));
        var newer = Lot(sale, product, 2m, Today, Now.AddDays(-1));
        var older = Lot(sale, product, 1m, Today, Now.AddDays(-2));
        var plan = SaleStockAllocation.Plan(sale, product.Id, ProductType.Medicine, [expired, later, newer, older], Today);
        Assert.Equal(new[] { older.Id, newer.Id, later.Id }, plan.Select(value => value.InventoryLotId));
        Assert.Equal(FefoQuantities, plan.Select(value => value.QuantityBase));
        Assert.DoesNotContain(plan, value => value.InventoryLotId == expired.Id);
        Assert.Equal(100m, expired.QuantityAvailableBase); // Planning itself changes no balances.
    }

    [Fact]
    public void RetailPlanUsesCreatedAtIdAndConsumesExpiredOrUndatedLots()
    {
        var (sale, product, _) = Cart(quantity: 3m);
        var first = Lot(sale, product, 1m, Today.AddDays(-20), Now.AddDays(-2));
        var second = Lot(sale, product, 2m, null, Now.AddDays(-1));
        var plan = SaleStockAllocation.Plan(sale, product.Id, ProductType.Retail, [second, first], Today);
        Assert.Equal(new[] { first.Id, second.Id }, plan.Select(value => value.InventoryLotId));
        Assert.Equal(3m, plan.Sum(value => value.QuantityBase));
    }

    [Fact]
    public void MultipleLinesOfProductShareResidualAvailabilityAndRemainTraceable()
    {
        var (sale, product, first) = Cart(quantity: 2m);
        product.UpdatePrices(2m, 1m);
        var second = SaleLine.Create(sale, product, SaleDomainTests.Unit(product, 2m), 1m, PriceKind.Wholesale);
        sale.ReplaceLines([first, second], Now);
        var lot = Lot(sale, product, 4m, Today, Now);
        var plan = SaleStockAllocation.Plan(sale, product.Id, ProductType.Medicine, [lot], Today);
        Assert.Equal(4m, SaleStockAllocation.RequiredQuantity(sale.Lines));
        Assert.Equal(4m, plan.Sum(value => value.QuantityBase));
        Assert.Equal(2, plan.Count);
        Assert.Contains(plan, value => value.SaleLineId == first.Id);
        Assert.Contains(plan, value => value.SaleLineId == second.Id);
        Assert.Throws<InsufficientStockException>(() => SaleStockAllocation.Plan(sale, product.Id, ProductType.Medicine,
            [Lot(sale, product, 3m, Today, Now)], Today));
    }

    [Fact]
    public void SaleMovementIsNegativeAndCannotCrossLineLotOwnershipOrOverconsume()
    {
        var (sale, product, line) = Cart(quantity: 2m);
        var lot = Lot(sale, product, 1m, Today, Now);
        var actor = Guid.NewGuid();
        var movement = StockMovement.Sell(lot, sale, line, 1m, actor, Now);
        Assert.Equal(StockMovementType.Sale, movement.MovementType);
        Assert.Equal("sale", StockMovementCodes.ToCode(movement.MovementType));
        Assert.Equal(-1m, movement.QuantityDeltaBase);
        Assert.Equal(line.Id, movement.SourceSaleLineId);
        Assert.Null(movement.SourcePurchaseLineId);
        Assert.Null(movement.Reason);
        lot.ApplySale(movement);
        Assert.Equal(0m, lot.QuantityAvailableBase);
        Assert.Throws<InsufficientStockException>(() => StockMovement.Sell(lot, sale, line, 1m, actor, Now));
        Assert.Throws<ArgumentException>(() => StockMovement.Sell(lot, sale, line, 0m, actor, Now));
        var other = InventoryLot.Receive(sale.TenantId, Guid.NewGuid(), product.Id, Guid.NewGuid(), 2m, null, Today, Now);
        Assert.Throws<ArgumentException>(() => StockMovement.Sell(other, sale, line, 1m, actor, Now));
    }

    internal static (Sale Sale, BusinessProduct Product, SaleLine Line) Cart(decimal quantity = 2m)
    {
        var sale = SaleDomainTests.Draft();
        var product = SaleDomainTests.Product(sale.TenantId, 2m, 1m);
        var line = SaleLine.Create(sale, product, SaleDomainTests.Unit(product, 1m), quantity, PriceKind.Retail);
        sale.ReplaceLines([line], Now);
        return (sale, product, line);
    }
    internal static InventoryLot Lot(Sale sale, BusinessProduct product, decimal quantity, DateOnly? expiration, DateTimeOffset created) =>
        InventoryLot.Receive(sale.TenantId, sale.BranchId, product.Id, Guid.NewGuid(), quantity, null, expiration, created);
}
