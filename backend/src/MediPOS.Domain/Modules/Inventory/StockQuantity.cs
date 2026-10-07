using System.Numerics;

namespace MediPOS.Domain.Modules.Inventory;

public static class StockQuantity
{
    internal static (BigInteger Mantissa, int Scale) Parts(decimal value)
    {
        var bits = decimal.GetBits(value);
        var mantissa = (BigInteger)(uint)bits[0] + ((BigInteger)(uint)bits[1] << 32) + ((BigInteger)(uint)bits[2] << 64);
        return (bits[3] < 0 ? -mantissa : mantissa, (bits[3] >> 16) & 0xff);
    }

    public static decimal Add(decimal before, decimal delta)
    {
        var result = checked(before + delta);
        var (a, scaleA) = Parts(before);
        var (b, scaleB) = Parts(delta);
        var (r, scaleR) = Parts(result);
        var scale = Math.Max(Math.Max(scaleA, scaleB), scaleR);
        if (a * BigInteger.Pow(10, scale - scaleA) + b * BigInteger.Pow(10, scale - scaleB) != r * BigInteger.Pow(10, scale - scaleR))
            throw new ArithmeticException("Stock quantities must remain exactly representable as decimal.");
        return result;
    }
}

public sealed class InsufficientStockException : InvalidOperationException
{
    public InsufficientStockException() : base("Insufficient available stock.") { }
}
