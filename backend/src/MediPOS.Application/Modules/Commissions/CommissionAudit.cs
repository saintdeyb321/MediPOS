using System.Text.Json;
using MediPOS.Domain.Modules.Commissions;

namespace MediPOS.Application.Modules.Commissions;

internal static class CommissionAudit
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    internal static string Settings(TenantCommissionSettings settings) => JsonSerializer.Serialize(new { settings.IsEnabled }, Options);
    internal static string Rule(CommissionRule rule) => JsonSerializer.Serialize(new
    {
        rule.BusinessProductId,
        ruleType = CommissionRuleTypeCodes.ToCode(rule.RuleType),
        rule.Value,
        rule.IsActive,
        rule.ValidFrom,
        rule.ValidUntil,
    }, Options);
}
