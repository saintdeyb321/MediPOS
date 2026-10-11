using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.Catalog.CreateLocalBusinessProduct;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Application.Modules.Inventory.SetBranchProductStockThreshold;
using MediPOS.Application.Modules.Purchasing;
using MediPOS.Application.Modules.Purchasing.ConfirmPurchase;
using MediPOS.Application.Modules.Purchasing.CreatePurchase;
using MediPOS.Application.Modules.Purchasing.ReplacePurchaseLines;
using MediPOS.Application.Modules.Reporting.Operational;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.CreateSaleDraft;
using MediPOS.Application.Modules.SalesPos.ReplaceSaleLines;
using MediPOS.Application.Modules.SalesPos.VoidSale;
using MediPOS.Application.Modules.Transfers.ApproveTransfer;
using MediPOS.Application.Modules.Transfers.DispatchTransfer;
using MediPOS.Application.Modules.Transfers.ReceiveTransfer;
using MediPOS.Application.Modules.Transfers.RequestTransfer;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.Reporting;

internal static class OperationalReportTestData
{
    internal static DateOnly Today => new(2026, 10, 7);
    internal static CancellationToken Token => TestContext.Current.CancellationToken;
    internal static OperationalPeriodInput SalePeriod => new(OperationalPeriodType.Day, Today.AddDays(-1));

    internal static async Task<Scenario> SeedAsync(IServiceProvider source, TenantIsolationTestData.TenantRows tenant, OwnerOverviewTestData.Clock clock)
    {
        var owner = await OwnerOverviewTestData.AddOwnerAsync(source, tenant);
        await CashSessionTestData.OpenAsync(source, owner, 1000m);
        await CashSessionTestData.OpenAsync(source, owner with { BranchId = tenant.SpareBranchId }, 500m);
        var medicineA = await ProductAsync(source, tenant, "MED-A", ProductType.Medicine, 2m);
        var medicineB = await ProductAsync(source, tenant, "MED-B", ProductType.Medicine, 5m);
        var empty = await ProductAsync(source, tenant, "EMPTY", ProductType.Retail, 1m);
        await source.GetRequiredService<ReplaceProductUnitsHandler>().HandleAsync(new(tenant.TenantId, medicineA,
            [new("Base", 1m, true), new("Caja de 3", 3m, false)], owner.UserId), Token);
        await source.GetRequiredService<ReplaceProductUnitsHandler>().HandleAsync(new(tenant.TenantId, medicineB,
            [new("Base", 1m, true), new("Caja de 10", 10m, false)], owner.UserId), Token);
        var context = source.GetRequiredService<MediPosDbContext>();
        var unitsA = await context.ProductUnits.Where(unit => unit.BusinessProductId == medicineA).ToArrayAsync(Token);
        var unitsB = await context.ProductUnits.Where(unit => unit.BusinessProductId == medicineB).ToArrayAsync(Token);
        var baseA = unitsA.Single(unit => unit.IsBaseUnit).Id; var boxA = unitsA.Single(unit => !unit.IsBaseUnit).Id;
        var baseB = unitsB.Single(unit => unit.IsBaseUnit).Id; var boxB = unitsB.Single(unit => !unit.IsBaseUnit).Id;
        var retailUnit = await OwnerOverviewTestData.BaseUnitAsync(source, tenant.BusinessProductId);
        var purchase = await source.GetRequiredService<CreatePurchaseHandler>().HandleAsync(new(tenant.TenantId, owner.BranchId, null, null, owner.UserId), Token);
        var lines = await source.GetRequiredService<ReplacePurchaseLinesHandler>().HandleAsync(new(tenant.TenantId, purchase.Id,
            [new(medicineA, boxA, 3m, 1m, "A-GOOD", Today.AddDays(15)), new(medicineA, boxA, 1m, 1m, "A-EXPIRED", Today.AddDays(-2)),
             new(medicineA, baseA, 1m, .5m, "A-30", Today.AddDays(30)), new(medicineB, boxB, 1m, 7m, "B-TODAY", Today),
             new(medicineB, baseB, 1m, 1m, "B-LATER", Today.AddDays(31)), new(tenant.BusinessProductId, retailUnit, 5m, 2m, null, Today.AddDays(-3))], owner.UserId), Token);
        await source.GetRequiredService<ConfirmPurchaseHandler>().HandleAsync(new(tenant.TenantId, purchase.Id, owner.UserId), Token);
        var original = await context.InventoryLots.AsNoTracking().SingleAsync(lot => lot.SourcePurchaseLineId == lines[0].Id, Token);
        var transfer = await source.GetRequiredService<RequestTransferHandler>().HandleAsync(new(tenant.TenantId, owner.BranchId, tenant.SpareBranchId,
            [new(medicineA, baseA, 3m)]), Token);
        await source.GetRequiredService<ApproveTransferHandler>().HandleAsync(new(tenant.TenantId, transfer.Id), Token);
        var dispatched = await source.GetRequiredService<DispatchTransferHandler>().HandleAsync(new(tenant.TenantId, transfer.Id,
            [new(transfer.Lines[0].Id, original.Id, 3m)]), Token);
        var allocation = Assert.Single(dispatched.Allocations);
        await source.GetRequiredService<ReceiveTransferHandler>().HandleAsync(new(tenant.TenantId, transfer.Id, [new(allocation.Id, 3m)]), Token);
        var mixedDraft = await DraftAsync(source, tenant.TenantId, owner.BranchId, [new(medicineA, baseA, 2m, PriceKind.Retail), new(medicineB, baseB, 1m, PriceKind.Retail)]);
        var mixed = await OwnerOverviewTestData.ConfirmAsync(source, tenant.TenantId, owner.BranchId, mixedDraft, mixed: true);
        var voidDraft = await DraftAsync(source, tenant.TenantId, owner.BranchId, [new(medicineA, baseA, 1m, PriceKind.Retail)]);
        var voided = await OwnerOverviewTestData.ConfirmAsync(source, tenant.TenantId, owner.BranchId, voidDraft);
        await DraftAsync(source, tenant.TenantId, owner.BranchId, [new(medicineA, baseA, 1m, PriceKind.Retail)]);
        clock.Now = new(2026, 10, 7, 14, 0, 0, TimeSpan.Zero);
        await source.GetRequiredService<VoidSaleHandler>().HandleAsync(new(tenant.TenantId, owner.BranchId, voided.SaleId, voided.Version, "Anulación al día siguiente"), Token);
        foreach (var setting in new[] { (owner.BranchId, medicineA, 5m), (tenant.SpareBranchId, medicineA, 3m), (owner.BranchId, empty, 0m) })
            await source.GetRequiredService<SetBranchProductStockThresholdHandler>().HandleAsync(new(tenant.TenantId, setting.Item1, setting.Item2, setting.Item3), Token);
        return new(tenant, owner, medicineA, medicineB, empty, mixed.SaleId, mixed.Version, original.Id, lines[0].Id);
    }

    internal static Task<OwnerOperationalDashboard> DashboardAsync(IServiceProvider source, Guid tenant, Guid? branch = null, OperationalPeriodInput? period = null) =>
        source.GetRequiredService<GetOwnerOperationalDashboardHandler>().HandleAsync(new(tenant, branch, period ?? SalePeriod), Token);
    internal static Task<OwnerSalesReport> SalesAsync(IServiceProvider source, Guid tenant, OwnerSalesDimension dimension, Guid? branch = null, int offset = 0, int limit = 100) =>
        source.GetRequiredService<GetOwnerSalesReportHandler>().HandleAsync(new(tenant, branch, SalePeriod, dimension, Offset: offset, Limit: limit), Token);
    private static async Task<Guid> ProductAsync(IServiceProvider source, TenantIsolationTestData.TenantRows tenant, string sku, ProductType type, decimal price) =>
        (await source.GetRequiredService<CreateLocalBusinessProductHandler>().HandleAsync(new(tenant.TenantId, sku, type, sku, tenant.CategoryId, "Lab", null,
            type == ProductType.Medicine ? new MedicineInput([new("Paracetamol", "500 mg")], "Tableta", "Oral") : null,
            price, null, tenant.Identity.ActorId), Token)).Id;
    private static async Task<SaleDraftDetails> DraftAsync(IServiceProvider source, Guid tenant, Guid branch, IReadOnlyList<SaleLineInput> lines)
    {
        var draft = await source.GetRequiredService<CreateSaleDraftHandler>().HandleAsync(new(tenant, branch), Token);
        return await source.GetRequiredService<ReplaceSaleLinesHandler>().HandleAsync(new(tenant, branch, draft.SaleId, draft.Version, lines), Token);
    }
    internal sealed record Scenario(TenantIsolationTestData.TenantRows Tenant, IdentityAccessTestSetup.Setup Owner, Guid MedicineA, Guid MedicineB,
        Guid EmptyProduct, Guid MixedSale, uint MixedVersion, Guid OriginalLot, Guid OriginalPurchaseLine);
}
