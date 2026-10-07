using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.Catalog.CreateLocalBusinessProduct;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Application.Modules.Purchasing.ConfirmPurchase;
using MediPOS.Application.Modules.Purchasing.CreatePurchase;
using MediPOS.Application.Modules.Purchasing.CreateSupplier;
using MediPOS.Application.Modules.Purchasing.ReplacePurchaseLines;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.ConfirmSale;
using MediPOS.Application.Modules.SalesPos.ReplaceSaleLines;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Infrastructure;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.SalesPos;

internal static class SaleCheckoutTestData
{
    internal static readonly DateOnly Today = new(2026, 10, 6);

    internal static async Task<Rows> CreateAsync(IServiceProvider source, TenantIsolationTestData.TenantRows tenant,
        decimal quantity = 1m, bool medicine = false, IReadOnlyList<(decimal Quantity, DateOnly? Expiration)>? receipts = null)
    {
        var draft = await SaleDraftTestData.CreateAsync(source, tenant);
        var productId = tenant.BusinessProductId;
        if (medicine)
        {
            var product = await source.GetRequiredService<CreateLocalBusinessProductHandler>().HandleAsync(
                new(tenant.TenantId, "CHECKOUT-MED", ProductType.Medicine, "Medicamento", tenant.CategoryId, "Lab", null,
                    new MedicineInput([new("Paracetamol", "500 mg")], "Tableta", "Oral"), 2.125m, 1.75m, tenant.Identity.ActorId), TestContext.Current.CancellationToken);
            productId = product.Id;
            await source.GetRequiredService<ReplaceProductUnitsHandler>().HandleAsync(
                new(tenant.TenantId, productId, [new("Base", 1m, true), new("Caja", 10m, false)], tenant.Identity.ActorId), TestContext.Current.CancellationToken);
        }
        var context = source.GetRequiredService<MediPosDbContext>();
        var unitId = await context.ProductUnits.Where(unit => unit.BusinessProductId == productId && unit.IsBaseUnit)
            .Select(unit => unit.Id).SingleAsync(TestContext.Current.CancellationToken);
        var updated = await source.GetRequiredService<ReplaceSaleLinesHandler>().HandleAsync(
            new(tenant.TenantId, tenant.Identity.BranchId, draft.Draft.SaleId, draft.Draft.Version, [new(productId, unitId, quantity, PriceKind.Retail)]), TestContext.Current.CancellationToken);
        var lotIds = await ReceiveAsync(source, tenant, productId, unitId, receipts ?? [(3m, Today)]);
        return new(tenant, productId, unitId, draft.Cash, updated, lotIds);
    }

    internal static async Task<Guid[]> ReceiveAsync(IServiceProvider source, TenantIsolationTestData.TenantRows tenant,
        Guid productId, Guid unitId, IReadOnlyList<(decimal Quantity, DateOnly? Expiration)> receipts)
    {
        if (receipts.Count == 0) return [];
        var supplier = await source.GetRequiredService<CreateSupplierHandler>().HandleAsync(
            new(tenant.TenantId, "Proveedor", null, null, tenant.Identity.ActorId), TestContext.Current.CancellationToken);
        var purchase = await source.GetRequiredService<CreatePurchaseHandler>().HandleAsync(
            new(tenant.TenantId, tenant.Identity.BranchId, supplier.Id, null, tenant.Identity.ActorId), TestContext.Current.CancellationToken);
        var lines = await source.GetRequiredService<ReplacePurchaseLinesHandler>().HandleAsync(new(tenant.TenantId, purchase.Id,
            receipts.Select((receipt, index) => new PurchaseLineInput(productId, unitId, receipt.Quantity, 1m,
                "L-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), receipt.Expiration)).ToArray(), tenant.Identity.ActorId), TestContext.Current.CancellationToken);
        await source.GetRequiredService<ConfirmPurchaseHandler>().HandleAsync(new(tenant.TenantId, purchase.Id, tenant.Identity.ActorId), TestContext.Current.CancellationToken);
        var ids = lines.Select(line => line.Id).ToArray();
        return await source.GetRequiredService<MediPosDbContext>().InventoryLots.Where(lot => ids.Contains(lot.SourcePurchaseLineId))
            .OrderBy(lot => lot.ExpirationDate).ThenBy(lot => lot.CreatedAt).ThenBy(lot => lot.Id).Select(lot => lot.Id).ToArrayAsync(TestContext.Current.CancellationToken);
    }

    internal static ConfirmSaleCommand Command(Rows rows, IReadOnlyList<SalePaymentInput>? payments = null) =>
        new(rows.Tenant.TenantId, rows.Tenant.Identity.BranchId, rows.Draft.SaleId, rows.Draft.Version,
            payments ?? [new(PaymentMethod.Cash, rows.Draft.TotalAmount)]);

    internal static Task<ConfirmSaleResult> ConfirmAsync(IServiceProvider source, Rows rows)
    {
        CashSessionTestData.Authenticate(source, rows.Tenant.Identity.UserId);
        return source.GetRequiredService<ConfirmSaleHandler>().HandleAsync(Command(rows), TestContext.Current.CancellationToken);
    }

    internal static ServiceProvider CreateServices(PostgreSqlFixture fixture, string applicationName)
    {
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { ApplicationName = applicationName }.ConnectionString;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:MediPosDatabase"] = connection }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new IdentityAccessTestSetup.Clock());
        services.AddInfrastructure(configuration);
        services.AddIdentityAuthentication<IdentityAccessTestSetup.TestGoogleIdentitySource, IdentityAccessTestSetup.TestServerSession>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    internal sealed record Rows(TenantIsolationTestData.TenantRows Tenant, Guid ProductId, Guid UnitId,
        OpenCashSessionDetails Cash, SaleDraftDetails Draft, Guid[] LotIds);
}
