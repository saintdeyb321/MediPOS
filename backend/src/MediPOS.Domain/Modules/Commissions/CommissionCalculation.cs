using MediPOS.Domain.Modules.SalesPos;
using MediPOS.SharedKernel;

namespace MediPOS.Domain.Modules.Commissions;

public static class CommissionCalculation
{
    public static decimal Calculate(SaleLine line, CommissionRule rule)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(rule);
        rule.Validate();
        if (line.TenantId != rule.TenantId || line.BusinessProductId != rule.BusinessProductId)
            throw new ArgumentException("Commission rule must belong to the sale line's tenant and product.");
        return CalculateSnapshot(line, rule.RuleType, rule.Value);
    }

    internal static decimal CalculateSnapshot(SaleLine line, CommissionRuleType type, decimal value)
    {
        if (!CommissionRule.IsValidValue(type, value) || line.Id == Guid.Empty || line.TenantId == Guid.Empty || line.SaleId == Guid.Empty ||
            line.BusinessProductId == Guid.Empty || line.BaseQuantity <= 0 || line.BaseQuantity > SaleLine.MaximumQuantity ||
            decimal.Round(line.BaseQuantity, 12) != line.BaseQuantity || line.LineTotal < 0 || line.LineTotal > ExactMoney.MaximumAmount ||
            decimal.Round(line.LineTotal, 4) != line.LineTotal)
            throw new ArgumentException("Commission calculation requires valid rule and exact sale line snapshots.");
        return type switch
        {
            CommissionRuleType.Fixed => ExactMoney.MultiplyAndRoundToEven4(line.BaseQuantity, value),
            CommissionRuleType.Percentage => ExactMoney.MultiplyAndRoundToEven4(line.LineTotal, value, 2),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }
}
