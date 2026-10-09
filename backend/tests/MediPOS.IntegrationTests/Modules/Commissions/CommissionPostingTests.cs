using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Commissions.DeactivateCommissionRule;
using MediPOS.Application.Modules.Commissions.SetCommissionRule;
using MediPOS.Application.Modules.Commissions.SetTenantCommissionsEnabled;
using MediPOS.Application.Modules.IdentityAccess.DeactivateMembership;
using MediPOS.Application.Modules.Reporting.GetOwnerCommissionsReport;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Commissions;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.Reporting;
using MediPOS.IntegrationTests.Modules.SalesPos;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.Commissions;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class CommissionPostingTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task MixedPaymentMultiProductMultiLotSaleAndVoidPreserveSellerSnapshotsAndFullyCompensateHistory()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        var clock = new OwnerOverviewTestData.Clock();
        await using var services = OwnerOverviewTestData.CreateServices(fixture, clock);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var rows = await CommissionTestData.CreateAsync(source, tenant);
        var confirmed = await CommissionTestData.ConfirmAsync(source, rows);
        await using (var verify = fixture.CreateContext(tenant.TenantId))
        {
            var sale = await verify.Sales.AsNoTracking().SingleAsync(sale => sale.Id == confirmed.SaleId, CommissionTestData.Token);
            Assert.Equal(2, sale.CommissionEntryCount);
            var earned = await verify.CommissionEntries.AsNoTracking().OrderBy(entry => entry.BusinessProductId).ToArrayAsync(CommissionTestData.Token);
            Assert.Equal(2, earned.Length);
            Assert.All(earned, entry =>
            {
                Assert.Equal(CommissionEntryType.Earned, entry.EntryType);
                Assert.Equal(tenant.Identity.MembershipId, entry.SellerMembershipId);
                Assert.Equal(confirmed.ConfirmedAt, entry.OccurredAt);
                Assert.Null(entry.ReversesCommissionEntryId);
            });
            Assert.Equal(.8004m, Assert.Single(earned, entry => entry.BusinessProductId == rows.Checkout.ProductId).Amount);
            Assert.Equal(5.2469m, Assert.Single(earned, entry => entry.BusinessProductId == tenant.BusinessProductId).Amount);
            Assert.Equal(3, await verify.SalePayments.CountAsync(CommissionTestData.Token));
            Assert.Equal(4, await verify.StockMovements.CountAsync(movement => movement.MovementType == StockMovementType.Sale, CommissionTestData.Token));
        }
        CashSessionTestData.Authenticate(source, rows.OwnerId);
        var reportQuery = new GetOwnerCommissionsReportQuery(tenant.TenantId, null, null, null, SaleCheckoutTestData.Today, SaleCheckoutTestData.Today, 0, 1);
        var report = await source.GetRequiredService<GetOwnerCommissionsReportHandler>().HandleAsync(reportQuery, CommissionTestData.Token);
        Assert.Single(report.Rows);
        Assert.Equal(new CommissionReportTotals(2, 6.0473m, 0m, 6.0473m), report.Totals);
        Assert.Equal(tenant.Identity.MembershipId, report.Rows[0].SellerMembershipId);
        clock.Now = IdentityAccessTestSetup.Now.AddDays(2);
        await source.GetRequiredService<SetCommissionRuleHandler>().HandleAsync(
            new(tenant.TenantId, rows.Checkout.ProductId, CommissionRuleType.Fixed, 9m, clock.Now, null), CommissionTestData.Token);
        await source.GetRequiredService<DeactivateCommissionRuleHandler>().HandleAsync(new(tenant.TenantId, rows.PercentageRuleId), CommissionTestData.Token);
        await source.GetRequiredService<SetTenantCommissionsEnabledHandler>().HandleAsync(new(tenant.TenantId, false), CommissionTestData.Token);
        await source.GetRequiredService<DeactivateMembershipHandler>().HandleAsync(
            new(tenant.TenantId, tenant.Identity.MembershipId, rows.OwnerId), CommissionTestData.Token);
        var preserved = await source.GetRequiredService<GetOwnerCommissionsReportHandler>().HandleAsync(reportQuery, CommissionTestData.Token);
        Assert.Equal(report.Totals, preserved.Totals);
        Assert.Equal(tenant.Identity.MembershipId, Assert.Single(preserved.Rows).SellerMembershipId);
        await CommissionTestData.VoidAsync(source, rows, confirmed);
        var after = await source.GetRequiredService<GetOwnerCommissionsReportHandler>().HandleAsync(reportQuery, CommissionTestData.Token);
        Assert.Equal(new CommissionReportTotals(2, 6.0473m, 6.0473m, 0m), after.Totals);
        Assert.All(after.Rows, entry => Assert.Equal(0m, entry.CurrentNetAmount));
        await using var final = fixture.CreateContext(tenant.TenantId);
        Assert.Equal(SaleStatus.Voided, await final.Sales.Where(sale => sale.Id == confirmed.SaleId).Select(sale => sale.Status).SingleAsync(CommissionTestData.Token));
        var entries = await final.CommissionEntries.AsNoTracking().ToArrayAsync(CommissionTestData.Token);
        var originals = entries.Where(entry => entry.EntryType == CommissionEntryType.Earned).ToArray();
        Assert.Equal(2, originals.Length);
        Assert.Equal(2, entries.Count(entry => entry.EntryType == CommissionEntryType.Reversal));
        foreach (var original in originals)
        {
            var reversal = Assert.Single(entries, entry => entry.ReversesCommissionEntryId == original.Id);
            Assert.Equal(-original.Amount, reversal.Amount);
            Assert.Equal(original.SellerMembershipId, reversal.SellerMembershipId);
            Assert.Equal(original.CommissionRuleId, reversal.CommissionRuleId);
            Assert.Equal(original.RuleTypeSnapshot, reversal.RuleTypeSnapshot);
            Assert.Equal(original.RuleValueSnapshot, reversal.RuleValueSnapshot);
            Assert.Equal(clock.Now, reversal.OccurredAt);
        }
        Assert.Contains(originals, entry => entry.CommissionRuleId == rows.FixedRuleId && entry.RuleValueSnapshot == .2001m);
        Assert.Contains(originals, entry => entry.CommissionRuleId == rows.PercentageRuleId && entry.RuleValueSnapshot == 12.3456m);
        Assert.Equal(3, await final.SalePayments.CountAsync(CommissionTestData.Token));
        Assert.Equal(3, await final.SalePaymentReversals.CountAsync(CommissionTestData.Token));
        Assert.Equal(4, await final.StockMovements.CountAsync(movement => movement.MovementType == StockMovementType.SaleReversal, CommissionTestData.Token));
        foreach (var lot in await final.InventoryLots.AsNoTracking().ToArrayAsync(CommissionTestData.Token))
        {
            Assert.Equal(rows.OriginalBalances[lot.Id], lot.QuantityAvailableBase);
            Assert.Equal(lot.QuantityAvailableBase, await final.StockMovements.Where(movement => movement.InventoryLotId == lot.Id)
                .SumAsync(movement => movement.QuantityDeltaBase, CommissionTestData.Token));
        }
        foreach (var action in new[] { AuditAction.SaleConfirmed, AuditAction.SaleVoided })
        {
            var audit = await final.AuditLogs.SingleAsync(audit => audit.EntityId == confirmed.SaleId && audit.Action == action, CommissionTestData.Token);
            using var json = JsonDocument.Parse(audit.AfterJson!);
            Assert.Equal(2, json.RootElement.GetProperty("commissionEntryCount").GetInt32());
        }
        var repeat = await Assert.ThrowsAsync<ApplicationErrorException>(() => CommissionTestData.VoidAsync(source, rows, confirmed));
        Assert.Equal(SalesPosErrors.AlreadyVoided, repeat.Error);
        Assert.Equal(4, await final.CommissionEntries.CountAsync(CommissionTestData.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisabledOrEnabledWithoutApplicableRuleProducesNoFictitiousCommission(bool enabled)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = OwnerOverviewTestData.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var checkout = await SaleCheckoutTestData.CreateAsync(source, tenant);
        var owner = (await OwnerOverviewTestData.AddOwnerAsync(source, tenant)).UserId;
        if (enabled) await source.GetRequiredService<SetTenantCommissionsEnabledHandler>().HandleAsync(new(tenant.TenantId, true), CommissionTestData.Token);
        var confirmed = await SaleCheckoutTestData.ConfirmAsync(source, checkout);
        await using var verify = fixture.CreateContext(tenant.TenantId);
        Assert.Empty(await verify.CommissionEntries.ToArrayAsync(CommissionTestData.Token));
        Assert.Equal(0, await verify.Sales.Where(sale => sale.Id == confirmed.SaleId).Select(sale => sale.CommissionEntryCount).SingleAsync(CommissionTestData.Token));
        CashSessionTestData.Authenticate(source, owner);
        await source.GetRequiredService<MediPOS.Application.Modules.SalesPos.VoidSale.VoidSaleHandler>().HandleAsync(
            new(tenant.TenantId, tenant.Identity.BranchId, confirmed.SaleId, confirmed.Version, "Sin comisión"), CommissionTestData.Token);
        Assert.Empty(await verify.CommissionEntries.ToArrayAsync(CommissionTestData.Token));
    }
}
