using System.Globalization;
using MediPOS.Domain.Modules.Catalog;

namespace MediPOS.UnitTests.Modules.Catalog;

public sealed class PharmaNormalizationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CanonicalComponentsCollapseWhitespaceCaseAccentsAndUnicodeComposition()
    {
        var first = MedicineData.Create([MedicineComponent.Create("  CAFÉÍNA\t anhidra  ", " 30\t MG ")], " Solución  oral ", " VÍA\n oral ", "REG-1");
        var second = MedicineData.Create([MedicineComponent.Create("cafe\u0301i\u0301na anhidra", "30 mg")], "solucion\tORAL", "via oral", "REG-2");
        Assert.Equal(first.EquivalenceKey, second.EquivalenceKey);
        var component = Assert.Single(first.Components);
        Assert.Equal("CAFEINA ANHIDRA", component.Ingredient);
        Assert.Equal("30 MG", component.StrengthNormalized);
        Assert.Equal("SOLUCION ORAL", first.CanonicalDosageForm);
        Assert.Equal("VIA ORAL", first.CanonicalRoute);
        Assert.Matches("^[0-9a-f]{64}$", first.EquivalenceKey!);
    }

    [Fact]
    public void CanonicalizationIsInvariantUnderTurkishCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
            var first = Medicine(" cafeína ", "30 mg", "comprimido", "via oral");
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            var second = Medicine("CAFEÍNA", "30 MG", "COMPRIMIDO", "VIA ORAL");
            Assert.Equal(first.EquivalenceKey, second.EquivalenceKey);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Fact]
    public void ComponentOrderAndExactDuplicatePairsDoNotChangeSetEquivalence()
    {
        var a = MedicineComponent.Create("Paracetamol", "500 mg");
        var b = MedicineComponent.Create("Cafeína", "30 mg");
        var first = MedicineData.Create([a, b], "Tableta", null);
        var second = MedicineData.Create([b, a, a], "tableta", " ");
        Assert.Equal(first.EquivalenceKey, second.EquivalenceKey);
        Assert.Equal(2, second.Components.Count);
        Assert.Contains(second.Components, value => value.Ingredient == "PARACETAMOL" && value.StrengthNormalized == "500 MG");
        Assert.Contains(second.Components, value => value.Ingredient == "CAFEINA" && value.StrengthNormalized == "30 MG");
        var reversedStrengths = MedicineData.Create(
            [MedicineComponent.Create("Cafeína", "500 mg"), MedicineComponent.Create("Paracetamol", "30 mg")], "Tableta");
        Assert.NotEqual(first.EquivalenceKey, reversedStrengths.EquivalenceKey);
    }

    [Theory]
    [InlineData("Otro", "500 mg", "Tableta", "Oral")]
    [InlineData("Paracetamol", "250 mg", "Tableta", "Oral")]
    [InlineData("Paracetamol", "500 mg", "Cápsula", "Oral")]
    [InlineData("Paracetamol", "500 mg", "Tableta", "Intravenosa")]
    [InlineData("Paracetamol", "500 mg", "Tableta", null)]
    public void ChangesInStructuralFieldsChangeKey(string ingredient, string strength, string form, string? route) =>
        Assert.NotEqual(Medicine("Paracetamol", "500 mg", "Tableta", "Oral").EquivalenceKey,
            Medicine(ingredient, strength, form, route).EquivalenceKey);

    [Fact]
    public void JsonEncodingPreservesTupleBoundariesAndDoesNotInferStrengthSemantics()
    {
        Assert.NotEqual(Medicine("A|B", "C").EquivalenceKey, Medicine("A", "B|C").EquivalenceKey);
        Assert.NotEqual(Medicine("Paracetamol", "500 mg").EquivalenceKey, Medicine("Paracetamol", "0.5 g").EquivalenceKey);
    }

    [Fact]
    public void ReferenceIdentityBrandBarcodeTenantPriceAndRegistrationDoNotAffectEquivalence()
    {
        var category = Guid.NewGuid();
        var first = GlobalProduct.Create(ProductType.Medicine, "Brand A product", category, "Lab A", "111",
            MedicineData.Create([MedicineComponent.Create("Paracetamol", "500 mg")], "Tableta", "Oral", "REG-1"), Now);
        var second = GlobalProduct.Create(ProductType.Medicine, "Brand B product", category, "Lab B", "222",
            MedicineData.Create([MedicineComponent.Create("paracetamol", "500 MG")], "tableta", "oral", "REG-2"), Now);
        Assert.Equal(first.MedicineProfile!.Data.EquivalenceKey, second.MedicineProfile!.Data.EquivalenceKey);
        var business = BusinessProduct.FromGlobal(Guid.NewGuid(), first, "M1", 1m, 0.5m, Now);
        business.UpdatePrices(99m, null);
        var local = BusinessProduct.CreateLocal(Guid.NewGuid(), "M2", ProductType.Medicine, "Local name", category, "Local lab",
            null, second.MedicineProfile.Data, 20m, 19m, Now);
        Assert.Equal(first.MedicineProfile.Data.EquivalenceKey, business.Medicine!.EquivalenceKey);
        Assert.Equal(business.Medicine.EquivalenceKey, local.Medicine!.EquivalenceKey);
        Assert.NotSame(first.MedicineProfile.Data, business.Medicine);
    }

    [Fact]
    public void RetailHasNoPharmaceuticalMetadataOrEquivalenceKey()
    {
        var retail = GlobalProduct.Create(ProductType.Retail, "Retail", Guid.NewGuid(), "Brand", null, null, Now);
        var business = BusinessProduct.FromGlobal(Guid.NewGuid(), retail, "R1", 0m, null, Now);
        Assert.Null(retail.MedicineProfile);
        Assert.Null(business.Medicine);
    }

    private static MedicineData Medicine(string ingredient, string strength, string form = "Tableta", string? route = null) =>
        MedicineData.Create([MedicineComponent.Create(ingredient, strength)], form, route);
}
