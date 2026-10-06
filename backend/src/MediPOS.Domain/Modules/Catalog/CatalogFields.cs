namespace MediPOS.Domain.Modules.Catalog;

internal static class CatalogFields
{
    public static string Required(string value, int maxLength, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new ArgumentException("Catalog text exceeds its storage limit.", parameter);
        return trimmed;
    }

    public static string? Optional(string? value, int maxLength, string parameter) =>
        string.IsNullOrWhiteSpace(value) ? null : Required(value, maxLength, parameter);

    public static void ValidateReference(ProductType type, Guid categoryId, MedicineData? medicine)
    {
        if (!Enum.IsDefined(type))
            throw new ArgumentOutOfRangeException(nameof(type));
        if (categoryId == Guid.Empty)
            throw new ArgumentException("A category identifier is required.", nameof(categoryId));
        if (type == ProductType.Medicine && medicine is null)
            throw new ArgumentException("Medicine metadata is required.", nameof(medicine));
        if (type == ProductType.Medicine && medicine is { IsNormalized: false })
            throw new ArgumentException("New medicines require structured normalized components.", nameof(medicine));
        if (type == ProductType.Retail && medicine is not null)
            throw new ArgumentException("Medicine metadata belongs to pharmaceutical products.", nameof(medicine));
    }
}
