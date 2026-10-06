using System.Globalization;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.Catalog.CreateLocalBusinessProduct;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Application.Modules.Catalog.SearchProducts;
using MediPOS.Application.Modules.Purchasing.ConfirmPurchase;
using MediPOS.Application.Modules.Purchasing.CreatePurchase;
using MediPOS.Application.Modules.Purchasing.ReplacePurchaseLines;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.Catalog;

internal static class ProductSearchTestData
{
    internal static readonly DateOnly Today = new(2026, 10, 6);

    internal static MedicineInput Medicine(string strength = "500 mg", string form = "Tableta", string route = "Oral",
        string ingredient = "Paracetamol") => new([new(ingredient, strength)], form, route);

    internal static async Task<Product> CreateAsync(IServiceProvider source, TenantIsolationTestData.TenantRows tenant,
        string name, string? code = null, string? barcode = null, MedicineInput? medicine = null,
        string brand = "Laboratorio", bool isActive = true)
    {
        var product = await source.GetRequiredService<CreateLocalBusinessProductHandler>().HandleAsync(
            new(tenant.TenantId, code ?? Guid.NewGuid().ToString("N"), medicine is null ? ProductType.Retail : ProductType.Medicine,
                name, tenant.CategoryId, brand, barcode, medicine, 2.5m, 2m, tenant.Identity.ActorId, isActive),
            TestContext.Current.CancellationToken);
        var units = await source.GetRequiredService<ReplaceProductUnitsHandler>().HandleAsync(
            new(tenant.TenantId, product.Id, [new("Base", 1m, true), new("Caja", 10m, false), new("Inactiva", 2m, false, false)],
                tenant.Identity.ActorId), TestContext.Current.CancellationToken);
        return new(product.Id, units.Single(value => value.IsBaseUnit).Id);
    }

    internal static async Task ReceiveAsync(IServiceProvider source, TenantIsolationTestData.TenantRows tenant, Guid branch,
        params Receipt[] receipts)
    {
        var purchase = await source.GetRequiredService<CreatePurchaseHandler>().HandleAsync(
            new(tenant.TenantId, branch, null, Guid.NewGuid().ToString("N"), tenant.Identity.ActorId), TestContext.Current.CancellationToken);
        await source.GetRequiredService<ReplacePurchaseLinesHandler>().HandleAsync(
            new(tenant.TenantId, purchase.Id, receipts.Select(value =>
                new PurchaseLineInput(value.Product.Id, value.Product.BaseUnitId, value.Quantity, 19.125m, "L", value.Expiry)).ToArray(),
                tenant.Identity.ActorId), TestContext.Current.CancellationToken);
        await source.GetRequiredService<ConfirmPurchaseHandler>().HandleAsync(
            new(tenant.TenantId, purchase.Id, tenant.Identity.ActorId), TestContext.Current.CancellationToken);
    }

    internal static Task<SearchProductsResult> SearchAsync(IServiceProvider source, TenantIsolationTestData.TenantRows tenant,
        string query, int limit = 50, Guid? branch = null) =>
        source.GetRequiredService<SearchProductsHandler>().HandleAsync(
            new(tenant.TenantId, branch ?? tenant.Identity.BranchId, query, limit), TestContext.Current.CancellationToken);

    internal static async Task AddRepresentativeCatalogAsync(IServiceProvider source, TenantIsolationTestData.TenantRows tenant)
    {
        // Bulk fixture setup uses real domain objects, tenant enforcement and audit records, without inventing search behavior.
        var context = source.GetRequiredService<MediPosDbContext>();
        context.SelectTenant(tenant.TenantId);
        await using var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        for (var index = 0; index < 512; index++)
        {
            var suffix = index.ToString("D4", CultureInfo.InvariantCulture);
            var product = BusinessProduct.CreateLocal(tenant.TenantId, "DATA-" + suffix, ProductType.Retail,
                "Artículo de higiene " + suffix, tenant.CategoryId, "Marca comercial " + suffix, "DATA-BAR-" + suffix,
                null, 1m, null, IdentityAccessTestSetup.Now);
            context.BusinessProducts.Add(product);
            context.AuditLogs.Add(AuditTrail.Record(tenant.TenantId, tenant.Identity.ActorId, AuditAction.BusinessProductCreated,
                product.Id, IdentityAccessTestSetup.Now, null, CatalogAudit.Created(product)));
        }
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
    }

    internal sealed record Product(Guid Id, Guid BaseUnitId);
    internal sealed record Receipt(Product Product, decimal Quantity, DateOnly? Expiry);
}
