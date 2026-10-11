using System.Text.Json;
using MediPOS.Application.Modules.Commissions.SetCommissionRule;
using MediPOS.Application.Modules.Commissions.SetTenantCommissionsEnabled;
using MediPOS.Application.Modules.OfflineSync;
using MediPOS.Domain.Modules.Commissions;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.Reporting;
using MediPOS.IntegrationTests.Modules.SalesPos;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.OfflineSync;

internal static class OfflineSalePostingTestData
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    internal static CancellationToken Token => TestContext.Current.CancellationToken;
    internal sealed record Rows(TenantIsolationTestData.TenantRows Tenant, SaleCheckoutTestData.Rows Checkout, Guid Device, Guid SecondDevice, Guid Owner);
    internal static async Task<Rows> CreateAsync(IServiceProvider source, TenantIsolationTestData.TenantRows tenant,
        bool medicine = true, IReadOnlyList<(decimal Quantity, DateOnly? Expiration)>? lots = null, bool commission = false)
    {
        var checkout = await SaleCheckoutTestData.CreateAsync(source, tenant, 1m, medicine, lots ?? [(1m, SaleCheckoutTestData.Today)]);
        var device = await OfflineSyncBatchTestData.Register(source, tenant);
        var second = await OfflineSyncBatchTestData.Register(source, tenant);
        var owner = (await OwnerOverviewTestData.AddOwnerAsync(source, tenant)).UserId;
        if (commission)
        {
            await source.GetRequiredService<SetTenantCommissionsEnabledHandler>().HandleAsync(new(tenant.TenantId, true), Token);
            await source.GetRequiredService<SetCommissionRuleHandler>().HandleAsync(new(tenant.TenantId, checkout.ProductId,
                CommissionRuleType.Percentage, 10m, IdentityAccess.IdentityAccessTestSetup.Now, null), Token);
        }
        CashSessionTestData.Authenticate(source, tenant.Identity.UserId);
        return new(tenant, checkout, device, second, owner);
    }
    internal static OfflineSalePayload Payload(Rows rows) => new(rows.Tenant.Identity.BranchId, rows.Checkout.Cash.CashSessionId,
        rows.Checkout.Draft.Lines.Select(line => new OfflineSaleLine(line.BusinessProductId, line.ProductUnitIdSnapshot,
            line.Quantity, line.ConversionToBaseSnapshot, line.UnitPriceSnapshot, PriceKindCodes.ToCode(line.PriceKind))).ToArray(),
        [new("cash", rows.Checkout.Draft.TotalAmount)], rows.Checkout.Draft.TotalAmount);
    internal static SyncEventEnvelope Envelope(Rows rows, Guid? device = null, Guid? id = null, OfflineSalePayload? payload = null) =>
        OfflineSyncBatchTestData.Envelope(device ?? rows.Device, id, JsonSerializer.Serialize(payload ?? Payload(rows), JsonOptions));
    internal static Task<OfflineSyncBatchResult> Run(IServiceProvider source, Rows rows, SyncEventEnvelope envelope) =>
        OfflineSyncBatchTestData.Run(source, rows.Tenant, envelope.DeviceId, [envelope]);
}
