using System.Numerics;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.SalesPos;

namespace MediPOS.Domain.Modules.Reporting;

public sealed record ReplenishmentEstimate(decimal AverageDailyConsumptionBase, decimal DemandTargetBase,
    decimal TargetStockBase, decimal RawSuggestedBase, decimal SuggestedReplenishmentBase,
    decimal? EstimatedDaysOfStock, bool IsAtRisk, bool BelowConfiguredMinimum)
{
    // The existing operational quantities permit fractions at 12 places for every product;
    // there is no per-product integer-only constraint. This is quantity precision, not a purchase pack.
    public const decimal QuantityGranularityBase = 0.000000000001m;

    public static ReplenishmentEstimate Calculate(decimal sold, decimal sellable, decimal? minimum,
        int lookbackDays, int leadTimeDays, int coverageDays)
    {
        if (sold < 0 || sellable < 0 || minimum < 0 || lookbackDays is < 7 or > 90 || leadTimeDays is < 0 or > 60 || coverageDays is < 1 or > 90)
            throw new ArgumentOutOfRangeException(nameof(lookbackDays));
        var consumption = Rational.From(sold).Divide(lookbackDays);
        var demand = consumption.Multiply(leadTimeDays + coverageDays);
        var floor = Rational.From(minimum ?? 0m);
        var target = demand.CompareTo(floor) >= 0 ? demand : floor;
        var stock = Rational.From(sellable);
        var need = target.Subtract(stock);
        if (need.Numerator < 0) need = Rational.From(0m);
        var suggestion = need.ToQuantityCeiling();
        // Cross multiplication uses the original ratio, including at equality with lead time.
        var atRisk = sold > 0 && stock.CompareTo(consumption.Multiply(leadTimeDays)) <= 0;
        var estimatedDays = sold == 0 ? (decimal?)null : stock.Divide(consumption).ToDecimal();
        return new(consumption.ToDecimal(), demand.ToDecimal(), target.ToDecimal(), need.ToDecimal(),
            suggestion, estimatedDays, atRisk, minimum.HasValue && sellable < minimum.Value);
    }

    private readonly record struct Rational(BigInteger Numerator, BigInteger Denominator)
    {
        internal static Rational From(decimal value)
        {
            var (mantissa, scale) = StockQuantity.Parts(value);
            return new(mantissa, BigInteger.Pow(10, scale));
        }
        internal Rational Multiply(int value) => new(Numerator * value, Denominator);
        internal Rational Divide(int value) => new(Numerator, Denominator * value);
        internal Rational Divide(Rational value) => new(Numerator * value.Denominator, Denominator * value.Numerator);
        internal Rational Subtract(Rational value) => new(Numerator * value.Denominator - value.Numerator * Denominator, Denominator * value.Denominator);
        internal int CompareTo(Rational value) => (Numerator * value.Denominator).CompareTo(value.Numerator * Denominator);
        internal decimal ToQuantityCeiling()
        {
            var units = BigInteger.DivRem(Numerator * BigInteger.Pow(10, 12), Denominator, out var remainder);
            if (remainder > 0) units++;
            var result = FromMantissa(units, 12);
            if (result > SaleLine.MaximumQuantity) throw new OverflowException("Suggested quantity exceeds the operational numeric(28,12) range.");
            return result;
        }
        internal decimal ToDecimal()
        {
            if (Numerator > (BigInteger)decimal.MaxValue * Denominator)
                throw new OverflowException("Replenishment metric exceeds decimal precision.");
            // Only display metrics become finite decimals. Decisions and quantity ceiling above use
            // the exact rational, avoiding 1/7 * 7 boundary errors and intermediate overflow.
            for (var scale = 28; scale >= 0; scale--)
            {
                var units = BigInteger.DivRem(Numerator * BigInteger.Pow(10, scale), Denominator, out var remainder);
                if (remainder * 2 > Denominator || (remainder * 2 == Denominator && !units.IsEven)) units++;
                if (units <= (BigInteger)decimal.MaxValue) return FromMantissa(units, scale);
            }
            throw new OverflowException("Replenishment metric exceeds decimal precision.");
        }
        private static decimal FromMantissa(BigInteger units, int scale)
        {
            if (units < 0 || units > (BigInteger)decimal.MaxValue) throw new OverflowException("Replenishment quantity exceeds decimal precision.");
            return new((int)(uint)(units & uint.MaxValue), (int)(uint)((units >> 32) & uint.MaxValue),
                (int)(uint)((units >> 64) & uint.MaxValue), false, (byte)scale);
        }
    }
}
