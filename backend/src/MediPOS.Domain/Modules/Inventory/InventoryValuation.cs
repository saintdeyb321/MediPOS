using System.Numerics;

namespace MediPOS.Domain.Modules.Inventory;

public static class InventoryValuation
{
    public static decimal? CostValue(decimal available, decimal? unitCost, decimal? conversionToBase)
    {
        if (!unitCost.HasValue || !conversionToBase.HasValue) return null;
        ArgumentOutOfRangeException.ThrowIfNegative(available);
        ArgumentOutOfRangeException.ThrowIfNegative(unitCost.Value);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(conversionToBase.Value);
        return MultiplyDivide(available, unitCost.Value, conversionToBase.Value);
    }

    public static decimal PotentialSaleValue(decimal available, decimal retailPrice)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(available);
        ArgumentOutOfRangeException.ThrowIfNegative(retailPrice);
        return MultiplyDivide(available, retailPrice, 1m);
    }

    private static decimal MultiplyDivide(decimal quantity, decimal price, decimal factor)
    {
        var (q, qs) = StockQuantity.Parts(quantity);
        var (p, ps) = StockQuantity.Parts(price);
        var (f, fs) = StockQuantity.Parts(factor);
        // Exact rational arithmetic avoids both intermediate division rounding and multiplication overflow.
        var numerator = q * p * BigInteger.Pow(10, fs + 4);
        var denominator = f * BigInteger.Pow(10, qs + ps);
        var rounded = BigInteger.DivRem(numerator, denominator, out var remainder);
        if (remainder * 2 > denominator || (remainder * 2 == denominator && !rounded.IsEven)) rounded++;
        // This is MidpointRounding.ToEven applied only once, to the final monetary value at four places.
        var scale = 4;
        while (scale > 0 && rounded % 10 == 0) { rounded /= 10; scale--; }
        if (rounded > (BigInteger)decimal.MaxValue) throw new OverflowException("Valuation exceeds decimal precision.");
        return new decimal((int)(uint)(rounded & uint.MaxValue), (int)(uint)((rounded >> 32) & uint.MaxValue),
            (int)(uint)((rounded >> 64) & uint.MaxValue), false, (byte)scale);
    }
}
