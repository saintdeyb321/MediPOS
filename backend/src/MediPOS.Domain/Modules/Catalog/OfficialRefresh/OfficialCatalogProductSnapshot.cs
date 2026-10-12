using System.Text.Json;
using MediPOS.Domain.Modules.Catalog.OfficialCuration;

namespace MediPOS.Domain.Modules.Catalog.OfficialRefresh;

public sealed record OfficialCatalogMedicineSnapshot(IReadOnlyList<string> ActiveIngredients, string NormalizedStrength,
    string DosageForm, string? Route, string? SanitaryRegistration, string? CanonicalDosageForm, string? CanonicalRoute,
    string? EquivalenceKey, IReadOnlyList<string> NormalizedIngredients, IReadOnlyList<string> NormalizedStrengths);
public sealed record OfficialCatalogProductSnapshot(Guid GlobalProductId, OfficialCatalogProposal Proposal,
    string? Barcode, bool IsActive, DateTimeOffset CreatedAtUtc, OfficialCatalogMedicineSnapshot? Medicine)
{
    public static OfficialCatalogProductSnapshot Capture(GlobalProduct product, OfficialCatalogProposal original)
    {
        var expected = original.Medicine();
        var actual = product.MedicineProfile?.Data;
        if (product.ProductType != original.ProductType || (expected is null) != (actual is null) ||
            expected is not null && (actual!.EquivalenceKey != expected.EquivalenceKey ||
                OfficialCatalogProposal.TextKey(actual.SanitaryRegistration ?? string.Empty) != OfficialCatalogProposal.TextKey(expected.SanitaryRegistration ?? string.Empty)))
            throw new InvalidOperationException("refresh_product_incompatible");
        return new(product.Id, original with { Name = product.Name, Laboratory = product.BrandOrLaboratory, CategoryId = product.CategoryId },
            product.Barcode, product.IsActive, product.CreatedAt, actual is null ? null : new(actual.ActiveIngredients.ToArray(), actual.NormalizedStrength,
                actual.DosageForm, actual.Route, actual.SanitaryRegistration, actual.CanonicalDosageForm, actual.CanonicalRoute, actual.EquivalenceKey,
                actual.Components.Select(item => item.Ingredient).ToArray(), actual.Components.Select(item => item.StrengthNormalized).ToArray()));
    }
    public string Json() => JsonSerializer.Serialize(this);
    public string Fingerprint() => OfficialCatalogProposal.Hash(Json());
    public static OfficialCatalogProductSnapshot Read(string json) => JsonSerializer.Deserialize<OfficialCatalogProductSnapshot>(json)!;
}
