using System.Net;
using MediPOS.Domain.Modules.Commissions;
using MediPOS.Domain.Modules.IdentityAccess;
using static MediPOS.UnitTests.Api.TenantApiHttpTests;

namespace MediPOS.UnitTests.Api;

[Collection("Observability")]
public sealed class TenantCommissionHttpTests
{
    [Fact]
    public async Task OwnerConfigurationSalePostingRuleReplacementVoidAndReportPreserveOriginalCommission()
    {
        var state = TenantCommercialContractTests.SeedStock(); await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host); var csrf = await host.Csrf(); var root = $"/api/tenants/{state.TenantId:D}"; var branch = root + $"/branches/{state.Branches[0].Id:D}";
        using var initial = await Json(host, "GET", root + "/commissions/settings"); Assert.False(initial.RootElement.GetProperty("isConfigured").GetBoolean()); Assert.Null(state.CommissionSettings); Assert.Empty(state.Audits);
        using var enabled = await Json(host, "PUT", root + "/commissions/settings", new { isEnabled = true }, csrf);
        using var rule = await Json(host, "PUT", root + $"/products/{state.Products[0].Id:D}/commission-rule", new { ruleType = "percentage", value = 10m, validFrom = DateTimeOffset.UtcNow.AddDays(-1) }, csrf);
        using var rules = await Json(host, "GET", root + "/commissions/rules?includeInactive=true&limit=10"); Assert.Single(rules.RootElement.EnumerateArray());
        using var cash = await Json(host, "POST", branch + "/cash-sessions", new { openingAmount = 0m }, csrf, 201);
        using var draft = await Json(host, "POST", branch + "/sales", new { }, csrf, 201); var id = draft.RootElement.GetProperty("saleId").GetGuid();
        using var cart = await Json(host, "PUT", branch + $"/sales/{id:D}/lines", new { expectedVersion = 1u, lines = new[] { new { businessProductId = state.Products[0].Id, productUnitId = state.Units[0].Id, quantity = 2m, priceKind = "retail" } } }, csrf);
        using var confirmed = await Json(host, "POST", branch + $"/sales/{id:D}/confirm", new { expectedVersion = cart.RootElement.GetProperty("version").GetUInt32(), payments = new[] { new { method = "cash", amount = 4m } } }, csrf);
        var earned = Assert.Single(state.CommissionEntries); Assert.Equal(.4m, earned.Amount); Assert.Equal(CommissionEntryType.Earned, earned.EntryType);
        using var replacement = await Json(host, "PUT", root + $"/products/{state.Products[0].Id:D}/commission-rule", new { ruleType = "percentage", value = 50m, validFrom = DateTimeOffset.UtcNow.AddDays(-1) }, csrf);
        using var voided = await Json(host, "POST", branch + $"/sales/{id:D}/void", new { expectedVersion = confirmed.RootElement.GetProperty("version").GetUInt32(), reason = "Corrección" }, csrf);
        var reversal = Assert.Single(state.CommissionEntries, entry => entry.EntryType == CommissionEntryType.Reversal); Assert.Equal(-earned.Amount, reversal.Amount); Assert.Equal(earned.Id, reversal.ReversesCommissionEntryId); Assert.Equal(2, state.CommissionEntries.Count);
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Lima")).DateTime);
        using var report = await Json(host, "GET", root + $"/reports/commissions?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}"); var totals = report.RootElement.GetProperty("totals");
        Assert.Equal(.4m, totals.GetProperty("originalEarnedAmount").GetDecimal()); Assert.Equal(.4m, totals.GetProperty("compensatedAmount").GetDecimal()); Assert.Equal(0m, totals.GetProperty("currentNetAmount").GetDecimal());
        using var deactivated = await Json(host, "POST", root + "/commissions/rules/" + replacement.RootElement.GetProperty("id").GetGuid().ToString("D") + "/deactivate", new { }, csrf); Assert.False(deactivated.RootElement.GetProperty("isActive").GetBoolean());
        using var disabled = await Json(host, "PUT", root + "/commissions/settings", new { isEnabled = false }, csrf); Assert.False(disabled.RootElement.GetProperty("isEnabled").GetBoolean()); Assert.Equal(2, state.CommissionEntries.Count);
    }

    [Theory]
    [InlineData(TenantRole.Cashier)]
    [InlineData(TenantRole.Pharmacist)]
    public async Task CommissionAdministrationAndReportRequireOwner(TenantRole role)
    {
        var state = TenantCommercialContractTests.SeedStock(); state.Member = Membership.Create(state.TenantId, state.User.Id, role, DateTimeOffset.UtcNow);
        await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host); var csrf = await host.Csrf(); var root = $"/api/tenants/{state.TenantId:D}";
        foreach (var path in new[] { "/commissions/settings", "/commissions/rules", "/reports/commissions?from=2026-10-14&to=2026-10-14" })
        { using var result = await Send(host, "GET", root + path); Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode); }
        using var mutation = await Send(host, "PUT", root + "/commissions/settings", new { isEnabled = true }, csrf); await Problem(mutation, 403, "commissions.forbidden"); Assert.Null(state.CommissionSettings); Assert.Empty(state.CommissionRules); Assert.Empty(state.Audits);
    }
}
