namespace MediPOS.Domain.Modules.Catalog;

// Structured input: concentration is supplied explicitly. No parsing or unit/synonym inference.
public sealed record MedicineComponent
{
    private MedicineComponent(string ingredient, string strengthNormalized)
    {
        Ingredient = ingredient;
        StrengthNormalized = strengthNormalized;
    }

    public string Ingredient { get; }
    public string StrengthNormalized { get; }

    public static MedicineComponent Create(string ingredient, string strengthNormalized) => new(
        PharmaCanonicalization.Normalize(ingredient, 200, nameof(ingredient)),
        PharmaCanonicalization.Normalize(strengthNormalized, 128, nameof(strengthNormalized)));
}
