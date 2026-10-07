using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.Catalog.UpdateBusinessProductPrices;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.CreateSaleDraft;
using MediPOS.Application.Modules.SalesPos.ReplaceSaleLines;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.SalesPos;

internal static class SaleDraftTestData
{
    internal static async Task<Rows> CreateAsync(IServiceProvider source, TenantIsolationTestData.TenantRows tenant)
    {
        await source.GetRequiredService<UpdateBusinessProductPricesHandler>().HandleAsync(
            new(tenant.TenantId, tenant.BusinessProductId, 2.125m, 1.75m, tenant.Identity.ActorId), TestContext.Current.CancellationToken);
        var cash = await CashSessionTestData.OpenAsync(source, tenant.Identity);
        var draft = await source.GetRequiredService<CreateSaleDraftHandler>().HandleAsync(
            new(tenant.TenantId, tenant.Identity.BranchId), TestContext.Current.CancellationToken);
        var context = source.GetRequiredService<MediPosDbContext>();
        var unitId = await context.ProductUnits.Where(value => value.BusinessProductId == tenant.BusinessProductId && value.Name == "Blíster")
            .Select(value => value.Id).SingleAsync(TestContext.Current.CancellationToken);
        return new(tenant, unitId, cash, draft);
    }

    internal static ReplaceSaleLinesCommand Command(Rows rows, SaleDraftDetails? draft = null, decimal quantity = 2m, PriceKind kind = PriceKind.Retail) =>
        new(rows.Tenant.TenantId, rows.Tenant.Identity.BranchId, rows.Draft.SaleId, (draft ?? rows.Draft).Version,
            [new(rows.Tenant.BusinessProductId, rows.UnitId, quantity, kind)]);

    internal sealed record Rows(TenantIsolationTestData.TenantRows Tenant, Guid UnitId, OpenCashSessionDetails Cash, SaleDraftDetails Draft);
}
