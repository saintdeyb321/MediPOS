using MediPOS.Application.Modules.Catalog.CreateLocalBusinessProduct;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Application.Modules.Catalog.UpdateBusinessProductPrices;
using MediPOS.Application.Modules.Inventory.SetBranchProductStockThreshold;
using MediPOS.Application.Modules.Reporting.Operational;
using MediPOS.Application.Modules.Reporting.Replenishment;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.CreateSaleDraft;
using MediPOS.Application.Modules.SalesPos.ReplaceSaleLines;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.SalesPos;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.Reporting;

internal static class ReplenishmentTestData
{
    internal static CancellationToken Token => TestContext.Current.CancellationToken;
    internal static async Task<Scenario> SeedAsync(IServiceProvider source, TenantIsolationTestData.TenantRows tenant, OwnerOverviewTestData.Clock clock)
    {
        clock.Now = IdentityAccessTestSetup.Now;
        var original = await OperationalReportTestData.SeedAsync(source, tenant, clock);
        var context = source.GetRequiredService<MediPosDbContext>();
        var package = await context.ProductUnits.Where(u => u.BusinessProductId == original.MedicineA && !u.IsBaseUnit).Select(u => u.Id).SingleAsync(Token);
        var baseUnit = await OwnerOverviewTestData.BaseUnitAsync(source, original.MedicineA);
        clock.Now = IdentityAccessTestSetup.Now.AddHours(1);
        await SellAsync(source, tenant.TenantId, tenant.Identity.BranchId, original.MedicineA, package, .5m);
        await SellAsync(source, tenant.TenantId, tenant.SpareBranchId, original.MedicineA, baseUnit, .5m);
        var noSales = await ProductAsync(source, tenant, "NO-CONSUMPTION"); var exhausted = await ProductAsync(source, tenant, "EXHAUSTED");
        var exhaustedUnit = await OwnerOverviewTestData.BaseUnitAsync(source, exhausted);
        await SaleCheckoutTestData.ReceiveAsync(source, tenant, exhausted, exhaustedUnit, [(1.25m, null)]);
        await SellAsync(source, tenant.TenantId, tenant.Identity.BranchId, exhausted, exhaustedUnit, 1.25m);
        await source.GetRequiredService<ReplaceProductUnitsHandler>().HandleAsync(new(tenant.TenantId, original.MedicineA,
            [new("Base actual", 1m, true), new("Caja actual", 10m, false)], original.Owner.UserId), Token);
        clock.Now = new(2026, 10, 7, 14, 0, 0, TimeSpan.Zero);
        await source.GetRequiredService<SetBranchProductStockThresholdHandler>().HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId, noSales, 8m), Token);
        var retailUnit = await OwnerOverviewTestData.BaseUnitAsync(source, tenant.BusinessProductId);
        await source.GetRequiredService<UpdateBusinessProductPricesHandler>().HandleAsync(new(tenant.TenantId, tenant.BusinessProductId, 1m, null, original.Owner.UserId), Token);
        await SellAsync(source, tenant.TenantId, tenant.Identity.BranchId, tenant.BusinessProductId, retailUnit, .5m); // Today is excluded from historical consumption.
        return new(original, noSales, exhausted);
    }
    internal static ReplenishmentReadRequest Request(Guid tenant, Guid? branch = null, bool onlyRisk = false, int offset = 0, int limit = 100) =>
        new(new(tenant, branch, OperationalReportTestData.Today), OperationalReportPolicy.Resolve(new(OperationalPeriodType.Custom,
            OperationalReportTestData.Today.AddDays(-30), OperationalReportTestData.Today.AddDays(-1)), new(2026, 10, 7, 14, 0, 0, TimeSpan.Zero)), 30, 7, 14, onlyRisk, offset, limit);
    internal static Task<OwnerReplenishmentSuggestions> ReadAsync(IServiceProvider source, Guid tenant, Guid? branch = null,
        bool onlyRisk = false, int offset = 0, int limit = 100) => source.GetRequiredService<GetOwnerReplenishmentSuggestionsHandler>()
        .HandleAsync(new(tenant, branch, OnlyAtRisk: onlyRisk, Offset: offset, Limit: limit), Token);
    internal static async Task SellAsync(IServiceProvider source, Guid tenant, Guid branch, Guid product, Guid unit, decimal quantity)
    {
        var draft = await source.GetRequiredService<CreateSaleDraftHandler>().HandleAsync(new(tenant, branch), Token);
        var ready = await source.GetRequiredService<ReplaceSaleLinesHandler>().HandleAsync(new(tenant, branch, draft.SaleId, draft.Version,
            [new(product, unit, quantity, PriceKind.Retail)]), Token);
        await OwnerOverviewTestData.ConfirmAsync(source, tenant, branch, ready);
    }
    private static async Task<Guid> ProductAsync(IServiceProvider source, TenantIsolationTestData.TenantRows tenant, string sku)
    {
        var product = await source.GetRequiredService<CreateLocalBusinessProductHandler>().HandleAsync(new(tenant.TenantId, sku, ProductType.Retail, sku,
            tenant.CategoryId, "Marca", null, null, 1m, null, tenant.Identity.ActorId), Token);
        await source.GetRequiredService<ReplaceProductUnitsHandler>().HandleAsync(new(tenant.TenantId, product.Id, [new("Base", 1m, true)], tenant.Identity.ActorId), Token);
        return product.Id;
    }
    internal sealed record Scenario(OperationalReportTestData.Scenario Original, Guid NoSalesProduct, Guid ExhaustedProduct);
}
