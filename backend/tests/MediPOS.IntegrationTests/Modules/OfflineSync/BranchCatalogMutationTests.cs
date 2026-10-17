using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Application.Modules.Catalog.SetBusinessProductStatus;
using MediPOS.Application.Modules.Catalog.UpdateBusinessProductPrices;
using MediPOS.Application.Modules.Inventory.AdjustStock;
using MediPOS.Application.Modules.SalesPos.VoidSale;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.SalesPos;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static MediPOS.IntegrationTests.Modules.OfflineSync.BranchCatalogTestData;

namespace MediPOS.IntegrationTests.Modules.OfflineSync;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class BranchCatalogMutationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task CreationPricesUnitReplacementAndLogicalTombstonesArePublishedAndReplayedInOrder()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OfflineSyncBatchTestData.CreateServices(fixture, processSales: true); await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider; var rows = await CreateAsync(source, pair.A); var baseline = await Snapshot(source, rows);
        var product = await AddProduct(source, rows);
        var created = await Changes(source, rows, baseline.NextCursor); Assert.Contains(created.Items, item => item.BusinessProductId == product && item.Product!.Units.Count == 2);
        await source.GetRequiredService<UpdateBusinessProductPricesHandler>().HandleAsync(new(rows.Tenant.TenantId, product, 3.4567m, null, rows.Tenant.Identity.UserId), Token);
        await source.GetRequiredService<VersionedProductUnitsFixture>().HandleAsync(new(rows.Tenant.TenantId, product, [new("Nueva base", 1m, true)], rows.Tenant.Identity.UserId), Token);
        await source.GetRequiredService<SetBusinessProductStatusHandler>().HandleAsync(new(rows.Tenant.TenantId, product, false, rows.Tenant.Identity.UserId), Token);
        var first = await Changes(source, rows, created.NextCursor, 100); var repeat = await Changes(source, rows, created.NextCursor, 100);
        Assert.Equal(first.NextCursor, repeat.NextCursor);
        var entries = first.Items.Where(item => item.BusinessProductId == product).ToArray(); Assert.Equal(3, entries.Length);
        Assert.Equal(3.4567m, entries[0].Product!.RetailPrice);
        var originalUnits = created.Items.Last(item => item.BusinessProductId == product).Product!.Units;
        Assert.Equal(originalUnits.Select(unit => (unit.ProductUnitId, unit.ConversionToBase)).OrderBy(unit => unit.ProductUnitId),
            entries[1].Product!.Units.Select(unit => (unit.ProductUnitId, unit.ConversionToBase)).OrderBy(unit => unit.ProductUnitId));
        Assert.Equal("Nueva base", Assert.Single(entries[1].Product!.Units, unit => unit.IsBaseUnit).Name);
        Assert.Equal("tombstone", entries[2].Operation); Assert.Null(entries[2].Product);
        Assert.Contains((await Snapshot(source, rows)).Items, item => item.BusinessProductId == product && !item.Product!.IsActive);
        await source.GetRequiredService<SetBusinessProductStatusHandler>().HandleAsync(new(rows.Tenant.TenantId, product, true, rows.Tenant.Identity.UserId), Token);
        Assert.Equal("upsert", Assert.Single((await Changes(source, rows, first.NextCursor)).Items).Operation);
    }

    [Fact]
    public async Task PurchaseOnlineSaleVoidAdjustmentAndOfflineSaleAllPublishTheirRealBalances()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OfflineSyncBatchTestData.CreateServices(fixture, processSales: true); await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider; var sale = await OfflineSalePostingTestData.CreateAsync(source, pair.A, lots: [(5m, SaleCheckoutTestData.Today)]);
        var rows = await CreateAsync(source, pair.A); var baseline = await Snapshot(source, rows);
        var product = sale.Checkout.ProductId;
        var confirmed = await SaleCheckoutTestData.ConfirmAsync(source, sale.Checkout);
        var sold = await Changes(source, rows, baseline.NextCursor); Assert.Equal(4m, Assert.Single(sold.Items, item => item.BusinessProductId == product).Stock!.PhysicalStockBase);
        await source.GetRequiredService<VoidSaleHandler>().HandleAsync(new(pair.A.TenantId, pair.A.Identity.BranchId, confirmed.SaleId, confirmed.Version, "Prueba reversión"), Token);
        var reversed = await Changes(source, rows, sold.NextCursor); Assert.Equal(5m, Assert.Single(reversed.Items, item => item.BusinessProductId == product).Stock!.SellableStockBase);
        await source.GetRequiredService<AdjustStockHandler>().HandleAsync(new(pair.A.TenantId, sale.Checkout.LotIds[0], 1m, "Conteo", rows.Tenant.Identity.UserId), Token);
        var adjusted = await Changes(source, rows, reversed.NextCursor); Assert.Equal(6m, Assert.Single(adjusted.Items).Stock!.PhysicalStockBase);
        await SaleCheckoutTestData.ReceiveAsync(source, pair.A, product, sale.Checkout.UnitId, [(2m, SaleCheckoutTestData.Today.AddDays(1))]);
        var purchased = await Changes(source, rows, adjusted.NextCursor); Assert.Equal(8m, Assert.Single(purchased.Items).Stock!.PhysicalStockBase);
        var envelope = OfflineSalePostingTestData.Envelope(sale);
        Assert.Equal("accepted", Assert.Single((await OfflineSalePostingTestData.Run(source, sale, envelope)).Results).Status);
        var offline = await Changes(source, rows, purchased.NextCursor); Assert.Equal(7m, Assert.Single(offline.Items).Stock!.PhysicalStockBase);
        await OfflineSalePostingTestData.Run(source, sale, envelope);
        Assert.Empty((await Changes(source, rows, offline.NextCursor)).Items);
        var other = await Snapshot(source, rows, device: rows.OtherBranchDevice); Assert.Equal(0m, Assert.Single(other.Items, item => item.BusinessProductId == product).Stock!.PhysicalStockBase);
    }

    [Fact]
    public async Task SourceRollbackRestoresCounterLogAndSnapshotWithoutGhostChanges()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OfflineSyncBatchTestData.CreateServices(fixture); await using var scope = services.CreateAsyncScope();
        var rows = await CreateAsync(scope.ServiceProvider, pair.A); var snapshot = await Snapshot(scope.ServiceProvider, rows);
        await using (var write = fixture.CreateContext(pair.A.TenantId))
        {
            await using var transaction = await write.Database.BeginTransactionAsync(Token);
            await write.Database.ExecuteSqlInterpolatedAsync($"UPDATE business_products SET retail_price = 9 WHERE tenant_id = {pair.A.TenantId} AND id = {pair.A.BusinessProductId}", Token);
            await transaction.RollbackAsync(Token);
        }
        Assert.Empty((await Changes(scope.ServiceProvider, rows, snapshot.NextCursor)).Items);
        Assert.Equal(snapshot.Items[0].Product!.RetailPrice, (await Snapshot(scope.ServiceProvider, rows)).Items[0].Product!.RetailPrice);
    }
}
