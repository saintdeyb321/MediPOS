using System.Globalization;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.SalesPos;

namespace MediPOS.UnitTests.Modules.SalesPos;

public sealed class SaleDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 9, 0, 0, TimeSpan.FromHours(-5));

    [Fact]
    public void EmptyDraftOwnsServerSelectedCashSellerAndUtcTimesWithNoConfirmation()
    {
        var sale = Draft();
        Assert.Equal(7, sale.Id.Version);
        Assert.Equal(SaleStatus.Draft, sale.Status);
        Assert.Equal(0m, sale.TotalAmount);
        Assert.Empty(sale.Lines);
        Assert.Equal(Now.ToUniversalTime(), sale.CreatedAt);
        Assert.Equal(sale.CreatedAt, sale.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, sale.CreatedAt.Offset);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void DraftRequiresEveryOwnershipIdentifier(int empty)
    {
        var ids = Enumerable.Range(0, 4).Select(value => value == empty ? Guid.Empty : Guid.NewGuid()).ToArray();
        Assert.Throws<ArgumentException>(() => Sale.CreateDraft(ids[0], ids[1], ids[2], ids[3], Now));
    }

    [Theory]
    [InlineData(PriceKind.Retail, "2.125", "42.5")]
    [InlineData(PriceKind.Wholesale, "1.75", "35")]
    public void PresentationUsesBaseUnitServerPriceWithExactConversionAndSnapshots(PriceKind kind, string price, string total)
    {
        var sale = Draft();
        var product = Product(sale.TenantId, 2.125m, 1.75m);
        var unit = Unit(product, 10m);
        var line = SaleLine.Create(sale, product, unit, 2m, kind);
        Assert.Equal(7, line.Id.Version);
        Assert.Equal(20m, line.BaseQuantity);
        Assert.Equal(10m, line.ConversionToBaseSnapshot);
        Assert.Equal(Parse(price), line.UnitPriceSnapshot);
        Assert.Equal(Parse(total), line.LineTotal);
        Assert.Equal(product.Name, line.ProductNameSnapshot);
        Assert.Equal(unit.Name, line.UnitNameSnapshot);
        Assert.Equal(unit.Id, line.ProductUnitIdSnapshot);
        sale.ReplaceLines([line], Now.AddSeconds(1));
        Assert.Equal(line.LineTotal, sale.TotalAmount);
        product.UpdatePrices(500m, 400m);
        product.SetStatus(false);
        Assert.Equal(Parse(price), line.UnitPriceSnapshot);
        Assert.Equal(Parse(total), sale.TotalAmount);
    }

    [Theory]
    [InlineData("0.5", "0.0001", "0")]
    [InlineData("1.5", "0.0001", "0.0002")]
    [InlineData("2.5", "0.0001", "0.0002")]
    [InlineData("0.333333333333", "3", "1")]
    [InlineData("1", "0", "0")]
    [InlineData("1", "99999999999999.9999", "99999999999999.9999")]
    [InlineData("99999999999999.9999", "1", "99999999999999.9999")]
    [InlineData("1.000000000001", "10000000000.1234", "10000000000.1334")]
    [InlineData("99999999999999.500100010001", "0.9999", "99989999999999.5001")]
    [InlineData("99999999999998.499899989999", "0.9999", "99989999999998.5001")]
    public void TotalsRoundExactProductOnlyOnceToEven(string quantity, string price, string expected)
    {
        var sale = Draft();
        var product = Product(sale.TenantId, Parse(price));
        var line = SaleLine.Create(sale, product, Unit(product, 1m), Parse(quantity), PriceKind.Retail);
        Assert.Equal(Parse(expected), line.LineTotal);
        Assert.Equal(Parse(quantity), line.BaseQuantity);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.0000000000001")]
    [InlineData("10000000000000000")]
    public void QuantityMustBePositiveAndExactlyPersistable(string quantity)
    {
        var sale = Draft();
        var product = Product(sale.TenantId);
        Assert.Throws<ArgumentOutOfRangeException>(() => SaleLine.Create(sale, product, Unit(product, 1m), Parse(quantity), PriceKind.Retail));
    }

    [Fact]
    public void ConversionCannotRoundBaseQuantityToFitPersistenceOrOverflowDecimal()
    {
        var sale = Draft();
        var product = Product(sale.TenantId);
        Assert.Throws<ArgumentOutOfRangeException>(() => SaleLine.Create(sale, product, Unit(product, 0.000000000001m), 0.000000000001m, PriceKind.Retail));
        Assert.Throws<ArgumentOutOfRangeException>(() => SaleLine.Create(sale, product, Unit(product, 10m), 1000000000000000m, PriceKind.Retail));
        Assert.Throws<OverflowException>(() => SaleLine.Create(sale, product, Unit(product, ProductUnit.MaximumConversion), SaleLine.MaximumQuantity, PriceKind.Retail));
    }

    [Theory]
    [InlineData("inactive_product")]
    [InlineData("inactive_unit")]
    [InlineData("foreign_product")]
    [InlineData("foreign_unit")]
    [InlineData("wrong_product")]
    [InlineData("no_wholesale")]
    public void InvalidCatalogSelectionCannotCreateLine(string failure)
    {
        var sale = Draft();
        var product = Product(failure == "foreign_product" ? Guid.NewGuid() : sale.TenantId);
        if (failure == "inactive_product") product.SetStatus(false);
        var other = Product(failure == "foreign_unit" ? Guid.NewGuid() : product.TenantId);
        var unit = Unit(failure is "foreign_unit" or "wrong_product" ? other : product, 1m, failure != "inactive_unit");
        Assert.Throws<ArgumentException>(() => SaleLine.Create(sale, product, unit, 1m,
            failure == "no_wholesale" ? PriceKind.Wholesale : PriceKind.Retail));
    }

    [Fact]
    public void ReplacementSumsRoundedLinesAndAllowsDifferentPresentationsOrPriceKinds()
    {
        var sale = Draft();
        var product = Product(sale.TenantId, 0.0001m, 0.0002m);
        var unit = Unit(product, 1m);
        SaleLine[] lines = [SaleLine.Create(sale, product, unit, 0.5m, PriceKind.Retail),
            SaleLine.Create(sale, product, unit, 0.5m, PriceKind.Wholesale),
            SaleLine.Create(sale, product, Unit(product, 10m), 1m, PriceKind.Retail)];
        sale.ReplaceLines(lines, Now.AddSeconds(1));
        Assert.Equal(0.0011m, sale.TotalAmount);
        Assert.Equal(lines.Sum(value => value.LineTotal), sale.TotalAmount);
        sale.ReplaceLines([], Now.AddSeconds(2));
        Assert.Equal(0m, sale.TotalAmount);
        Assert.Empty(sale.Lines);
    }

    [Fact]
    public void InvalidDuplicateForeignOrOverflowReplacementPreservesOriginalCart()
    {
        var sale = Draft();
        var product = Product(sale.TenantId, Sale.MaximumAmount);
        var unit = Unit(product, 1m);
        var first = SaleLine.Create(sale, product, unit, 1m, PriceKind.Retail);
        sale.ReplaceLines([first], Now);
        var duplicate = SaleLine.Create(sale, product, unit, 1m, PriceKind.Retail);
        Assert.Throws<ArgumentException>(() => sale.ReplaceLines([first, duplicate], Now.AddSeconds(1)));
        var another = SaleLine.Create(sale, product, Unit(product, 2m), 0.5m, PriceKind.Retail);
        Assert.Throws<ArgumentOutOfRangeException>(() => sale.ReplaceLines([first, another], Now.AddSeconds(1)));
        var foreignSale = Sale.CreateDraft(sale.TenantId, sale.BranchId, sale.SellerMembershipId, sale.CashSessionId, Now);
        var foreignLine = SaleLine.Create(foreignSale, product, unit, 1m, PriceKind.Retail);
        Assert.Throws<ArgumentException>(() => sale.ReplaceLines([foreignLine], Now.AddSeconds(1)));
        Assert.Same(first, Assert.Single(sale.Lines));
        Assert.Equal(first.LineTotal, sale.TotalAmount);
        Assert.Equal(Now.ToUniversalTime(), sale.UpdatedAt);
        Assert.Throws<ArgumentOutOfRangeException>(() => SaleLine.Create(sale, product, unit, 2m, PriceKind.Retail));
    }

    [Fact]
    public void ConfirmedCodeIsReservedAndCannotBeEditedOrProduceNewLines()
    {
        var sale = Draft();
        typeof(Sale).GetProperty(nameof(Sale.Status))!.SetValue(sale, SaleStatus.Confirmed);
        Assert.Throws<InvalidOperationException>(() => sale.ReplaceLines([], Now));
        var product = Product(sale.TenantId);
        Assert.Throws<InvalidOperationException>(() => SaleLine.Create(sale, product, Unit(product, 1m), 1m, PriceKind.Retail));
        Assert.Equal("draft", SaleStatusCodes.ToCode(SaleStatus.Draft));
        Assert.Equal(SaleStatus.Confirmed, SaleStatusCodes.FromCode("confirmed"));
        Assert.Equal("retail", PriceKindCodes.ToCode(PriceKind.Retail));
        Assert.Equal(PriceKind.Wholesale, PriceKindCodes.FromCode("wholesale"));
        Assert.Throws<InvalidOperationException>(() => SaleStatusCodes.FromCode("unknown"));
        Assert.Throws<InvalidOperationException>(() => PriceKindCodes.FromCode("unknown"));
    }

    internal static Sale Draft() => Sale.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
    internal static BusinessProduct Product(Guid tenant, decimal retail = 2m, decimal? wholesale = null) =>
        BusinessProduct.CreateLocal(tenant, Guid.NewGuid().ToString("N"), ProductType.Retail, "Producto", Guid.NewGuid(), "Marca", null, null, retail, wholesale, Now);
    internal static ProductUnit Unit(BusinessProduct product, decimal factor, bool active = true) =>
        ProductUnit.Create(product.TenantId, product.Id, product.TenantId, "Presentación", factor, factor == 1m, active);
    private static decimal Parse(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
