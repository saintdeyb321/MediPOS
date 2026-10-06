namespace MediPOS.Domain.Modules.Catalog;

public sealed class BusinessProduct
{
    public const decimal MaximumPrice = 99999999999999.9999m; // PostgreSQL numeric(18,4).
    private BusinessProduct() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? GlobalProductId { get; private set; }
    public string InternalCode { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public ProductType ProductType { get; private set; }
    public Guid CategoryId { get; private set; }
    public string BrandOrLaboratory { get; private set; } = string.Empty;
    public string? Barcode { get; private set; }
    public decimal RetailPrice { get; private set; }
    public decimal? WholesalePrice { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public MedicineData? Medicine { get; private set; }

    public static BusinessProduct CreateLocal(Guid tenantId, string internalCode, ProductType productType, string name,
        Guid categoryId, string brandOrLaboratory, string? barcode, MedicineData? medicine,
        decimal retailPrice, decimal? wholesalePrice, DateTimeOffset createdAt, bool isActive = true)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("A tenant identifier is required.", nameof(tenantId));
        CatalogFields.ValidateReference(productType, categoryId, medicine);
        ValidatePrice(retailPrice);
        if (wholesalePrice.HasValue)
            ValidatePrice(wholesalePrice.Value);
        return new BusinessProduct
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            InternalCode = CatalogFields.Required(internalCode, 64, nameof(internalCode)),
            ProductType = productType,
            Name = CatalogFields.Required(name, 256, nameof(name)),
            CategoryId = categoryId,
            BrandOrLaboratory = CatalogFields.Required(brandOrLaboratory, 200, nameof(brandOrLaboratory)),
            Barcode = CatalogFields.Optional(barcode, 128, nameof(barcode)),
            Medicine = medicine?.Copy(),
            RetailPrice = retailPrice,
            WholesalePrice = wholesalePrice,
            IsActive = isActive,
            CreatedAt = createdAt.ToUniversalTime(),
        };
    }

    public static BusinessProduct FromGlobal(Guid tenantId, GlobalProduct global, string internalCode,
        decimal retailPrice, decimal? wholesalePrice, DateTimeOffset createdAt, bool isActive = true)
    {
        ArgumentNullException.ThrowIfNull(global);
        if (!global.IsActive)
            throw new ArgumentException("An active global product is required.", nameof(global));
        var product = CreateLocal(tenantId, internalCode, global.ProductType, global.Name, global.CategoryId,
            global.BrandOrLaboratory, global.Barcode, global.MedicineProfile?.Data, retailPrice, wholesalePrice, createdAt, isActive);
        product.GlobalProductId = global.Id;
        return product;
    }

    public bool UpdatePrices(decimal retailPrice, decimal? wholesalePrice)
    {
        ValidatePrice(retailPrice);
        if (wholesalePrice.HasValue)
            ValidatePrice(wholesalePrice.Value);
        if (RetailPrice == retailPrice && WholesalePrice == wholesalePrice)
            return false;
        RetailPrice = retailPrice;
        WholesalePrice = wholesalePrice;
        return true;
    }

    public bool SetStatus(bool isActive)
    {
        if (IsActive == isActive)
            return false;
        IsActive = isActive;
        return true;
    }

    private static void ValidatePrice(decimal value)
    {
        if (value < 0 || value > MaximumPrice || decimal.Round(value, 4) != value)
            throw new ArgumentOutOfRangeException(nameof(value), "Price must be nonnegative and fit numeric(18,4) exactly.");
    }
}
