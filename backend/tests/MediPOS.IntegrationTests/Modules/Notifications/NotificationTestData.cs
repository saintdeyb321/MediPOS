using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.Catalog.CreateLocalBusinessProduct;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Application.Modules.Notifications;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.CreateSaleDraft;
using MediPOS.Application.Modules.SalesPos.ReplaceSaleLines;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.Reporting;
using MediPOS.IntegrationTests.Modules.SalesPos;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.Notifications;

internal static class NotificationTestData
{
    internal static DateOnly Today => new(2026, 10, 6);
    internal static CancellationToken Token => TestContext.Current.CancellationToken;
    internal static async Task<Scenario> SeedAsync(IServiceProvider source, TenantIsolationTestData.TenantRows tenant)
    {
        var owner = await OwnerOverviewTestData.AddOwnerAsync(source, tenant);
        var product = await source.GetRequiredService<CreateLocalBusinessProductHandler>().HandleAsync(new(tenant.TenantId, "EXP-MED", ProductType.Medicine,
            "Paracetamol", tenant.CategoryId, "Lab", null, new MedicineInput([new("Paracetamol", "500 mg")], "Tableta", "Oral"), 2m, null, owner.UserId), Token);
        await source.GetRequiredService<VersionedProductUnitsFixture>().HandleAsync(new(tenant.TenantId, product.Id, [new("Base", 1m, true)], owner.UserId), Token);
        var unit = await OwnerOverviewTestData.BaseUnitAsync(source, product.Id);
        foreach (var branch in new[] { owner.BranchId, tenant.SpareBranchId })
            await SaleCheckoutTestData.ReceiveAsync(source, tenant with { Identity = tenant.Identity with { BranchId = branch } }, product.Id, unit,
                [(1m, Today), (1m, Today.AddDays(10)), (1m, Today.AddDays(30)), (1m, Today.AddDays(31)), (1m, Today.AddDays(-1))]);
        var retailUnit = await OwnerOverviewTestData.BaseUnitAsync(source, tenant.BusinessProductId);
        await SaleCheckoutTestData.ReceiveAsync(source, tenant, tenant.BusinessProductId, retailUnit, [(1m, Today.AddDays(10))]);
        var lot = await source.GetRequiredService<MediPosDbContext>().InventoryLots.AsNoTracking()
            .SingleAsync(l => l.BusinessProductId == product.Id && l.BranchId == owner.BranchId && l.ExpirationDate == Today, Token);
        return new(tenant, owner, product.Id, unit, lot.Id);
    }
    internal static Task<NotificationPage> ListAsync(IServiceProvider source, Guid tenant, Guid? branch = null, bool unread = false, int offset = 0, int limit = 100) =>
        source.GetRequiredService<GetNotificationsHandler>().HandleAsync(new(tenant, branch, unread, offset, limit), Token);
    internal static Task<long> UnreadAsync(IServiceProvider source, Guid tenant, Guid? branch = null) =>
        source.GetRequiredService<GetUnreadNotificationCountHandler>().HandleAsync(new(tenant, branch), Token);
    internal static Task<NotificationReadResult> MarkAsync(IServiceProvider source, Guid tenant, Guid notification) =>
        source.GetRequiredService<MarkNotificationReadHandler>().HandleAsync(new(tenant, notification), Token);
    internal static async Task ExhaustTodayAsync(IServiceProvider source, Scenario scenario)
    {
        CashSessionTestData.Authenticate(source, scenario.Owner.UserId);
        await CashSessionTestData.OpenAsync(source, scenario.Owner, 0m);
        var draft = await source.GetRequiredService<CreateSaleDraftHandler>().HandleAsync(new(scenario.Tenant.TenantId, scenario.Owner.BranchId), Token);
        var ready = await source.GetRequiredService<ReplaceSaleLinesHandler>().HandleAsync(new(scenario.Tenant.TenantId, scenario.Owner.BranchId,
            draft.SaleId, draft.Version, [new(scenario.ProductId, scenario.UnitId, 1m, PriceKind.Retail)]), Token);
        await OwnerOverviewTestData.ConfirmAsync(source, scenario.Tenant.TenantId, scenario.Owner.BranchId, ready);
    }
    internal sealed record Scenario(TenantIsolationTestData.TenantRows Tenant, IdentityAccessTestSetup.Setup Owner, Guid ProductId, Guid UnitId, Guid TodayLotId);
}
