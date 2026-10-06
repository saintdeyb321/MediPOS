using System.Numerics;

namespace MediPOS.Domain.Modules.Catalog;

public sealed class ProductUnit
{
    public const decimal MaximumConversion = 9999999999999999.999999999999m; // numeric(28,12).
    private ProductUnit() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BusinessProductId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public decimal ConversionToBase { get; private set; }
    public bool IsBaseUnit { get; private set; }
    public bool IsActive { get; private set; }

    public static ProductUnit Create(Guid tenantId, Guid businessProductId, Guid productTenantId,
        string name, decimal conversionToBase, bool isBaseUnit, bool isActive = true)
    {
        if (tenantId == Guid.Empty || businessProductId == Guid.Empty)
            throw new ArgumentException("Tenant and business product identifiers are required.");
        if (tenantId != productTenantId)
            throw new ArgumentException("Unit and product must belong to the same tenant.");
        if (conversionToBase <= 0 || conversionToBase > MaximumConversion || decimal.Round(conversionToBase, 12) != conversionToBase)
            throw new ArgumentOutOfRangeException(nameof(conversionToBase), "Conversion must be positive and fit numeric(28,12) exactly.");
        if (isBaseUnit && conversionToBase != 1m)
            throw new ArgumentException("The base unit must have a conversion of one.", nameof(conversionToBase));
        return new ProductUnit
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            BusinessProductId = businessProductId,
            Name = CatalogFields.Required(name, 128, nameof(name)),
            ConversionToBase = conversionToBase,
            IsBaseUnit = isBaseUnit,
            IsActive = isActive,
        };
    }

    public decimal ToBaseQuantity(decimal quantity)
    {
        if (!IsActive)
            throw new InvalidOperationException("An inactive presentation is unavailable for operations.");
        var converted = checked(quantity * ConversionToBase);
        var (quantityMantissa, quantityScale) = Parts(quantity);
        var (factorMantissa, factorScale) = Parts(ConversionToBase);
        var (resultMantissa, resultScale) = Parts(converted);
        // Check the rational result independently: decimal multiplication can lose low-order precision.
        if (quantityMantissa * factorMantissa * BigInteger.Pow(10, resultScale) !=
            resultMantissa * BigInteger.Pow(10, quantityScale + factorScale))
            throw new ArithmeticException("The base quantity cannot be represented exactly as decimal.");
        return converted;
    }

    private static (BigInteger Mantissa, int Scale) Parts(decimal value)
    {
        var bits = decimal.GetBits(value);
        var mantissa = (BigInteger)(uint)bits[0] + ((BigInteger)(uint)bits[1] << 32) + ((BigInteger)(uint)bits[2] << 64);
        return (bits[3] < 0 ? -mantissa : mantissa, (bits[3] >> 16) & 0xff);
    }
}
