using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.Purchasing;

namespace MediPOS.UnitTests.Modules.Purchasing;

public sealed class PurchasingDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 9, 0, 0, TimeSpan.FromHours(-5));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void SupplierRequiresName(string? name) => Assert.Throws<ArgumentException>(() =>
        Supplier.Create(Guid.NewGuid(), name!, null, null, Now));

    [Fact]
    public void SupplierNormalizesOptionalFieldsAndUtc()
    {
        var supplier = Supplier.Create(Guid.NewGuid(), "  Proveedor  ", " 123 ", "  correo  ", Now);
        Assert.Equal("Proveedor", supplier.Name);
        Assert.Equal("123", supplier.Ruc);
        Assert.Equal("correo", supplier.Contact);
        Assert.Equal(TimeSpan.Zero, supplier.CreatedAt.Offset);
        var optional = Supplier.Create(supplier.TenantId, "Proveedor", " ", null, Now);
        Assert.Null(optional.Ruc);
        Assert.Null(optional.Contact);
    }

    [Fact]
    public void LineSnapshotsCurrentPresentationWithExactDecimalCostAndBaseQuantity()
    {
        var (purchase, product) = Setup();
        var unit = ProductUnit.Create(purchase.TenantId, product.Id, product.TenantId, " Fracción ", 0.125000000001m, false);
        var line = PurchaseLine.Create(purchase, product, unit, 2m, 0.1234567890123456789012345678m, " L-1 ", new(2020, 1, 1));
        Assert.Equal("Fracción", line.UnitNameSnapshot);
        Assert.Equal(unit.ConversionToBase, line.ConversionToBaseSnapshot);
        Assert.Equal(0.250000000002m, line.BaseQuantity);
        Assert.Equal(0.1234567890123456789012345678m, line.UnitCost);
        Assert.Equal("L-1", line.BatchNumber);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    public void InvalidQuantityOrCostRejected(decimal quantity, decimal cost)
    {
        var (purchase, product) = Setup();
        Assert.Throws<ArgumentOutOfRangeException>(() => PurchaseLine.Create(purchase, product, Unit(product), quantity, cost, null, null));
    }

    [Theory]
    [InlineData("inactive_unit")]
    [InlineData("inactive_product")]
    [InlineData("foreign_unit")]
    [InlineData("wrong_product")]
    public void UnavailableOrMismatchedCatalogEntriesRejected(string scenario)
    {
        var (purchase, product) = Setup();
        if (scenario == "inactive_product") product.SetStatus(false);
        var unitTenant = scenario == "foreign_unit" ? Guid.NewGuid() : product.TenantId;
        var unit = ProductUnit.Create(unitTenant,
            scenario == "wrong_product" ? Guid.NewGuid() : product.Id,
            unitTenant, "Base", 1m, true, scenario != "inactive_unit");
        Assert.Throws<ArgumentException>(() => PurchaseLine.Create(purchase, product, unit, 1m, 0m, null, null));
    }

    [Fact]
    public void UnrepresentableConversionIsRejectedWithoutRounding()
    {
        var (purchase, product) = Setup();
        var unit = ProductUnit.Create(product.TenantId, product.Id, product.TenantId, "Small", 0.000000000001m, false);
        Assert.Throws<ArithmeticException>(() => PurchaseLine.Create(purchase, product, unit, 0.0000000000000000000000000001m, 0m, null, null));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("L", false)]
    [InlineData(null, true)]
    public void MedicineNeedsBatchAndExpirationAtConfirmation(string? batch, bool expiration)
    {
        var (purchase, product) = Setup(ProductType.Medicine);
        var line = PurchaseLine.Create(purchase, product, Unit(product), 1m, 0m, batch, expiration ? new DateOnly(2020, 1, 1) : null);
        Assert.Throws<ArgumentException>(() => purchase.Confirm([line], new Dictionary<Guid, BusinessProduct> { [product.Id] = product }, Guid.NewGuid(), Now));
        Assert.Equal(PurchaseStatus.Draft, purchase.Status);
    }

    [Theory]
    [InlineData(ProductType.Retail)]
    [InlineData(ProductType.Medicine)]
    public void SuccessfulConfirmationIsTerminalAndAllowsPastExpiration(ProductType type)
    {
        var (purchase, product) = Setup(type);
        var line = PurchaseLine.Create(purchase, product, Unit(product), 3m, 0m, type == ProductType.Medicine ? "L" : null,
            type == ProductType.Medicine ? new DateOnly(2020, 1, 1) : null);
        var catalog = new Dictionary<Guid, BusinessProduct> { [product.Id] = product };
        var actor = Guid.NewGuid();
        purchase.Confirm([line], catalog, actor, Now);
        Assert.Equal(PurchaseStatus.Confirmed, purchase.Status);
        Assert.Equal(actor, purchase.ConfirmedByActorId);
        Assert.Equal(Now.ToUniversalTime(), purchase.ConfirmedAt);
        Assert.Throws<InvalidOperationException>(purchase.EnsureDraft);
        Assert.Throws<InvalidOperationException>(() => purchase.Confirm([line], catalog, actor, Now));
        Assert.Throws<InvalidOperationException>(() => PurchaseLine.Create(purchase, product, Unit(product), 1m, 0m, null, null));
    }

    [Fact]
    public void EmptyConfirmationCannotMutateDraft()
    {
        var (purchase, _) = Setup();
        Assert.Throws<ArgumentException>(() => purchase.Confirm([], new Dictionary<Guid, BusinessProduct>(), Guid.NewGuid(), Now));
        Assert.Equal(PurchaseStatus.Draft, purchase.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ReceiptCannotChangeStockByNonpositiveQuantity(decimal quantity)
    {
        var lot = InventoryLot.Receive(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, Now);
        Assert.Throws<ArgumentOutOfRangeException>(() => StockMovement.Receive(lot, quantity, Guid.NewGuid(), Now));
    }

    internal static (Purchase Purchase, BusinessProduct Product) Setup(ProductType type = ProductType.Retail)
    {
        var tenant = Guid.NewGuid();
        var medicine = type == ProductType.Medicine ? MedicineData.Create([MedicineComponent.Create("Paracetamol", "500 mg")], "Tableta", "Oral") : null;
        var product = BusinessProduct.CreateLocal(tenant, "R1", type, "Producto", Guid.NewGuid(), "Marca", null, medicine, 0m, null, Now);
        return (Purchase.Create(tenant, Guid.NewGuid(), null, " F001-1 ", Guid.NewGuid(), Now), product);
    }
    internal static ProductUnit Unit(BusinessProduct product) => ProductUnit.Create(product.TenantId, product.Id, product.TenantId, "Base", 1m, true);
}
