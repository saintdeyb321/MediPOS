using MediPOS.Application.Tenancy;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediPOS.IntegrationTests.Tenancy;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class TenantConnectionReuseTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task OnePhysicalPooledConnectionDoesNotCarryTenantIntoNextScope()
    {
        var (first, second) = await TenantIsolationTestData.CreatePairAsync(fixture);
        var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            ApplicationName = Guid.NewGuid().ToString("N"),
            MaxPoolSize = 1,
            // Deliberate stress: tenant correctness must hold even without the driver's state reset.
            NoResetOnClose = true,
        }.ConnectionString;
        int firstPid;
        await using (var firstContext = CreateContext(connectionString, first.TenantId))
        {
            await firstContext.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            firstPid = await BackendPidAsync(firstContext);
            Assert.Equal(first.TenantId.ToString("D"), await TenantSettingAsync(firstContext));
            AssertOnlyTenant(await firstContext.Branches.Select(value => value.TenantId).ToListAsync(TestContext.Current.CancellationToken), first.TenantId);
        }
        await using (var noTenant = CreateContext(connectionString, null))
        {
            await noTenant.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            Assert.Equal(firstPid, await BackendPidAsync(noTenant));
            Assert.Equal(string.Empty, await TenantSettingAsync(noTenant));
            Assert.Empty(await noTenant.Branches.ToListAsync(TestContext.Current.CancellationToken));
            // This direct SQL has no EF query filter, so it proves the RLS setting was cleared.
            await using var command = noTenant.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT count(*) FROM branches";
            Assert.Equal(0L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        }
        await using (var secondContext = CreateContext(connectionString, second.TenantId))
        {
            // Exercise synchronous framework interception as well as the normal asynchronous application path.
            secondContext.Database.OpenConnection();
            Assert.Equal(firstPid, await BackendPidAsync(secondContext));
            Assert.Equal(second.TenantId.ToString("D"), await TenantSettingAsync(secondContext));
            AssertOnlyTenant(secondContext.Branches.Select(value => value.TenantId).ToList(), second.TenantId);
        }
    }

    [Fact]
    public async Task SelectionAfterOpenAndTransactionRollbackAreResynchronizedBeforeQuery()
    {
        var (first, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        var scope = new TenantDataContext();
        await using var context = new MediPosDbContext(
            new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql(fixture.ConnectionString).Options, scope);
        await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        Assert.Equal(string.Empty, await TenantSettingAsync(context));
        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            scope.SelectTenant(first.TenantId);
            AssertOnlyTenant(await context.Licenses.Select(value => value.TenantId).ToListAsync(TestContext.Current.CancellationToken), first.TenantId);
            await transaction.RollbackAsync(TestContext.Current.CancellationToken);
        }
        // PostgreSQL rolls back the session setting, but the data scope remains bound to A.
        Assert.Equal(string.Empty, await TenantSettingAsync(context));
        Assert.Equal(first.TenantId, scope.TenantId);
        AssertOnlyTenant(await context.Licenses.Select(value => value.TenantId).ToListAsync(TestContext.Current.CancellationToken), first.TenantId);
        Assert.Equal(first.TenantId.ToString("D"), await TenantSettingAsync(context));
    }

    private static MediPosDbContext CreateContext(string connectionString, Guid? tenantId)
    {
        var context = new TenantDataContext();
        if (tenantId.HasValue)
            context.SelectTenant(tenantId.Value);
        return new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql(connectionString).Options, context);
    }

    private static async Task<int> BackendPidAsync(MediPosDbContext context)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_backend_pid()";
        return Assert.IsType<int>(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<string> TenantSettingAsync(MediPosDbContext context)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COALESCE(current_setting('medipos.tenant_id', true), '')";
        return Assert.IsType<string>(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    private static void AssertOnlyTenant(IReadOnlyList<Guid> tenantIds, Guid tenantId)
    {
        Assert.NotEmpty(tenantIds);
        Assert.All(tenantIds, value => Assert.Equal(tenantId, value));
    }
}
