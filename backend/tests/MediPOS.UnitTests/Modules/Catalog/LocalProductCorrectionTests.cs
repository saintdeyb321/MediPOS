using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.SalesPos;

namespace MediPOS.UnitTests.Modules.Catalog;

public sealed class LocalProductCorrectionTests
{
    [Fact]
    public void CorrectionPreservesPriorMedicineCopyIdentityPricesAndCreationTime()
    {
        var original = MedicineData.Create([MedicineComponent.Create("Paracetamol", "500 mg")], "Tableta", "Oral");
        var product = BusinessProduct.CreateLocal(Guid.NewGuid(), "LOCAL", ProductType.Medicine, "Original", Guid.NewGuid(), "Brand", null, original, 1m, 0.9m, DateTimeOffset.UtcNow);
        var snapshot = product.Medicine!; var id = product.Id; var created = product.CreatedAt;
        var sale = Sale.CreateDraft(product.TenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), created);
        var unit = ProductUnit.Create(product.TenantId, product.Id, product.TenantId, "Tableta", 1m, true);
        var historicalLine = SaleLine.Create(sale, product, unit, 2m, PriceKind.Retail);
        product.CorrectLocalData("Corrected", product.CategoryId, "Brand", null, MedicineData.Create([MedicineComponent.Create("Paracetamol", "25 µg")], "Tableta", "Oral"));
        Assert.Equal(Assert.Single(original.Components).StrengthNormalized, Assert.Single(snapshot.Components).StrengthNormalized);
        Assert.Equal(MedicineComponent.Create("Paracetamol", "25 µg").StrengthNormalized, Assert.Single(product.Medicine!.Components).StrengthNormalized);
        Assert.NotEqual(snapshot.NormalizedStrength, product.Medicine.NormalizedStrength);
        Assert.Equal(id, product.Id); Assert.Equal(created, product.CreatedAt); Assert.Null(product.GlobalProductId);
        Assert.Equal(1m, product.RetailPrice); Assert.Equal(0.9m, product.WholesalePrice); Assert.Equal("LOCAL", product.InternalCode);
        Assert.Equal("Original", historicalLine.ProductNameSnapshot); Assert.Equal("Tableta", historicalLine.UnitNameSnapshot);
        Assert.Equal(1m, historicalLine.UnitPriceSnapshot); Assert.Equal(2m, historicalLine.LineTotal);
    }
    [Fact]
    public void InvalidCorrectionAndGlobalReferenceAreRejectedWithoutChangingIdentity()
    {
        var tenant = Guid.NewGuid(); var category = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        var local = BusinessProduct.CreateLocal(tenant, "L", ProductType.Retail, "Original", category, "Brand", null, null, 1m, null, now);
        Assert.Throws<ArgumentException>(() => local.CorrectLocalData("Changed", category, "", null, null)); Assert.Equal("Original", local.Name);
        var global = GlobalProduct.Create(ProductType.Retail, "Global", category, "Brand", null, null, now);
        var linked = BusinessProduct.FromGlobal(tenant, global, "G", 1m, null, now);
        Assert.Throws<ArgumentException>(() => linked.CorrectLocalData("Changed", category, "Brand", null, null));
        Assert.Equal(global.Id, linked.GlobalProductId); Assert.Equal("Global", linked.Name);
    }
}
