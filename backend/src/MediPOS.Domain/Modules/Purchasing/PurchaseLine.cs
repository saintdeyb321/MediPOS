using MediPOS.Domain.Modules.Catalog;

namespace MediPOS.Domain.Modules.Purchasing;

public sealed class PurchaseLine
{
    private PurchaseLine() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid PurchaseId { get; private set; }
    public Guid BusinessProductId { get; private set; }
    public decimal Quantity { get; private set; }
    public string UnitNameSnapshot { get; private set; } = string.Empty;
    public decimal ConversionToBaseSnapshot { get; private set; }
    public decimal BaseQuantity { get; private set; }
    public decimal UnitCost { get; private set; }
    public string? BatchNumber { get; private set; }
    public DateOnly? ExpirationDate { get; private set; }

    public static PurchaseLine Create(Purchase purchase, BusinessProduct product, ProductUnit unit,
        decimal quantity, decimal unitCost, string? batchNumber, DateOnly? expirationDate)
    {
        ArgumentNullException.ThrowIfNull(purchase);
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(unit);
        purchase.EnsureDraft();
        if (product.TenantId != purchase.TenantId || unit.TenantId != purchase.TenantId ||
            unit.BusinessProductId != product.Id)
            throw new ArgumentException("Purchase, product and unit must share tenant and product ownership.");
        if (!product.IsActive || !unit.IsActive)
            throw new ArgumentException("Product and presentation must be active.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        ArgumentOutOfRangeException.ThrowIfNegative(unitCost);
        return new PurchaseLine
        {
            Id = Guid.CreateVersion7(),
            TenantId = purchase.TenantId,
            PurchaseId = purchase.Id,
            BusinessProductId = product.Id,
            Quantity = quantity,
            UnitCost = unitCost,
            UnitNameSnapshot = unit.Name,
            ConversionToBaseSnapshot = unit.ConversionToBase,
            BaseQuantity = unit.ToBaseQuantity(quantity),
            BatchNumber = PurchasingFields.Optional(batchNumber, 128),
            ExpirationDate = expirationDate,
        };
    }

    public void ValidateForConfirmation(Purchase purchase, BusinessProduct product)
    {
        if (TenantId != purchase.TenantId || PurchaseId != purchase.Id ||
            BusinessProductId != product.Id || product.TenantId != TenantId || !product.IsActive)
            throw new ArgumentException("A valid, active product belonging to the purchase is required.");
        if (Quantity <= 0 || UnitCost < 0 || ConversionToBaseSnapshot <= 0 || BaseQuantity <= 0)
            throw new ArgumentException("Invalid purchase line quantities or cost.");
        if (product.ProductType == ProductType.Medicine && (BatchNumber is null || ExpirationDate is null))
            throw new ArgumentException("Medicine receipt requires batch and expiration date.");
    }
}
