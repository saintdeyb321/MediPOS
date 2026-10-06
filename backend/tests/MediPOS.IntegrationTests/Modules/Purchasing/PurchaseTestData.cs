using MediPOS.Application.Modules.Purchasing.ConfirmPurchase;
using MediPOS.Application.Modules.Purchasing.CreatePurchase;
using MediPOS.Application.Modules.Purchasing.CreateSupplier;
using MediPOS.Application.Modules.Purchasing.ReplacePurchaseLines;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.Purchasing;

internal static class PurchaseTestData
{
    internal static async Task<Rows> CreateAsync(PostgreSqlFixture fixture, TenantIsolationTestData.TenantRows tenant,
        bool confirmed = false, string? reference = null)
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var supplier = await source.GetRequiredService<CreateSupplierHandler>().HandleAsync(
            new(tenant.TenantId, " Distribuidor ", " 123 ", " contacto ", tenant.Identity.ActorId), TestContext.Current.CancellationToken);
        var purchase = await source.GetRequiredService<CreatePurchaseHandler>().HandleAsync(
            new(tenant.TenantId, tenant.Identity.BranchId, supplier.Id, reference ?? Guid.NewGuid().ToString("N"), tenant.Identity.ActorId),
            TestContext.Current.CancellationToken);
        var context = source.GetRequiredService<MediPOS.Infrastructure.Persistence.MediPosDbContext>();
        var unit = await context.ProductUnits.SingleAsync(value => value.BusinessProductId == tenant.BusinessProductId && value.Name == "Blíster",
            TestContext.Current.CancellationToken);
        var lines = await source.GetRequiredService<ReplacePurchaseLinesHandler>().HandleAsync(new(tenant.TenantId, purchase.Id,
            [new(tenant.BusinessProductId, unit.Id, 2.5m, 0.1234567890123456789012345678m, null, null),
             new(tenant.BusinessProductId, unit.Id, 3.125m, 12.125m, "L-1", new(2020, 1, 1))], tenant.Identity.ActorId),
             TestContext.Current.CancellationToken);
        if (confirmed)
            await source.GetRequiredService<ConfirmPurchaseHandler>().HandleAsync(new(tenant.TenantId, purchase.Id, tenant.Identity.ActorId),
                TestContext.Current.CancellationToken);
        return new(tenant, supplier.Id, purchase.Id, lines.Select(value => value.Id).ToArray());
    }

    internal sealed record Rows(TenantIsolationTestData.TenantRows Tenant, Guid SupplierId, Guid PurchaseId, Guid[] LineIds)
    {
        internal Guid TenantId => Tenant.TenantId;
        internal Guid ActorId => Tenant.Identity.ActorId;
    }
}
