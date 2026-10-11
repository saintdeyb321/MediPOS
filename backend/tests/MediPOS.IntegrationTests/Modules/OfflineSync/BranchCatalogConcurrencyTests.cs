using MediPOS.Application.Modules.Branches.CreateBranch;
using MediPOS.Application.Modules.Transfers;
using MediPOS.Application.Modules.Transfers.ApproveTransfer;
using MediPOS.Application.Modules.Transfers.DispatchTransfer;
using MediPOS.Application.Modules.Transfers.ReceiveTransfer;
using MediPOS.Application.Modules.Transfers.RequestTransfer;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.Reporting;
using MediPOS.IntegrationTests.Modules.SalesPos;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static MediPOS.IntegrationTests.Modules.OfflineSync.BranchCatalogTestData;

namespace MediPOS.IntegrationTests.Modules.OfflineSync;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class BranchCatalogConcurrencyTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task TransactionThatStartsFirstAndCommitsLastCannotBeSkippedByAnAdvancedCursor()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OfflineSyncBatchTestData.CreateServices(fixture); await using var seed = services.CreateAsyncScope();
        var rows = await CreateAsync(seed.ServiceProvider, pair.A); var secondProduct = await AddProduct(seed.ServiceProvider, rows);
        var baseline = await Snapshot(seed.ServiceProvider, rows, size: 1);
        while (baseline.HasMore) baseline = await Snapshot(seed.ServiceProvider, rows, baseline.NextCursor, 1);
        await using var slow = fixture.CreateContext(pair.A.TenantId); await using var fast = fixture.CreateContext(pair.A.TenantId);
        await using var firstTransaction = await slow.Database.BeginTransactionAsync(Token);
        await slow.Database.ExecuteSqlInterpolatedAsync($"UPDATE business_products SET retail_price = 7 WHERE tenant_id = {pair.A.TenantId} AND id = {pair.A.BusinessProductId}", Token);
        await using (var secondTransaction = await fast.Database.BeginTransactionAsync(Token))
        {
            await fast.Database.ExecuteSqlInterpolatedAsync($"UPDATE business_products SET retail_price = 9 WHERE tenant_id = {pair.A.TenantId} AND id = {secondProduct}", Token);
            // Timeout makes the FK KEY SHARE / counter NO KEY UPDATE compatibility a real deadlock check.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token); timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await secondTransaction.CommitAsync(timeout.Token);
        }
        var early = await Changes(seed.ServiceProvider, rows, baseline.NextCursor, 1); Assert.Equal(secondProduct, Assert.Single(early.Items).BusinessProductId);
        await firstTransaction.CommitAsync(Token);
        var late = await Changes(seed.ServiceProvider, rows, early.NextCursor, 1); Assert.Equal(pair.A.BusinessProductId, Assert.Single(late.Items).BusinessProductId);
        Assert.Equal(early.Items[0].Version + 1, late.Items[0].Version); Assert.Equal(7m, late.Items[0].Product!.RetailPrice);
        Assert.Empty((await Changes(seed.ServiceProvider, rows, late.NextCursor, 1)).Items);
    }

    [Fact]
    public async Task SnapshotInvalidatesOnConcurrentInsertWhileDeltaContinuationsKeepTheirImmutableWindow()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OfflineSyncBatchTestData.CreateServices(fixture); await using var seed = services.CreateAsyncScope();
        var rows = await CreateAsync(seed.ServiceProvider, pair.A); await AddProduct(seed.ServiceProvider, rows);
        var initial = await Snapshot(seed.ServiceProvider, rows, size: 1);
        var checkpoint = initial;
        while (checkpoint.HasMore) checkpoint = await Snapshot(seed.ServiceProvider, rows, checkpoint.NextCursor, 1);
        await using (var writer = services.CreateAsyncScope()) await AddProduct(writer.ServiceProvider, rows, "NEW-CONCURRENT");
        Assert.True((await Snapshot(seed.ServiceProvider, rows, initial.NextCursor, 1)).RequiresFullResync);
        var first = await Changes(seed.ServiceProvider, rows, checkpoint.NextCursor, 1); Assert.True(first.HasMore);
        await using (var writer = services.CreateAsyncScope()) await AddProduct(writer.ServiceProvider, rows, "LATER-CONCURRENT");
        var next = await Changes(seed.ServiceProvider, rows, first.NextCursor, 1); var replay = await Changes(seed.ServiceProvider, rows, first.NextCursor, 1);
        Assert.Equal(next.NextCursor, replay.NextCursor); Assert.Equal(next.Items.Select(item => item.Version), replay.Items.Select(item => item.Version));
        Assert.False(next.HasMore);
        var later = await Changes(seed.ServiceProvider, rows, next.NextCursor, 1);
        Assert.True(later.HasMore); Assert.Single(later.Items);
        var laterEnd = await Changes(seed.ServiceProvider, rows, later.NextCursor, 1);
        Assert.False(laterEnd.HasMore); Assert.Single(laterEnd.Items);
    }

    [Fact]
    public async Task ThreeBranchesTwoDevicesSalesPurchasesTransferInverseCommitsOfflineAcceptanceAndLimaRolloverDoNotLoseUpdates()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); var clock = new OwnerOverviewTestData.Clock();
        await using var services = OfflineSyncBatchTestData.CreateServices(fixture, clock: clock, processSales: true); await using var seed = services.CreateAsyncScope(); var source = seed.ServiceProvider;
        var sale = await OfflineSalePostingTestData.CreateAsync(source, pair.A, lots: [(6m, SaleCheckoutTestData.Today)]);
        var rows = await CreateAsync(source, pair.A, third: true);
        var downloading = await Snapshot(source, rows, size: 1); Assert.True(downloading.HasMore);
        var secondDevice = await Snapshot(source, rows, device: rows.SecondDevice); var third = await Snapshot(source, rows, device: rows.ThirdDevice);
        await SaleCheckoutTestData.ReceiveAsync(source, pair.A, sale.Checkout.ProductId, sale.Checkout.UnitId, [(2m, SaleCheckoutTestData.Today)]);
        await SaleCheckoutTestData.ConfirmAsync(source, sale.Checkout);
        Assert.Equal("accepted", Assert.Single((await OfflineSalePostingTestData.Run(source, sale, OfflineSalePostingTestData.Envelope(sale))).Results).Status);
        CashSessionTestData.Authenticate(source, rows.Tenant.Identity.UserId);
        var request = await source.GetRequiredService<RequestTransferHandler>().HandleAsync(new(pair.A.TenantId, pair.A.Identity.BranchId, pair.A.SpareBranchId,
            [new(sale.Checkout.ProductId, sale.Checkout.UnitId, 2m)]), Token);
        await source.GetRequiredService<ApproveTransferHandler>().HandleAsync(new(pair.A.TenantId, request.Id), Token);
        var context = source.GetRequiredService<MediPosDbContext>();
        var lot = await context.InventoryLots.AsNoTracking().Where(value => value.BusinessProductId == sale.Checkout.ProductId && value.BranchId == pair.A.Identity.BranchId && value.QuantityAvailableBase >= 2m)
            .OrderBy(value => value.Id).Select(value => value.Id).FirstAsync(Token);
        var dispatched = await source.GetRequiredService<DispatchTransferHandler>().HandleAsync(new(pair.A.TenantId, request.Id,
            [new(request.Lines[0].Id, lot, 2m)]), Token);
        await source.GetRequiredService<ReceiveTransferHandler>().HandleAsync(new(pair.A.TenantId, request.Id,
            [new(dispatched.Allocations[0].Id, 2m)]), Token);
        Assert.Empty((await Changes(source, rows, third.NextCursor, device: rows.ThirdDevice)).Items); // Stock only affects main and destination.
        await using (var slow = fixture.CreateContext(pair.A.TenantId))
        await using (var fast = fixture.CreateContext(pair.A.TenantId))
        {
            await using var before = await slow.Database.BeginTransactionAsync(Token);
            await slow.Database.ExecuteSqlInterpolatedAsync($"UPDATE business_products SET retail_price = 3 WHERE id = {pair.A.BusinessProductId}", Token);
            await using (var after = await fast.Database.BeginTransactionAsync(Token))
            {
                await fast.Database.ExecuteSqlInterpolatedAsync($"UPDATE business_products SET retail_price = 2.2 WHERE id = {sale.Checkout.ProductId}", Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token); timeout.CancelAfter(TimeSpan.FromSeconds(15)); await after.CommitAsync(timeout.Token);
            }
            await before.CommitAsync(Token);
        }
        Assert.True((await Snapshot(source, rows, downloading.NextCursor, 1)).RequiresFullResync);
        var deltas = await Changes(source, rows, secondDevice.NextCursor, device: rows.SecondDevice);
        var medicine = deltas.Items.Last(item => item.BusinessProductId == sale.Checkout.ProductId); Assert.Equal(4m, medicine.Stock!.PhysicalStockBase);
        Assert.Equal(2.2m, medicine.Product!.RetailPrice); Assert.Equal(3m, deltas.Items.Last(item => item.BusinessProductId == pair.A.BusinessProductId).Product!.RetailPrice);
        for (var index = 1; index < deltas.Items.Count; index++) Assert.Equal(deltas.Items[index - 1].Version + 1, deltas.Items[index].Version);
        var fresh = await Snapshot(source, rows); Assert.Equal(4m, Assert.Single(fresh.Items, item => item.BusinessProductId == sale.Checkout.ProductId).Stock!.SellableStockBase);
        Assert.Equal(2m, Assert.Single((await Snapshot(source, rows, device: rows.OtherBranchDevice)).Items, item => item.BusinessProductId == sale.Checkout.ProductId).Stock!.PhysicalStockBase);
        Assert.Equal(0m, Assert.Single((await Snapshot(source, rows, device: rows.ThirdDevice)).Items, item => item.BusinessProductId == sale.Checkout.ProductId).Stock!.PhysicalStockBase);
        clock.Now = new(2026, 10, 7, 5, 0, 0, TimeSpan.Zero);
        Assert.True((await Changes(source, rows, fresh.NextCursor)).RequiresFullResync);
        Assert.True((await Changes(source, rows, deltas.NextCursor, device: rows.SecondDevice)).RequiresFullResync);
        var tomorrow = await Snapshot(source, rows); var expired = Assert.Single(tomorrow.Items, item => item.BusinessProductId == sale.Checkout.ProductId).Stock!;
        Assert.Equal(4m, expired.PhysicalStockBase); Assert.Equal(0m, expired.SellableStockBase); Assert.Equal(4m, expired.ExpiredStockBase);
        await using var otherTenant = fixture.CreateContext(pair.B.TenantId); Assert.Empty(await otherTenant.Database.SqlQuery<Guid>($"SELECT entity_id AS \"Value\" FROM branch_catalog_changes WHERE entity_id = {sale.Checkout.ProductId}").ToArrayAsync(Token));
    }
}
