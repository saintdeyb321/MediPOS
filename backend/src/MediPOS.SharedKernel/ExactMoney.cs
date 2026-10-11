using System.Numerics;

namespace MediPOS.SharedKernel;

public static class ExactMoney
{
    public const decimal MaximumAmount = 99999999999999.9999m; // numeric(18,4).

    // Signed decimal rounding shares the same exact arithmetic and final monetary bound as posting.
    public static decimal RoundToEven4(decimal value)
    {
        var magnitude = MultiplyAndRoundToEven4(decimal.Abs(value), 1m);
        return value < 0 ? -magnitude : magnitude;
    }

    public static decimal MultiplyAndRoundToEven4(decimal left, decimal right, int divisorPowerOfTen = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(left);
        ArgumentOutOfRangeException.ThrowIfNegative(right);
        ArgumentOutOfRangeException.ThrowIfNegative(divisorPowerOfTen);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(divisorPowerOfTen, 28);
        var (leftMantissa, leftScale) = Parts(left);
        var (rightMantissa, rightScale) = Parts(right);
        var mantissa = leftMantissa * rightMantissa;
        var scale = leftScale + rightScale + divisorPowerOfTen;
        BigInteger rounded;
        if (scale <= 4) rounded = mantissa * BigInteger.Pow(10, 4 - scale);
        else
        {
            var divisor = BigInteger.Pow(10, scale - 4);
            rounded = BigInteger.DivRem(mantissa, divisor, out var remainder);
            var midpoint = (remainder * 2).CompareTo(divisor);
            if (midpoint > 0 || (midpoint == 0 && !rounded.IsEven)) rounded++;
        }
        if (rounded > (BigInteger)(MaximumAmount * 10000m))
            throw new ArgumentOutOfRangeException(nameof(left), "Amount exceeds numeric(18,4).");
        return (decimal)rounded / 10000m;
    }

    private static (BigInteger Mantissa, int Scale) Parts(decimal value)
    {
        var bits = decimal.GetBits(value);
        return ((BigInteger)(uint)bits[0] + ((BigInteger)(uint)bits[1] << 32) + ((BigInteger)(uint)bits[2] << 64), (bits[3] >> 16) & 0xff);
    }
}
