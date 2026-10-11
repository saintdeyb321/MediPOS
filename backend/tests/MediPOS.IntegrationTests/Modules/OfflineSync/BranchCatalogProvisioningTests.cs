using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static MediPOS.IntegrationTests.Modules.OfflineSync.BranchCatalogTestData;

namespace MediPOS.IntegrationTests.Modules.OfflineSync;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class BranchCatalogProvisioningTests(PostgreSqlFixture fixture)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BranchProvisioningAndConcurrentCatalogMutationCannotLeaveARevisionZeroBranchMissingADelta(bool branchFirst)
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = OfflineSyncBatchTestData.CreateServices(fixture); await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider; var rows = await CreateAsync(source, pair.A); var newBranch = Guid.CreateVersion7();
        await using var provision = fixture.CreateContext(pair.A.TenantId); await using var price = fixture.CreateContext(pair.A.TenantId);
        await provision.Database.OpenConnectionAsync(Token); await price.Database.OpenConnectionAsync(Token);
        var provisionPid = await provision.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(Token);
        var pricePid = await price.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(Token);
        await using var provisionTransaction = await provision.Database.BeginTransactionAsync(Token);
        await using var priceTransaction = await price.Database.BeginTransactionAsync(Token);
        if (branchFirst)
        {
            await InsertBranch(provision);
            var writing = UpdatePrice(price); await WaitForTopologyLock(pricePid);
            await provisionTransaction.CommitAsync(Token); await writing;
            // The new branch is visible while the catalog writer still has not committed.
            var device = await Register(source, rows.Tenant, newBranch);
            var baseline = await Snapshot(source, rows, device: device);
            Assert.Equal(0m, Assert.Single(baseline.Items).Product!.RetailPrice);
            await priceTransaction.CommitAsync(Token);
            Assert.Equal(6m, Assert.Single((await Changes(source, rows, baseline.NextCursor, device: device)).Items).Product!.RetailPrice);
        }
        else
        {
            await UpdatePrice(price);
            var inserting = InsertBranch(provision); await WaitForTopologyLock(provisionPid);
            await priceTransaction.CommitAsync(Token); await inserting; await provisionTransaction.CommitAsync(Token);
            var device = await Register(source, rows.Tenant, newBranch);
            var initial = await Snapshot(source, rows, device: device);
            Assert.Equal(6m, Assert.Single(initial.Items).Product!.RetailPrice);
            Assert.Empty((await Changes(source, rows, initial.NextCursor, device: device)).Items);
        }

        Task<int> InsertBranch(MediPosDbContext context) => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO branches (id,tenant_id,legal_entity_id,name,is_main_hub,created_at)
            VALUES ({newBranch},{pair.A.TenantId},{pair.A.LegalEntityId},'Concurrent branch',false,now())
            """, Token);
        Task<int> UpdatePrice(MediPosDbContext context) => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE business_products SET retail_price = 6 WHERE id = {pair.A.BusinessProductId}", Token);
    }

    private async Task WaitForTopologyLock(int pid)
    {
        await using var observer = fixture.CreateConstraintContext();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        while (!await observer.Database.SqlQuery<bool>($"SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid = {pid} AND wait_event = 'advisory') AS \"Value\"").SingleAsync(timeout.Token))
            await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
    }
}
