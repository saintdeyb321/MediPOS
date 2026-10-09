using MediPOS.Application.Modules.Commissions.SetCommissionRule;
using MediPOS.Application.Modules.Commissions.SetTenantCommissionsEnabled;
using MediPOS.Application.Modules.SalesPos.ConfirmSale;
using MediPOS.Application.Modules.SalesPos.ReplaceSaleLines;
using MediPOS.Application.Modules.SalesPos.VoidSale;
using MediPOS.Domain.Modules.Commissions;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.Reporting;
using MediPOS.IntegrationTests.Modules.SalesPos;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.Commissions;

internal static class CommissionTestData
{
    internal static CancellationToken Token => TestContext.Current.CancellationToken;

    internal static async Task<Rows> CreateAsync(IServiceProvider source, TenantIsolationTestData.TenantRows tenant)
    {
        var checkout = await SaleCheckoutTestData.CreateAsync(source, tenant, 4m, medicine: true,
            receipts: [(1m, SaleCheckoutTestData.Today), (2m, SaleCheckoutTestData.Today.AddDays(1)), (10m, SaleCheckoutTestData.Today.AddDays(2))]);
        var context = source.GetRequiredService<MediPosDbContext>();
        var blister = await context.ProductUnits.Where(unit => unit.BusinessProductId == tenant.BusinessProductId && unit.Name == "Blíster")
            .Select(unit => unit.Id).SingleAsync(Token);
        await OwnerOverviewTestData.ReceiveRetailAsync(source, tenant, 50m);
        var draft = await source.GetRequiredService<ReplaceSaleLinesHandler>().HandleAsync(
            new(tenant.TenantId, tenant.Identity.BranchId, checkout.Draft.SaleId, checkout.Draft.Version,
                [new(checkout.ProductId, checkout.UnitId, 4m, PriceKind.Retail), new(tenant.BusinessProductId, blister, 2m, PriceKind.Retail)]), Token);
        checkout = checkout with { Draft = draft };
        var owner = (await OwnerOverviewTestData.AddOwnerAsync(source, tenant)).UserId;
        await source.GetRequiredService<SetTenantCommissionsEnabledHandler>().HandleAsync(new(tenant.TenantId, true), Token);
        var fixedRule = await source.GetRequiredService<SetCommissionRuleHandler>().HandleAsync(
            new(tenant.TenantId, checkout.ProductId, CommissionRuleType.Fixed, .2001m, IdentityAccessTestSetup.Now.AddDays(-1), null), Token);
        var percentageRule = await source.GetRequiredService<SetCommissionRuleHandler>().HandleAsync(
            new(tenant.TenantId, tenant.BusinessProductId, CommissionRuleType.Percentage, 12.3456m, IdentityAccessTestSetup.Now.AddDays(-1), null), Token);
        var balances = await context.InventoryLots.AsNoTracking().ToDictionaryAsync(lot => lot.Id, lot => lot.QuantityAvailableBase, Token);
        return new(checkout, owner, fixedRule.Id, percentageRule.Id, balances);
    }

    internal static Task<ConfirmSaleResult> ConfirmAsync(IServiceProvider source, Rows rows)
    {
        CashSessionTestData.Authenticate(source, rows.Checkout.Tenant.Identity.UserId);
        return source.GetRequiredService<ConfirmSaleHandler>().HandleAsync(SaleCheckoutTestData.Command(rows.Checkout,
            [new(PaymentMethod.Cash, 1.1111m), new(PaymentMethod.Yape, 2.2222m), new(PaymentMethod.Card, rows.Checkout.Draft.TotalAmount - 3.3333m)]), Token);
    }

    internal static Task<VoidSaleResult> VoidAsync(IServiceProvider source, Rows rows, ConfirmSaleResult confirmed)
    {
        CashSessionTestData.Authenticate(source, rows.OwnerId);
        return source.GetRequiredService<VoidSaleHandler>().HandleAsync(
            new(rows.Checkout.Tenant.TenantId, rows.Checkout.Tenant.Identity.BranchId, confirmed.SaleId, confirmed.Version, "Anulación con comisión"), Token);
    }

    internal sealed record Rows(SaleCheckoutTestData.Rows Checkout, Guid OwnerId, Guid FixedRuleId, Guid PercentageRuleId,
        IReadOnlyDictionary<Guid, decimal> OriginalBalances);
}
