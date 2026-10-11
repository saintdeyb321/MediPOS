using MediPOS.Application.Modules.Branches.CreateBranch;
using MediPOS.Application.Modules.Catalog.CreateLocalBusinessProduct;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Application.Modules.OfflineSync;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.Reporting;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.OfflineSync;

internal static class BranchCatalogTestData
{
    internal static CancellationToken Token => TestContext.Current.CancellationToken;
    internal sealed record Rows(TenantIsolationTestData.TenantRows Tenant, Guid Device, Guid SecondDevice, Guid OtherBranchDevice, Guid ThirdBranch, Guid ThirdDevice);
    internal static async Task<Rows> CreateAsync(IServiceProvider source, TenantIsolationTestData.TenantRows tenant, bool third = false)
    {
        var owner = await OwnerOverviewTestData.AddOwnerAsync(source, tenant);
        var owned = tenant with { Identity = owner };
        var device = await Register(source, owned, tenant.Identity.BranchId); var second = await Register(source, owned, tenant.Identity.BranchId);
        var other = await Register(source, owned, tenant.SpareBranchId);
        Guid thirdBranch = Guid.Empty; Guid thirdDevice = Guid.Empty;
        if (third)
        {
            thirdBranch = (await source.GetRequiredService<CreateBranchHandler>().HandleAsync(new(tenant.TenantId, tenant.LegalEntityId, "Tercera", owner.UserId), Token)).Id;
            thirdDevice = await Register(source, owned, thirdBranch);
        }
        return new(owned, device, second, other, thirdBranch, thirdDevice);
    }
    internal static async Task<Guid> Register(IServiceProvider source, TenantIsolationTestData.TenantRows tenant, Guid branch)
    {
        CashSessionTestData.Authenticate(source, tenant.Identity.UserId);
        return (await source.GetRequiredService<RegisterSyncDeviceHandler>().HandleAsync(new(tenant.TenantId, branch, "Catálogo"), Token)).DeviceId;
    }
    internal static Task<BranchCatalogPage> Snapshot(IServiceProvider source, Rows rows, string? cursor = null, int size = 100, Guid? device = null)
    {
        CashSessionTestData.Authenticate(source, rows.Tenant.Identity.UserId);
        return source.GetRequiredService<GetBranchCatalogSnapshotHandler>().HandleAsync(new(rows.Tenant.TenantId, device ?? rows.Device, cursor, size), Token);
    }
    internal static Task<BranchCatalogPage> Changes(IServiceProvider source, Rows rows, string? cursor, int size = 100, Guid? device = null)
    {
        CashSessionTestData.Authenticate(source, rows.Tenant.Identity.UserId);
        return source.GetRequiredService<GetBranchCatalogChangesHandler>().HandleAsync(new(rows.Tenant.TenantId, device ?? rows.Device, cursor, size), Token);
    }
    internal static async Task<Guid> AddProduct(IServiceProvider source, Rows rows, string code = "DELTA")
    {
        CashSessionTestData.Authenticate(source, rows.Tenant.Identity.UserId);
        var product = await source.GetRequiredService<CreateLocalBusinessProductHandler>().HandleAsync(new(rows.Tenant.TenantId, code,
            ProductType.Retail, "Nuevo", rows.Tenant.CategoryId, "Marca", null, null, 2m, 1m, rows.Tenant.Identity.UserId), Token);
        await source.GetRequiredService<ReplaceProductUnitsHandler>().HandleAsync(new(rows.Tenant.TenantId, product.Id,
            [new("Base", 1m, true), new("Caja", 10m, false)], rows.Tenant.Identity.UserId), Token);
        return product.Id;
    }
}
