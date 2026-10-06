using System.Security.Cryptography;
using System.Text.Json;

namespace MediPOS.Domain.Modules.Catalog;

public sealed class MedicineData
{
    // B1.1 source columns remain intact for legacy rows whose ingredient/strength association is unknown.
    private string[] _activeIngredients = [];
    private string[]? _normalizedIngredients;
    private string[]? _normalizedStrengths;
    private MedicineData() { }

    public IReadOnlyList<string> ActiveIngredients => Array.AsReadOnly(_activeIngredients);
    public string NormalizedStrength { get; private set; } = string.Empty;
    public string DosageForm { get; private set; } = string.Empty;
    public string? Route { get; private set; }
    public string? SanitaryRegistration { get; private set; }
    public string? CanonicalDosageForm { get; private set; }
    public string? CanonicalRoute { get; private set; }
    public string? EquivalenceKey { get; private set; }
    public bool IsNormalized => EquivalenceKey is not null;

    public IReadOnlyList<MedicineComponent> Components => _normalizedIngredients is null || _normalizedStrengths is null
        ? [] : Array.AsReadOnly(_normalizedIngredients.Select((ingredient, index) =>
            MedicineComponent.Create(ingredient, _normalizedStrengths[index])).ToArray());

    public static MedicineData Create(IEnumerable<MedicineComponent> components, string dosageForm,
        string? route = null, string? sanitaryRegistration = null)
    {
        ArgumentNullException.ThrowIfNull(components);
        var supplied = components.ToArray();
        if (supplied.Length == 0 || supplied.Any(value => value is null))
            throw new ArgumentException("At least one structured medicine component is required.", nameof(components));
        // Exact duplicate pairs are a set member once; different concentrations stay distinct, never summed.
        var canonical = supplied.Distinct().OrderBy(value => value.Ingredient, StringComparer.Ordinal)
            .ThenBy(value => value.StrengthNormalized, StringComparer.Ordinal).ToArray();
        var form = PharmaCanonicalization.Normalize(dosageForm, 128, nameof(dosageForm));
        var normalizedRoute = string.IsNullOrWhiteSpace(route) ? null : PharmaCanonicalization.Normalize(route, 128, nameof(route));
        var data = new MedicineData
        {
            _activeIngredients = supplied.Select(value => value.Ingredient).ToArray(),
            NormalizedStrength = string.Join(" + ", supplied.Select(value => value.StrengthNormalized)),
            DosageForm = CatalogFields.Required(dosageForm, 128, nameof(dosageForm)),
            Route = CatalogFields.Optional(route, 128, nameof(route)),
            SanitaryRegistration = CatalogFields.Optional(sanitaryRegistration, 128, nameof(sanitaryRegistration)),
            _normalizedIngredients = canonical.Select(value => value.Ingredient).ToArray(),
            _normalizedStrengths = canonical.Select(value => value.StrengthNormalized).ToArray(),
            CanonicalDosageForm = form,
            CanonicalRoute = normalizedRoute,
            EquivalenceKey = ComputeKey(canonical, form, normalizedRoute),
        };
        return data;
    }

    private static string ComputeKey(IReadOnlyList<MedicineComponent> components, string form, string? route)
    {
        // Length-safe structured encoding; labels cannot collide through delimiters or ingredient order.
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { components, dosageForm = form, route });
        return Convert.ToHexStringLower(SHA256.HashData(payload));
    }

    internal MedicineData Copy() => new()
    {
        _activeIngredients = (string[])_activeIngredients.Clone(),
        NormalizedStrength = NormalizedStrength,
        DosageForm = DosageForm,
        Route = Route,
        SanitaryRegistration = SanitaryRegistration,
        _normalizedIngredients = _normalizedIngredients is null ? null : (string[])_normalizedIngredients.Clone(),
        _normalizedStrengths = _normalizedStrengths is null ? null : (string[])_normalizedStrengths.Clone(),
        CanonicalDosageForm = CanonicalDosageForm,
        CanonicalRoute = CanonicalRoute,
        EquivalenceKey = EquivalenceKey,
    };
}
