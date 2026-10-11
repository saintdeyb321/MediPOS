using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.OfflineSync;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.UnitTests.Modules.OfflineSync;

public sealed class SyncWriteGuardTests
{
    [Theory]
    [InlineData("TenantId")]
    [InlineData("BranchId")]
    [InlineData("RegisteredByMembershipId")]
    public async Task EfReassignmentIsRejectedBeforeOpeningAnyConnection(string property)
    {
        var device = SyncDevice.Register(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "POS", SyncDomainTests.Now);
        var tenant = new TenantDataContext(); tenant.SelectTenant(device.TenantId);
        await using var context = new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql().Options, tenant);
        context.Attach(device);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            context.Entry(device).Property(property).CurrentValue = Guid.NewGuid();
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        });
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }
    [Fact]
    public async Task InboxHistoryCannotBeDeletedThroughEfEvenWithoutDatabase()
    {
        var syncEvent = SyncDomainTests.Receive(); var tenant = new TenantDataContext(); tenant.SelectTenant(syncEvent.TenantId);
        await using var context = new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql().Options, tenant);
        context.Attach(syncEvent); context.Remove(syncEvent);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }
}
