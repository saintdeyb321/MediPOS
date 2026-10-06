using System.Globalization;
using MediPOS.Domain.Modules.Catalog;

namespace MediPOS.UnitTests.Modules.Catalog;

public sealed class CatalogDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 9, 0, 0, TimeSpan.FromHours(-5));
    private static readonly string[] ExpectedIngredients = ["Paracetamol", "Cafeína"];
    private static MedicineData Medicine() => MedicineData.Create([" Paracetamol ", "Cafeína"], "500 mg + 30 mg", " Tableta ", " Oral ", " RS-1 ");

    [Fact]
    public void CategoryTrimsNameAndUsesUtcAndUuidV7()
    {
        var category = Category.Create("  Analgésicos  ", Now);
        Assert.Equal("Analgésicos", category.Name);
        Assert.True(category.IsActive);
        Assert.Equal(7, category.Id.Version);
        Assert.Equal(Now.ToUniversalTime(), category.CreatedAt);
        Assert.Equal(TimeSpan.Zero, category.CreatedAt.Offset);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void CategoryRequiresName(string name) => Assert.Throws<ArgumentException>(() => Category.Create(name, Now));

    [Fact]
    public void MedicinePreservesMultipleIngredientsAndDefensivelyCopiesInputs()
    {
        var ingredients = new[] { " Paracetamol ", " Cafeína " };
        var data = MedicineData.Create(ingredients, " 500 mg + 30 mg ", " Tableta ", " ", null);
        ingredients[0] = "Changed";
        Assert.Equal(ExpectedIngredients, data.ActiveIngredients);
        Assert.Equal("500 mg + 30 mg", data.NormalizedStrength);
        Assert.Equal("Tableta", data.DosageForm);
        Assert.Null(data.Route);
        Assert.Null(data.SanitaryRegistration);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)data.ActiveIngredients)[0] = "Changed");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void MedicineRequiresCompositionStrengthAndForm(int invalid)
    {
        Assert.Throws<ArgumentException>(() => MedicineData.Create(invalid == 0 ? [] : [invalid == 1 ? " " : "Paracetamol"],
            invalid == 2 ? " " : "500 mg", invalid == 3 ? "" : "Tableta"));
    }

    [Fact]
    public void GlobalMedicineHasDependentProfileAndNoPrivateOperationalFields()
    {
        var data = Medicine();
        var global = GlobalProduct.Create(ProductType.Medicine, " Producto ", Guid.NewGuid(), " Laboratorio ", "  ", data, Now);
        Assert.Equal(global.Id, global.MedicineProfile!.GlobalProductId);
        Assert.NotSame(data, global.MedicineProfile.Data);
        Assert.Equal(data.ActiveIngredients, global.MedicineProfile.Data.ActiveIngredients);
        Assert.Null(global.Barcode);
        Assert.Equal(TimeSpan.Zero, global.CreatedAt.Offset);
        var names = typeof(GlobalProduct).GetProperties().Select(value => value.Name).ToArray();
        Assert.DoesNotContain(names, name => name.Contains("Price", StringComparison.Ordinal) ||
            name is "TenantId" or "Cost" or "Stock" or "BranchId");
        Assert.DoesNotContain(typeof(MedicineProfile).GetProperties(), value => value.Name == "TenantId");
    }

    [Fact]
    public void RetailDoesNotRequireMedicineOrBarcode()
    {
        var global = GlobalProduct.Create(ProductType.Retail, "Pañales", Guid.NewGuid(), "Marca", null, null, Now);
        Assert.Null(global.MedicineProfile);
        Assert.Null(global.Barcode);
        Assert.Equal(7, global.Id.Version);
        Assert.Equal("retail", ProductTypeCodes.ToCode(global.ProductType));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void GlobalRejectsMissingRequiredReferenceData(int invalid) =>
        Assert.ThrowsAny<ArgumentException>(() => GlobalProduct.Create(invalid == 0 ? (ProductType)99 : ProductType.Medicine,
            invalid == 1 ? " " : "Medicine", invalid == 2 ? Guid.Empty : Guid.NewGuid(),
            invalid == 3 ? "" : "Lab", null, invalid == 4 ? null : Medicine(), Now));

    [Fact]
    public void BusinessFromGlobalCopiesReferenceAndMedicineWithoutChangingGlobal()
    {
        var global = GlobalProduct.Create(ProductType.Medicine, "Medicine", Guid.NewGuid(), "Lab", " 123 ", Medicine(), Now);
        var product = BusinessProduct.FromGlobal(Guid.NewGuid(), global, " MED-1 ", 1.2345m, 0m, Now, false);
        Assert.Equal(global.Id, product.GlobalProductId);
        Assert.Equal(global.CategoryId, product.CategoryId);
        Assert.Equal(global.Name, product.Name);
        Assert.Equal(global.ProductType, product.ProductType);
        Assert.Equal(global.BrandOrLaboratory, product.BrandOrLaboratory);
        Assert.Equal("123", product.Barcode);
        Assert.Equal("MED-1", product.InternalCode);
        Assert.NotSame(global.MedicineProfile!.Data, product.Medicine);
        Assert.Equal(global.MedicineProfile.Data.ActiveIngredients, product.Medicine!.ActiveIngredients);
        product.UpdatePrices(2m, null);
        Assert.Equal("Medicine", global.Name);
        Assert.True(global.IsActive);
        Assert.False(product.IsActive);
        Assert.Equal(7, product.Id.Version);
    }

    [Fact]
    public void LocalMedicineHasNoGlobalReferenceAndRetainsPharmaceuticalData()
    {
        var product = Local(ProductType.Medicine, Medicine());
        Assert.Null(product.GlobalProductId);
        Assert.Equal(ExpectedIngredients, product.Medicine!.ActiveIngredients);
        Assert.Equal("Oral", product.Medicine.Route);
        Assert.Equal("RS-1", product.Medicine.SanitaryRegistration);
        Assert.Equal(TimeSpan.Zero, product.CreatedAt.Offset);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void InternalCodeIsRequired(string code) =>
        Assert.Throws<ArgumentException>(() => BusinessProduct.CreateLocal(Guid.NewGuid(), code, ProductType.Retail,
            "Retail", Guid.NewGuid(), "Brand", null, null, 0m, null, Now));

    [Theory]
    [InlineData("-0.0001")]
    [InlineData("0.00001")]
    [InlineData("100000000000000")]
    public void PricesCannotBeNegativeOverflowOrRequireRounding(string value)
    {
        var price = decimal.Parse(value, CultureInfo.InvariantCulture);
        var product = Local();
        Assert.Throws<ArgumentOutOfRangeException>(() => product.UpdatePrices(price, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => product.UpdatePrices(0m, price));
        Assert.Equal(0m, product.RetailPrice);
        Assert.Null(product.WholesalePrice);
        Assert.Throws<ArgumentOutOfRangeException>(() => BusinessProduct.CreateLocal(Guid.NewGuid(), "R1", ProductType.Retail,
            "Retail", Guid.NewGuid(), "Brand", null, null, price, null, Now));
    }

    [Fact]
    public void NullableWholesaleAndIdempotentPriceStatusChangesPreserveProduct()
    {
        var product = Local();
        Assert.False(product.UpdatePrices(0m, null));
        Assert.True(product.UpdatePrices(BusinessProduct.MaximumPrice, 0m));
        Assert.False(product.UpdatePrices(BusinessProduct.MaximumPrice, 0m));
        Assert.True(product.UpdatePrices(0m, null));
        Assert.True(product.SetStatus(false));
        Assert.False(product.SetStatus(false));
        Assert.True(product.SetStatus(true));
        Assert.Null(product.GlobalProductId);
        Assert.Null(product.Medicine);
    }

    [Fact]
    public void TypeCodesAreExplicitAndUnknownCodesRejected()
    {
        Assert.Equal(ProductType.Medicine, ProductTypeCodes.FromCode("medicine"));
        Assert.Equal(ProductType.Retail, ProductTypeCodes.FromCode("retail"));
        Assert.Equal("medicine", ProductTypeCodes.ToCode(ProductType.Medicine));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProductTypeCodes.ToCode((ProductType)99));
        Assert.Throws<InvalidOperationException>(() => ProductTypeCodes.FromCode("Medicine"));
    }

    [Fact]
    public void EmptyTenantAndInactiveGlobalAndMissingLocalMedicineAreRejected()
    {
        var category = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => BusinessProduct.CreateLocal(Guid.Empty, "R1", ProductType.Retail,
            "Retail", category, "Brand", null, null, 0m, null, Now));
        Assert.Throws<ArgumentException>(() => BusinessProduct.CreateLocal(Guid.NewGuid(), "M1", ProductType.Medicine,
            "Medicine", category, "Lab", null, null, 0m, null, Now));
        var inactive = GlobalProduct.Create(ProductType.Retail, "Retail", category, "Brand", null, null, Now, false);
        Assert.Throws<ArgumentException>(() => BusinessProduct.FromGlobal(Guid.NewGuid(), inactive, "R1", 0m, null, Now));
    }

    private static BusinessProduct Local(ProductType type = ProductType.Retail, MedicineData? medicine = null) =>
        BusinessProduct.CreateLocal(Guid.NewGuid(), "R1", type, "Local", Guid.NewGuid(), "Brand", null, medicine, 0m, null, Now);
}
