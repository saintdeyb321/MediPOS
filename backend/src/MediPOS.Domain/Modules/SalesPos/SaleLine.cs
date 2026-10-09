using MediPOS.Domain.Modules.Catalog;
using MediPOS.SharedKernel;

namespace MediPOS.Domain.Modules.SalesPos;

public enum PriceKind { Retail, Wholesale }

public static class PriceKindCodes
{
    public static string ToCode(PriceKind kind) => kind switch
    {
        PriceKind.Retail => "retail",
        PriceKind.Wholesale => "wholesale",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
    public static PriceKind FromCode(string code) => code switch
    {
        "retail" => PriceKind.Retail,
        "wholesale" => PriceKind.Wholesale,
        _ => throw new InvalidOperationException("Unknown persisted sale price kind."),
    };
}

public sealed class SaleLine
{
    public const decimal MaximumQuantity = 9999999999999999.999999999999m; // numeric(28,12), like ProductUnit.
    private SaleLine() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SaleId { get; private set; }
    public Guid BusinessProductId { get; private set; }
    // Historical selection identity only: deliberately has no ProductUnit FK.
    public Guid ProductUnitIdSnapshot { get; private set; }
    public string ProductNameSnapshot { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal BaseQuantity { get; private set; }
    public string UnitNameSnapshot { get; private set; } = string.Empty;
    public decimal ConversionToBaseSnapshot { get; private set; }
    public PriceKind PriceKind { get; private set; }
    public decimal UnitPriceSnapshot { get; private set; }
    public decimal LineTotal { get; private set; }

    public static SaleLine Create(Sale sale, BusinessProduct product, ProductUnit unit, decimal quantity, PriceKind priceKind)
    {
        ArgumentNullException.ThrowIfNull(sale);
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(unit);
        sale.EnsureDraft();
        if (product.TenantId != sale.TenantId || unit.TenantId != sale.TenantId || unit.BusinessProductId != product.Id)
            throw new ArgumentException("Sale, product and presentation ownership must match.");
        if (!product.IsActive || !unit.IsActive) throw new ArgumentException("Product and presentation must be active.");
        ValidateQuantity(quantity);
        var price = priceKind switch
        {
            PriceKind.Retail => product.RetailPrice,
            PriceKind.Wholesale => product.WholesalePrice ?? throw new ArgumentException("Wholesale price is unavailable."),
            _ => throw new ArgumentOutOfRangeException(nameof(priceKind)),
        };
        var baseQuantity = unit.ToBaseQuantity(quantity);
        ValidateQuantity(baseQuantity); // Reject values that PostgreSQL would round; never round stock quantities.
        return new SaleLine
        {
            Id = Guid.CreateVersion7(),
            TenantId = sale.TenantId,
            SaleId = sale.Id,
            BusinessProductId = product.Id,
            ProductUnitIdSnapshot = unit.Id,
            ProductNameSnapshot = product.Name,
            Quantity = quantity,
            BaseQuantity = baseQuantity,
            UnitNameSnapshot = unit.Name,
            ConversionToBaseSnapshot = unit.ConversionToBase,
            PriceKind = priceKind,
            UnitPriceSnapshot = price,
            LineTotal = CalculateTotal(baseQuantity, price),
        };
    }

    public void ValidateSnapshots(Sale sale)
    {
        ArgumentNullException.ThrowIfNull(sale);
        if (Id == Guid.Empty || TenantId != sale.TenantId || SaleId != sale.Id || BusinessProductId == Guid.Empty ||
            ProductUnitIdSnapshot == Guid.Empty || !Enum.IsDefined(PriceKind) ||
            string.IsNullOrWhiteSpace(ProductNameSnapshot) || ProductNameSnapshot.Length > 256 ||
            string.IsNullOrWhiteSpace(UnitNameSnapshot) || UnitNameSnapshot.Length > 128 ||
            UnitPriceSnapshot < 0 || UnitPriceSnapshot > Sale.MaximumAmount || decimal.Round(UnitPriceSnapshot, 4) != UnitPriceSnapshot)
            throw new ArgumentException("Invalid sale line snapshots.");
        ValidateQuantity(Quantity);
        ValidateQuantity(BaseQuantity);
        ValidateQuantity(ConversionToBaseSnapshot);
        if (ProductUnit.ConvertExactly(Quantity, ConversionToBaseSnapshot) != BaseQuantity ||
            CalculateTotal(BaseQuantity, UnitPriceSnapshot) != LineTotal)
            throw new ArgumentException("Sale line quantities and amount must match their exact snapshots.");
    }

    private static void ValidateQuantity(decimal value)
    {
        if (value <= 0 || value > MaximumQuantity || decimal.Round(value, 12) != value)
            throw new ArgumentOutOfRangeException(nameof(value), "Quantity must be positive and fit numeric(28,12) exactly.");
    }

    private static decimal CalculateTotal(decimal quantity, decimal price)
    {
        try { return ExactMoney.MultiplyAndRoundToEven4(quantity, price); }
        catch (ArgumentOutOfRangeException)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Line total exceeds numeric(18,4).");
        }
    }
}
