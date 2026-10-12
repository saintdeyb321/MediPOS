using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MediPOS.Domain.Modules.Catalog.OfficialImport;

namespace MediPOS.Domain.Modules.Catalog.OfficialCuration;

public sealed record OfficialMedicineComponent(string Ingredient, string Strength);

// Explicit administrative assertions, never clinical attributes inferred from an unvalidated source schema.
public sealed record OfficialCatalogProposal([property: JsonConverter(typeof(OfficialProductTypeJsonConverter))] ProductType ProductType, string Name, string Laboratory,
    string? Registration, string Presentation, IReadOnlyList<OfficialMedicineComponent> Components,
    string DosageForm, string? Route, Guid? CategoryId, string? OfficialIdentity,
    bool IdentityVerified = false, bool ClinicalDataVerified = false)
{
    public static OfficialCatalogProposal FromStaging(OfficialCatalogStagingRow row) => new(ProductType.Medicine,
        row.OriginalName.Trim(), row.OriginalLaboratory?.Trim() ?? string.Empty, row.OriginalRegistration?.Trim(),
        row.OriginalPresentation?.Trim() ?? string.Empty, [], row.OriginalDosageFormText?.Trim() ?? string.Empty,
        row.OriginalRouteText?.Trim(), null, row.SourceRecordId);

    public IReadOnlyList<string> Errors()
    {
        var errors = new List<string>();
        void Text(string? value, int max, string field, bool required = true)
        {
            if ((required && string.IsNullOrWhiteSpace(value)) || value?.Length > max || value?.Any(char.IsControl) == true || value?.Contains('?', StringComparison.Ordinal) == true)
                errors.Add(field + "_invalid");
        }
        Text(Name, 256, "name"); Text(Laboratory, 200, "laboratory"); Text(Presentation, 1024, "presentation");
        Text(Registration, 128, "registration", false); Text(OfficialIdentity, 256, "official_identity");
        if (!IdentityVerified) errors.Add("identity_unverified");
        if (!CategoryId.HasValue || CategoryId == Guid.Empty) errors.Add("category_required");
        if (ProductType == ProductType.Medicine)
        {
            Text(DosageForm, 128, "dosage_form"); Text(Route, 128, "route", false);
            if (!ClinicalDataVerified) errors.Add("clinical_data_unverified");
            if (Components is null || Components.Count is < 1 or > 32) errors.Add("ingredient_requires_review");
            else foreach (var component in Components)
            {
                if (component is null) { errors.Add("ingredient_requires_review"); continue; }
                Text(component.Ingredient, 200, "ingredient");
                if (component.Ingredient.Trim().Equals("unknown", StringComparison.OrdinalIgnoreCase) || component.Ingredient.Trim().Equals("desconocido", StringComparison.OrdinalIgnoreCase)) errors.Add("ingredient_unknown");
                if (component.Strength.Length > 128 || !StrengthShape.IsMatch(component.Strength)) errors.Add("strength_ambiguous");
            }
            if (Components is null || Components.Count == 0) errors.Add("strength_requires_review");
        }
        else if (ProductType != ProductType.Retail || Components is null || Components.Count != 0 || !string.IsNullOrWhiteSpace(DosageForm) || !string.IsNullOrWhiteSpace(Route) || !string.IsNullOrWhiteSpace(Registration))
            errors.Add("product_type_invalid");
        if (errors.Count == 0)
        {
            try { _ = CreateGlobalProduct(DateTimeOffset.UnixEpoch); }
            catch (ArgumentException) { errors.Add("domain_data_invalid"); }
        }
        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }
    // Shape validation only. No conversion of dose, unit, salt, package, denominator or numeric precision.
    // Accept mcg, U+00B5 micro sign and U+03BC Greek mu explicitly; other Unicode lookalikes are not units.
    private static readonly Regex StrengthShape = new(@"^\s*[0-9]+(?:[.,][0-9]+)?\s*(?:mcg|(?-i:[\u00B5\u03BC])g|mg|g|mL|L|IU|UI|%)(?:\s*/\s*(?:[0-9]+(?:[.,][0-9]+)?\s*)?(?:mL|L|g|mg))?\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));

    public bool HasValidCategory(Category? category) => category is not null && category.Id == CategoryId && category.IsActive;
    public MedicineData? Medicine() => ProductType == ProductType.Retail ? null : MedicineData.Create(
        Components.Select(component => MedicineComponent.Create(component.Ingredient, component.Strength)), DosageForm, Route, Registration);
    public GlobalProduct CreateGlobalProduct(DateTimeOffset now) => GlobalProduct.Create(ProductType, Name,
        CategoryId ?? Guid.Empty, Laboratory, null, Medicine(), now);
    [JsonIgnore]
    public string? EquivalenceKey => Components.Count == 0 || ProductType != ProductType.Medicine ? null : Medicine()?.EquivalenceKey;
    public string Fingerprint() => Hash(JsonSerializer.Serialize(new
    {
        type = ProductTypeCodes.ToCode(ProductType),
        name = TextKey(Name),
        laboratory = TextKey(Laboratory),
        registration = TextKey(Registration ?? string.Empty),
        presentation = TextKey(Presentation),
        category = CategoryId,
        equivalence = EquivalenceKey,
    }));
    public static string TextKey(string value) => string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed class OfficialProductTypeJsonConverter : JsonConverter<ProductType>
{
    public override ProductType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ProductTypeCodes.FromCode(reader.GetString() ?? throw new JsonException("Product type is required."));
    public override void Write(Utf8JsonWriter writer, ProductType value, JsonSerializerOptions options) => writer.WriteStringValue(ProductTypeCodes.ToCode(value));
}
