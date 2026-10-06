namespace MediPOS.Domain.Modules.Catalog;

// B1.1 preserves supplied pharmaceutical data. Parsing, unit conversion and equivalence belong to later slices.
public sealed class MedicineData
{
    private string[] _activeIngredients = [];
    private MedicineData() { }

    public IReadOnlyList<string> ActiveIngredients => Array.AsReadOnly(_activeIngredients);
    public string NormalizedStrength { get; private set; } = string.Empty;
    public string DosageForm { get; private set; } = string.Empty;
    public string? Route { get; private set; }
    public string? SanitaryRegistration { get; private set; }

    public static MedicineData Create(IEnumerable<string> activeIngredients, string normalizedStrength,
        string dosageForm, string? route = null, string? sanitaryRegistration = null)
    {
        ArgumentNullException.ThrowIfNull(activeIngredients);
        var ingredients = activeIngredients.Select(value => CatalogFields.Required(value, 200, nameof(activeIngredients))).ToArray();
        if (ingredients.Length == 0)
            throw new ArgumentException("At least one active ingredient is required.", nameof(activeIngredients));
        return new MedicineData
        {
            _activeIngredients = ingredients,
            NormalizedStrength = CatalogFields.Required(normalizedStrength, 128, nameof(normalizedStrength)),
            DosageForm = CatalogFields.Required(dosageForm, 128, nameof(dosageForm)),
            Route = CatalogFields.Optional(route, 128, nameof(route)),
            SanitaryRegistration = CatalogFields.Optional(sanitaryRegistration, 128, nameof(sanitaryRegistration)),
        };
    }

    internal MedicineData Copy() => Create(ActiveIngredients, NormalizedStrength, DosageForm, Route, SanitaryRegistration);
}
