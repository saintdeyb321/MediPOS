using MediPOS.Application.Modules.Catalog.CreateCategory;
using MediPOS.Application.Modules.Catalog.UpdateBusinessProductPrices;
using MediPOS.Application.Modules.Reporting.Operational;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.CreateSaleDraft;
using MediPOS.Application.Modules.SalesPos.ReplaceSaleLines;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.Reporting;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class FilteredSalesAggregationTests(PostgreSqlFixture fixture)
{
    [Theory]
    [InlineData(OwnerSalesDimension.Branch)]
    [InlineData(OwnerSalesDimension.Employee)]
    public async Task FilteredAmountsAndDistinctCountsMatchAcrossProductsCategoriesBranchesSellersAndPages(OwnerSalesDimension dimension)
    {
        var (tenant, foreignTenant) = await TenantIsolationTestData.CreatePairAsync(fixture); var clock = new OwnerOverviewTestData.Clock();
        await using var services = OwnerOverviewTestData.CreateServices(fixture, clock); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var data = await OperationalReportTestData.SeedAsync(source, tenant, clock);
        var context = source.GetRequiredService<MediPosDbContext>();
        var categoryA = await source.GetRequiredService<CreateCategoryHandler>().HandleAsync(new("Categoría A " + Guid.NewGuid().ToString("N")), OperationalReportTestData.Token);
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE business_products SET category_id = {categoryA.Id} WHERE id = {data.MedicineA}", OperationalReportTestData.Token);
        var baseA = await OwnerOverviewTestData.BaseUnitAsync(source, data.MedicineA);
        var boxA = await context.ProductUnits.Where(unit => unit.BusinessProductId == data.MedicineA && !unit.IsBaseUnit).Select(unit => unit.Id).SingleAsync(OperationalReportTestData.Token);
        var baseB = await OwnerOverviewTestData.BaseUnitAsync(source, data.MedicineB);
        // Two presentations of A contribute 2 + 6; B contributes 5. This sale must count once.
        var repeated = await ConfirmAsync(source, tenant.TenantId, tenant.Identity.BranchId,
            [new(data.MedicineA, baseA, 1m, PriceKind.Retail), new(data.MedicineA, boxA, 1m, PriceKind.Retail), new(data.MedicineB, baseB, 1m, PriceKind.Retail)]);
        Assert.Equal(13m, repeated.TotalAmount);
        var secondOwner = await AddSellerAsync(source, tenant, tenant.SpareBranchId);
        var spare = await ConfirmAsync(source, tenant.TenantId, tenant.SpareBranchId, [new(data.MedicineA, baseA, 1m, PriceKind.Retail)]);
        Assert.Equal(2m, spare.TotalAmount);
        await OwnerOverviewTestData.SelectOwnerAsync(source, tenant.TenantId, data.Owner.UserId);

        await using (var foreignServices = OwnerOverviewTestData.CreateServices(fixture))
        await using (var foreignScope = foreignServices.CreateAsyncScope())
        {
            var foreignSource = foreignScope.ServiceProvider; var owner = await OwnerOverviewTestData.AddOwnerAsync(foreignSource, foreignTenant);
            await CashSessionTestData.OpenAsync(foreignSource, owner); await OwnerOverviewTestData.ReceiveRetailAsync(foreignSource, foreignTenant);
            var draft = await OwnerOverviewTestData.DraftAsync(foreignSource, foreignTenant, owner.BranchId);
            await OwnerOverviewTestData.ConfirmAsync(foreignSource, foreignTenant.TenantId, owner.BranchId, draft, mixed: true);
        }

        var query = new GetOwnerSalesReportQuery(tenant.TenantId, null, Period(), dimension);
        async Task<OwnerSalesReport> ReadAsync(GetOwnerSalesReportQuery request) =>
            await source.GetRequiredService<GetOwnerSalesReportHandler>().HandleAsync(request, OperationalReportTestData.Token);
        var whole = await ReadAsync(query);
        Assert.Equal(new SalesReportTotals(2, 3, 24m), whole.Totals); Assert.Equal("whole_sale_headers", whole.AmountBasis);
        Assert.Equal(24m, whole.Rows.Sum(row => row.SalesAmount));
        foreach (var request in new[] { query with { BusinessProductId = data.MedicineA }, query with { CategoryId = categoryA.Id },
            query with { BusinessProductId = data.MedicineA, CategoryId = categoryA.Id } })
        {
            var report = await ReadAsync(request);
            Assert.Equal(new SalesReportTotals(2, 3, 14m), report.Totals); Assert.Equal("matching_sale_lines", report.AmountBasis);
            Assert.Equal(14m, report.Rows.Sum(row => row.SalesAmount));
            var mainId = dimension == OwnerSalesDimension.Branch ? tenant.Identity.BranchId : data.Owner.MembershipId;
            Assert.Equal(12m, Assert.Single(report.Rows, row => row.GroupId == mainId).SalesAmount);
            Assert.Equal(2, Assert.Single(report.Rows, row => row.GroupId == mainId).SaleCount);
            var page = await ReadAsync(request with { Offset = 1, Limit = 1 });
            Assert.Equal(report.Totals, page.Totals); Assert.Equal(2m, Assert.Single(page.Rows).SalesAmount);
        }
        var matching = query with { BusinessProductId = data.MedicineA, CategoryId = categoryA.Id };
        var main = await ReadAsync(matching with { BranchId = tenant.Identity.BranchId, EmployeeMembershipId = data.Owner.MembershipId });
        Assert.Equal(new SalesReportTotals(1, 2, 12m), main.Totals);
        Assert.Equal(2m, (await ReadAsync(matching with { EmployeeMembershipId = secondOwner.MembershipId })).Totals.SalesAmount);
        Assert.Equal(10m, (await ReadAsync(query with { CategoryId = tenant.CategoryId })).Totals.SalesAmount);
        foreach (var invalid in new[] { matching with { CategoryId = tenant.CategoryId }, matching with { BranchId = tenant.SpareBranchId, EmployeeMembershipId = data.Owner.MembershipId },
            query with { BusinessProductId = foreignTenant.BusinessProductId } })
        {
            var empty = await ReadAsync(invalid); Assert.Empty(empty.Rows); Assert.Equal(new SalesReportTotals(0, 0, 0m), empty.Totals);
        }
        Assert.Equal(0, await context.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM sale_lines WHERE tenant_id = {foreignTenant.TenantId}").SingleAsync(OperationalReportTestData.Token));
    }

    [Theory]
    [InlineData(OwnerSalesDimension.Branch)]
    [InlineData(OwnerSalesDimension.Employee)]
    public async Task ZeroPricedMatchingLinesHaveZeroAmountAndOneDistinctSale(OwnerSalesDimension dimension)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture); var clock = new OwnerOverviewTestData.Clock();
        await using var services = OwnerOverviewTestData.CreateServices(fixture, clock); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var data = await OperationalReportTestData.SeedAsync(source, tenant, clock);
        await source.GetRequiredService<UpdateBusinessProductPricesHandler>().HandleAsync(new(tenant.TenantId, data.MedicineA, 0m, null, data.Owner.UserId), OperationalReportTestData.Token);
        var seller = await AddSellerAsync(source, tenant, tenant.Identity.BranchId);
        var baseA = await OwnerOverviewTestData.BaseUnitAsync(source, data.MedicineA); var baseB = await OwnerOverviewTestData.BaseUnitAsync(source, data.MedicineB);
        await ConfirmAsync(source, tenant.TenantId, tenant.Identity.BranchId, [new(data.MedicineA, baseA, 1m, PriceKind.Retail), new(data.MedicineB, baseB, 1m, PriceKind.Retail)]);
        var handler = source.GetRequiredService<GetOwnerSalesReportHandler>();
        var query = new GetOwnerSalesReportQuery(tenant.TenantId, tenant.Identity.BranchId, Period(), dimension, seller.MembershipId, data.MedicineA);
        var result = await handler.HandleAsync(query, OperationalReportTestData.Token);
        Assert.Equal(new SalesReportTotals(1, 1, 0m), result.Totals); Assert.Equal(0m, Assert.Single(result.Rows).SalesAmount);
        Assert.Equal(1, Assert.Single(result.Rows).SaleCount); Assert.Equal("matching_sale_lines", result.AmountBasis);
        Assert.Equal(5m, (await handler.HandleAsync(query with { BusinessProductId = null }, OperationalReportTestData.Token)).Totals.SalesAmount);
    }

    private static OperationalPeriodInput Period() => new(OperationalPeriodType.Custom, OperationalReportTestData.Today.AddDays(-1), OperationalReportTestData.Today);
    private static async Task<IdentityAccessTestSetup.Setup> AddSellerAsync(IServiceProvider source, TenantIsolationTestData.TenantRows tenant, Guid branch)
    {
        var user = await CashSessionTestData.AddOwnerAsync(source, tenant.Identity);
        var membership = await source.GetRequiredService<MediPosDbContext>().Memberships.Where(member => member.UserId == user).Select(member => member.Id).SingleAsync(OperationalReportTestData.Token);
        var identity = tenant.Identity with { UserId = user, MembershipId = membership, BranchId = branch };
        await CashSessionTestData.OpenAsync(source, identity);
        return identity;
    }
    private static async Task<SaleDraftDetails> ConfirmAsync(IServiceProvider source, Guid tenant, Guid branch, IReadOnlyList<SaleLineInput> lines)
    {
        var draft = await source.GetRequiredService<CreateSaleDraftHandler>().HandleAsync(new(tenant, branch), OperationalReportTestData.Token);
        var changed = await source.GetRequiredService<ReplaceSaleLinesHandler>().HandleAsync(new(tenant, branch, draft.SaleId, draft.Version, lines), OperationalReportTestData.Token);
        await OwnerOverviewTestData.ConfirmAsync(source, tenant, branch, changed, mixed: true);
        return changed;
    }
}
