using System.Text.Json.Serialization;
using MediPOS.Application.Modules.Commissions;
using MediPOS.Application.Modules.Commissions.DeactivateCommissionRule;
using MediPOS.Application.Modules.Commissions.GetCommissionRules;
using MediPOS.Application.Modules.Commissions.SetCommissionRule;
using MediPOS.Application.Modules.Commissions.SetTenantCommissionsEnabled;
using MediPOS.Application.Modules.Reporting.GetOwnerCommissionsReport;
using MediPOS.Domain.Modules.Commissions;

namespace MediPOS.Api.Tenant;

public static partial class TenantEndpoints
{
    private static void MapCommissions(RouteGroupBuilder tenants)
    {
        tenants.MapGet("/commissions/settings", async (HttpContext c, GetTenantCommissionSettingsHandler handler) => TenantHttp.Response(await handler.HandleAsync(TenantHttp.Tenant(c), c.RequestAborted)))
            .Produces<TenantCommissionConfiguration>().WithName("tenant.commissions.settings");
        tenants.MapPut("/commissions/settings", async (HttpContext c, SetTenantCommissionsEnabledHandler handler) =>
        {
            var input = await TenantHttp.Read<CommissionSettingsRequest>(c);
            return TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), input.IsEnabled), c.RequestAborted));
        }).Accepts<CommissionSettingsRequest>("application/json").Produces<TenantCommissionSettingsDetails>().WithName("tenant.commissions.set_enabled");
        tenants.MapGet("/commissions/rules", async (HttpContext c, GetCommissionRulesHandler handler) =>
            TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.OptionalId(c, "productId"), TenantHttp.Flag(c, "includeInactive", true), TenantHttp.Offset(c), TenantHttp.Limit(c)), c.RequestAborted)))
            .Produces<IReadOnlyList<CommissionRuleDetails>>().WithName("tenant.commissions.rules").WithQuery<TenantOpenApi.CommissionRules>();
        tenants.MapPut("/products/{productId}/commission-rule", async (HttpContext c, SetCommissionRuleHandler handler) =>
        {
            var input = await TenantHttp.Read<CommissionRuleRequest>(c);
            return TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Id(c, "productId"), input.RuleType, input.Value, input.ValidFrom, input.ValidUntil), c.RequestAborted));
        }).Accepts<CommissionRuleRequest>("application/json").Produces<CommissionRuleDetails>().WithName("tenant.commissions.set_rule");
        tenants.MapPost("/commissions/rules/{ruleId}/deactivate", async (HttpContext c, DeactivateCommissionRuleHandler handler) =>
        {
            await TenantHttp.Read<EmptyRequest>(c);
            return TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Id(c, "ruleId")), c.RequestAborted));
        }).Accepts<EmptyRequest>("application/json").Produces<CommissionRuleDetails>().WithName("tenant.commissions.deactivate_rule");
        tenants.MapGet("/reports/commissions", async (HttpContext c, GetOwnerCommissionsReportHandler handler) =>
            TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.OptionalId(c, "branchId"), TenantHttp.OptionalId(c, "sellerMembershipId"), TenantHttp.OptionalId(c, "productId"), TenantHttp.Date(c, "from"), TenantHttp.Date(c, "to"), TenantHttp.Offset(c), TenantHttp.Limit(c)), c.RequestAborted)))
            .Produces<OwnerCommissionsReport>().WithName("tenant.commissions.report").WithQuery<TenantOpenApi.CommissionReport>();
    }
    public sealed record CommissionSettingsRequest([property: JsonRequired] bool IsEnabled);
    public sealed record CommissionRuleRequest([property: JsonRequired] CommissionRuleType RuleType, [property: JsonRequired] decimal Value, [property: JsonRequired] DateTimeOffset ValidFrom, DateTimeOffset? ValidUntil = null);
}
