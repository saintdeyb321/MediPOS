using System.Globalization;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Domain.Modules.Catalog;

namespace MediPOS.UnitTests.Modules.Catalog;

public sealed class ProductUnitTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BaseUnitHasFactorOneAndAnExactConversion()
    {
        var unit = Unit(" Tableta ", 1m, true);
        Assert.Equal(7, unit.Id.Version);
        Assert.Equal("Tableta", unit.Name);
        Assert.Equal(1.23456789m, unit.ToBaseQuantity(1.23456789m));
        Assert.Equal(-2m, unit.ToBaseQuantity(-2m));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.0000000000001")]
    [InlineData("10000000000000000")]
    public void FactorMustBePositiveAndExactlyStorable(string value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Unit("Presentation", decimal.Parse(value, CultureInfo.InvariantCulture), false));

    [Fact]
    public void NonUnitBaseFactorAndMissingIdsOrForeignTenantAreRejected()
    {
        Assert.Throws<ArgumentException>(() => Unit("Base", 10m, true));
        var tenant = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => ProductUnit.Create(tenant, Guid.NewGuid(), Guid.NewGuid(), "Foreign", 1m, true));
        Assert.Throws<ArgumentException>(() => ProductUnit.Create(Guid.Empty, Guid.NewGuid(), Guid.Empty, "Empty", 1m, true));
        Assert.Throws<ArgumentException>(() => ProductUnit.Create(tenant, Guid.Empty, tenant, "Empty", 1m, true));
        Assert.Throws<ArgumentException>(() => Unit(" ", 1m, true));
    }

    [Fact]
    public void DecimalFactorAndFractionalQuantityConvertExactly()
    {
        Assert.Equal(20m, Unit("Blíster", 10m, false).ToBaseQuantity(2m));
        Assert.Equal(0.0375m, Unit("Fracción", 0.125m, false).ToBaseQuantity(0.3m));
        Assert.Equal(0.000000000002m, Unit("Pequeña", 0.000000000001m, false).ToBaseQuantity(2m));
        Assert.Equal(0m, Unit("Presentation", 0.1m, false).ToBaseQuantity(0m));
    }

    [Fact]
    public void PrecisionLossAndOverflowAreRejectedInsteadOfRounding()
    {
        var tenth = Unit("Tenth", 0.1m, false);
        Assert.Throws<ArithmeticException>(() => tenth.ToBaseQuantity(0.0000000000000000000000000001m));
        Assert.Equal(decimal.MaxValue / 10m, tenth.ToBaseQuantity(decimal.MaxValue));
        Assert.Throws<ArithmeticException>(() => Unit("Nine tenths", 0.9m, false).ToBaseQuantity(decimal.MaxValue));
        Assert.Throws<OverflowException>(() => Unit("Double", 2m, false).ToBaseQuantity(decimal.MaxValue));
    }

    [Fact]
    public void InactivePresentationCannotConvertAnOperationalQuantity() =>
        Assert.Throws<InvalidOperationException>(() => Unit("Inactive", 1m, true, false).ToBaseQuantity(1m));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CompleteConfigurationRejectsNoBaseMultipleBasesAndTrimmedDuplicates(int invalid)
    {
        var product = Product();
        ProductUnitDefinition[] input = invalid switch
        {
            0 => [new("Blíster", 10m, false)],
            1 => [new("Base", 1m, true), new("Otra base", 1m, true)],
            2 => [new(" Base ", 1m, true), new("Base", 2m, false)],
            _ => throw new ArgumentOutOfRangeException(nameof(invalid)),
        };
        Assert.Throws<ArgumentException>(() => ProductUnitConfiguration.Create(product.TenantId, product, input));
        Assert.Throws<ArgumentException>(() => ProductUnitConfiguration.Create(product.TenantId, product, []));
    }

    [Fact]
    public void CompleteConfigurationIsTenantBoundAndSnapshotsIgnoreIdsOrderAndDecimalScale()
    {
        var product = Product();
        var first = ProductUnitConfiguration.Create(product.TenantId, product, [new("Base", 1m, true), new("Blíster", 10m, false, false)]);
        var second = ProductUnitConfiguration.Create(product.TenantId, product, [new("Blíster", 10.000000000000m, false, false), new("Base", 1.000000000000m, true)]);
        Assert.All(first, unit => Assert.Equal(product.TenantId, unit.TenantId));
        Assert.NotEqual(first[0].Id, second[1].Id);
        Assert.Equal(ProductUnitsAudit.Snapshot(first), ProductUnitsAudit.Snapshot(second));
        Assert.Throws<ArgumentException>(() => ProductUnitConfiguration.Create(Guid.NewGuid(), product, [new("Base", 1m, true)]));
    }

    private static ProductUnit Unit(string name, decimal factor, bool isBase, bool isActive = true)
    {
        var tenant = Guid.NewGuid();
        return ProductUnit.Create(tenant, Guid.NewGuid(), tenant, name, factor, isBase, isActive);
    }

    private static BusinessProduct Product() =>
        BusinessProduct.CreateLocal(Guid.NewGuid(), "R1", ProductType.Retail, "Retail", Guid.NewGuid(), "Brand", null, null, 0m, null, Now);
}
