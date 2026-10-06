namespace MediPOS.Domain.Modules.Catalog;

public enum ProductType { Medicine, Retail }

public static class ProductTypeCodes
{
    public static string ToCode(ProductType type) => type switch
    {
        ProductType.Medicine => "medicine",
        ProductType.Retail => "retail",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static ProductType FromCode(string code) => code switch
    {
        "medicine" => ProductType.Medicine,
        "retail" => ProductType.Retail,
        _ => throw new InvalidOperationException("Unknown persisted product type."),
    };
}
