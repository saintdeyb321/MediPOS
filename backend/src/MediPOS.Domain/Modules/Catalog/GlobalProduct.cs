namespace MediPOS.Domain.Modules.Catalog;

public sealed class GlobalProduct
{
    private GlobalProduct() { }
    public Guid Id { get; private set; }
    public ProductType ProductType { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public Guid CategoryId { get; private set; }
    public string BrandOrLaboratory { get; private set; } = string.Empty;
    public string? Barcode { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public MedicineProfile? MedicineProfile { get; private set; }

    public static GlobalProduct Create(ProductType productType, string name, Guid categoryId, string brandOrLaboratory,
        string? barcode, MedicineData? medicine, DateTimeOffset createdAt, bool isActive = true)
    {
        CatalogFields.ValidateReference(productType, categoryId, medicine);
        var product = new GlobalProduct
        {
            Id = Guid.CreateVersion7(),
            ProductType = productType,
            Name = CatalogFields.Required(name, 256, nameof(name)),
            CategoryId = categoryId,
            BrandOrLaboratory = CatalogFields.Required(brandOrLaboratory, 200, nameof(brandOrLaboratory)),
            Barcode = CatalogFields.Optional(barcode, 128, nameof(barcode)),
            IsActive = isActive,
            CreatedAt = createdAt.ToUniversalTime(),
        };
        if (medicine is not null)
            product.MedicineProfile = MedicineProfile.Create(product.Id, medicine);
        return product;
    }
}
