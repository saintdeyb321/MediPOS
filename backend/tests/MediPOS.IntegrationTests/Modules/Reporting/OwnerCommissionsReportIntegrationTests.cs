using System.Data.Common;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Reporting.GetOwnerCommissionsReport;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.Commissions;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.SalesPos;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.Reporting;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class OwnerCommissionsReportIntegrationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task FullTotalsRemainExactAcrossPagesAndBranchSellerProductAndRecognitionPeriodFilters()
    {
        var (tenant, foreign) = await TenantIsolationTestData.CreatePairAsync(fixture);
        var clock = new OwnerOverviewTestData.Clock();
        await using var services = OwnerOverviewTestData.CreateServices(fixture, clock);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var rows = await CommissionTestData.CreateAsync(source, tenant);
        var confirmed = await CommissionTestData.ConfirmAsync(source, rows);
        CashSessionTestData.Authenticate(source, rows.OwnerId);
        var handler = source.GetRequiredService<GetOwnerCommissionsReportHandler>();
        var query = Query(tenant.TenantId) with { Limit = 1 };
        var first = await handler.HandleAsync(query, CommissionTestData.Token);
        var second = await handler.HandleAsync(query with { Offset = 1 }, CommissionTestData.Token);
        Assert.Single(first.Rows);
        Assert.Single(second.Rows);
        Assert.NotEqual(first.Rows[0].CommissionEntryId, second.Rows[0].CommissionEntryId);
        Assert.Equal(new CommissionReportTotals(2, 6.0473m, 0m, 6.0473m), first.Totals);
        Assert.Equal(first.Totals, second.Totals);
        var beyond = await handler.HandleAsync(query with { Offset = 2 }, CommissionTestData.Token);
        Assert.Empty(beyond.Rows);
        Assert.Equal(first.Totals, beyond.Totals);
        var selected = await handler.HandleAsync(query with { BranchId = tenant.Identity.BranchId, SellerMembershipId = tenant.Identity.MembershipId, Limit = 50 }, CommissionTestData.Token);
        Assert.Equal(first.Totals, selected.Totals);
        Assert.All(selected.Rows, row =>
        {
            Assert.Equal(tenant.Identity.MembershipId, row.SellerMembershipId);
            Assert.Equal("Staff", row.SellerName);
            Assert.False(string.IsNullOrWhiteSpace(row.ProductName));
        });
        var product = await handler.HandleAsync(query with { BusinessProductId = rows.Checkout.ProductId }, CommissionTestData.Token);
        Assert.Equal(new CommissionReportTotals(1, .8004m, 0m, .8004m), product.Totals);
        Assert.Equal(rows.Checkout.ProductId, Assert.Single(product.Rows).BusinessProductId);
        foreach (var filter in new[] { query with { BranchId = tenant.SpareBranchId }, query with { SellerMembershipId = foreign.Identity.MembershipId },
                     query with { BusinessProductId = foreign.BusinessProductId } })
        {
            var empty = await handler.HandleAsync(filter, CommissionTestData.Token);
            Assert.Empty(empty.Rows);
            Assert.Equal(new CommissionReportTotals(0, 0m, 0m, 0m), empty.Totals);
        }
        clock.Now = IdentityAccessTestSetup.Now.AddDays(2);
        await CommissionTestData.VoidAsync(source, rows, confirmed);
        var originalPeriod = await handler.HandleAsync(query with { Limit = 50 }, CommissionTestData.Token);
        Assert.Equal(new CommissionReportTotals(2, 6.0473m, 6.0473m, 0m), originalPeriod.Totals);
        Assert.All(originalPeriod.Rows, row => Assert.Equal(clock.Now, row.ReversedAtUtc));
        var reversalDate = SaleCheckoutTestData.Today.AddDays(2);
        var later = await handler.HandleAsync(query with { FromLocalDate = reversalDate, ToLocalDate = reversalDate }, CommissionTestData.Token);
        Assert.Empty(later.Rows);
        Assert.Equal(new CommissionReportTotals(0, 0m, 0m, 0m), later.Totals);
    }

    [Theory]
    [InlineData(TenantRole.Cashier)]
    [InlineData(TenantRole.Pharmacist)]
    public async Task OperationalRolesCannotReadEmployeeCommissionAmounts(TenantRole role)
    {
        await using var services = OwnerOverviewTestData.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var identity = await CashSessionTestData.CreateAsync(source, role);
        CashSessionTestData.Authenticate(source, identity.UserId);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<GetOwnerCommissionsReportHandler>()
            .HandleAsync(Query(identity.TenantId) with { BranchId = identity.BranchId }, CommissionTestData.Token));
        Assert.Equal(CommissionReportErrors.Forbidden, error.Error);
    }

    [Fact]
    public async Task ReportHasTwoBoundedSqlSelectsAndOneReadOnlySnapshotDuringConcurrentCommittedVoid()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var writerServices = OwnerOverviewTestData.CreateServices(fixture);
        CommissionTestData.Rows rows;
        MediPOS.Application.Modules.SalesPos.ConfirmSale.ConfirmSaleResult confirmed;
        await using (var scope = writerServices.CreateAsyncScope())
        {
            rows = await CommissionTestData.CreateAsync(scope.ServiceProvider, tenant);
            confirmed = await CommissionTestData.ConfirmAsync(scope.ServiceProvider, rows);
        }
        var observer = new OwnerOverviewTestData.SqlObserver();
        await using var reportServices = OwnerOverviewTestData.CreateServices(fixture, observer: observer);
        await using var reportScope = reportServices.CreateAsyncScope();
        var source = reportScope.ServiceProvider;
        await OwnerOverviewTestData.SelectOwnerAsync(source, tenant.TenantId, rows.OwnerId);
        observer.Reset();
        var committed = false;
        observer.BeforeSecondSelect = async (active, token) =>
        {
            Assert.Equal("repeatable read", await SettingAsync(active, "SHOW transaction_isolation", token));
            Assert.Equal("on", await SettingAsync(active, "SHOW transaction_read_only", token));
            await using var scope = writerServices.CreateAsyncScope();
            await CommissionTestData.VoidAsync(scope.ServiceProvider, rows, confirmed);
            committed = true;
        };
        var request = new OwnerCommissionsReadRequest(tenant.TenantId, null, null, null,
            new(2026, 10, 6, 5, 0, 0, TimeSpan.Zero), new(2026, 10, 7, 5, 0, 0, TimeSpan.Zero), 0, 1);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(CommissionTestData.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var page = await source.GetRequiredService<IOwnerCommissionsReportReader>().ReadAsync(request, timeout.Token);
        Assert.True(committed);
        Assert.Equal(new CommissionReportTotals(2, 6.0473m, 0m, 6.0473m), page.Totals);
        Assert.True(Assert.Single(page.Rows).CurrentNetAmount > 0);
        Assert.Equal(2, observer.Selects.Count);
        Assert.Contains("GROUP BY", observer.Selects[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sum(", observer.Selects[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", observer.Selects[1], StringComparison.OrdinalIgnoreCase);
        Assert.All(observer.Selects, sql => Assert.DoesNotContain("FOR UPDATE", sql, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("SET TRANSACTION READ ONLY", Assert.Single(observer.Writes));
        Assert.Empty(source.GetRequiredService<MediPosDbContext>().ChangeTracker.Entries());
        observer.BeforeSecondSelect = null;
        observer.Reset();
        var fresh = await source.GetRequiredService<IOwnerCommissionsReportReader>().ReadAsync(request with { Offset = 1 }, CommissionTestData.Token);
        Assert.Equal(new CommissionReportTotals(2, 6.0473m, 6.0473m, 0m), fresh.Totals);
        Assert.Equal(0m, Assert.Single(fresh.Rows).CurrentNetAmount);
        Assert.Equal(2, observer.Selects.Count);
    }

    private static GetOwnerCommissionsReportQuery Query(Guid tenant) => new(tenant, null, null, null, SaleCheckoutTestData.Today, SaleCheckoutTestData.Today);
    private static async Task<string> SettingAsync(DbCommand active, string sql, CancellationToken token)
    {
        await using var command = active.Connection!.CreateCommand();
        command.Transaction = active.Transaction;
        command.CommandText = sql;
        return Assert.IsType<string>(await command.ExecuteScalarAsync(token));
    }
}
