using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Application.Modules.TenancyLicensing.CreateTenant;
using MediPOS.Application.Modules.TenancyLicensing.ReactivateLicense;
using MediPOS.Application.Modules.TenancyLicensing.RenewLicense;
using MediPOS.Application.Modules.TenancyLicensing.RequestTenantPurge;
using MediPOS.Application.Modules.TenancyLicensing.SuspendLicense;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.Infrastructure;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.TenancyLicensing;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class TenancyLicensingTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InitialMigrationAppliesToEmptyDatabase()
    {
        await using var context = fixture.CreateContext();
        var applied = await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);

        Assert.Contains(applied, migration => migration.EndsWith("_InitialTenancyLicensing", StringComparison.Ordinal));
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreationPersistsTenantLicenseHistoryAndStableCodes()
    {
        var actor = Guid.NewGuid();
        await using var services = CreateServices();
        await using var scope = services.CreateAsyncScope();
        var result = await CreateTenantAsync(scope.ServiceProvider, actor);

        await using var context = fixture.CreateContext(result.TenantId);
        var tenant = await context.Tenants.Include(value => value.License).ThenInclude(value => value.Changes)
            .SingleAsync(value => value.Id == result.TenantId, TestContext.Current.CancellationToken);
        var change = Assert.Single(tenant.License.Changes);
        Assert.Equal(result.LicenseId, tenant.License.Id);
        Assert.Equal(tenant.Id, tenant.License.TenantId);
        Assert.Equal(tenant.Id, change.TenantId);
        Assert.Equal(actor, change.ActorId);
        Assert.Equal(Now, change.OccurredAt);
        Assert.Null(change.PreviousStatus);

        await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT l.status, h.kind FROM licenses l JOIN license_changes h ON h.tenant_id = l.tenant_id AND h.license_id = l.id WHERE l.tenant_id = @tenant";
        command.Parameters.Add(new NpgsqlParameter("tenant", tenant.Id));
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal("active", reader.GetString(0));
        Assert.Equal("created", reader.GetString(1));
    }

    [Fact]
    public async Task MutationSlicesPersistStateHistoryAndPurgeGate()
    {
        var actor = Guid.NewGuid();
        await using var services = CreateServices();
        LicenseDetails created;

        await using (var scope = services.CreateAsyncScope())
        {
            created = await CreateTenantAsync(scope.ServiceProvider, actor);
        }

        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<RenewLicenseHandler>().HandleAsync(
                new(created.TenantId, created.LicenseId, Now.AddMonths(2), actor), TestContext.Current.CancellationToken);
        }
        await AssertStoredStatusAsync(created, LicenseStatus.Active, 2);

        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<SuspendLicenseHandler>().HandleAsync(
                new(created.TenantId, created.LicenseId, actor), TestContext.Current.CancellationToken);
        }
        await AssertStoredStatusAsync(created, LicenseStatus.Suspended, 3);

        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ReactivateLicenseHandler>().HandleAsync(
                new(created.TenantId, created.LicenseId, LicenseStatus.Active, actor), TestContext.Current.CancellationToken);
        }
        await AssertStoredStatusAsync(created, LicenseStatus.Active, 4);

        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<RequestTenantPurgeHandler>().HandleAsync(
                new(created.TenantId, created.LicenseId, actor), TestContext.Current.CancellationToken);
        }
        await AssertStoredStatusAsync(created, LicenseStatus.PurgePending, 5);

        await using var context = fixture.CreateContext(created.TenantId);
        var tenant = await context.Tenants.Include(value => value.License).ThenInclude(value => value.Changes)
            .SingleAsync(value => value.Id == created.TenantId, TestContext.Current.CancellationToken);
        Assert.False(tenant.License.AllowsOperation(Now.AddDays(1)));
        Assert.Equal(Now.AddMonths(2), tenant.License.ExpiresAt);
        Assert.Contains(tenant.License.Changes, change => change.Kind == LicenseChangeKind.Renewed
            && change.PreviousExpiresAt == Now.AddMonths(1) && change.NewExpiresAt == Now.AddMonths(2));
        Assert.All(tenant.License.Changes, change => Assert.Equal(actor, change.ActorId));
    }

    [Fact]
    public async Task StoreRejectsMismatchedTenantAndLicenseWithoutChanges()
    {
        var actor = Guid.NewGuid();
        await using var services = CreateServices();
        await using var scope = services.CreateAsyncScope();
        var first = await CreateTenantAsync(scope.ServiceProvider, actor);
        await using var secondScope = services.CreateAsyncScope();
        var second = await CreateTenantAsync(secondScope.ServiceProvider, actor);

        var store = scope.ServiceProvider.GetRequiredService<ITenancyLicensingStore>();
        Assert.Null(await store.FindLicenseAsync(first.TenantId, second.LicenseId, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => scope.ServiceProvider.GetRequiredService<SuspendLicenseHandler>().HandleAsync(
            new(first.TenantId, second.LicenseId, actor), TestContext.Current.CancellationToken));
        await AssertStoredStatusAsync(second, LicenseStatus.Active, 1);
    }

    [Fact]
    public async Task DatabaseRejectsSecondCurrentLicenseForSameTenant()
    {
        var tenant = NewTenant();
        await using var context = fixture.CreateContext(tenant.Id);
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();
        context.Licenses.Add(License.Create(tenant.Id, Now, Now.AddMonths(1), 1, LicenseStatus.Trial, Guid.NewGuid(), Now));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);

        await using var verification = fixture.CreateContext(tenant.Id);
        Assert.Single(await verification.Licenses.Where(value => value.TenantId == tenant.Id).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Single(await verification.LicenseChanges.Where(value => value.TenantId == tenant.Id).ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DatabaseRejectsLicenseWithoutTenant()
    {
        var missingTenant = Guid.NewGuid();
        await using var context = fixture.CreateContext(missingTenant);
        context.Licenses.Add(License.Create(missingTenant, Now, Now.AddMonths(1), 1, LicenseStatus.Active, Guid.NewGuid(), Now));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Fact]
    public async Task HistoryForeignKeyCannotCrossTenantBoundary()
    {
        var first = NewTenant();
        var second = NewTenant();
        await using var firstContext = fixture.CreateContext(first.Id);
        firstContext.Tenants.Add(first);
        await firstContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        await using var context = fixture.CreateContext(second.Id);
        context.Tenants.Add(second);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO license_changes (id, tenant_id, license_id, kind, actor_id, occurred_at, new_status, new_starts_at, new_expires_at, new_max_branches) VALUES ({Guid.NewGuid()}, {second.Id}, {first.License.Id}, 'created', {Guid.NewGuid()}, {Now}, 'active', {Now}, {Now.AddMonths(1)}, 1)",
            TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task DatabaseProtectsBranchLimits(int limit)
    {
        var tenant = NewTenant();
        await using var context = fixture.CreateContext(tenant.Id);
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE licenses SET max_branches = {limit} WHERE tenant_id = {tenant.Id}", TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task DatabaseProtectsPeriodAndStableStatusCodes()
    {
        var tenant = NewTenant();
        await using var context = fixture.CreateContext(tenant.Id);
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var periodError = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE licenses SET expires_at = starts_at WHERE tenant_id = {tenant.Id}", TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.CheckViolation, periodError.SqlState);
        var statusError = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE licenses SET status = '1' WHERE tenant_id = {tenant.Id}", TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.CheckViolation, statusError.SqlState);
    }

    [Fact]
    public async Task NormalPersistenceCannotModifyOrRemoveExistingHistory()
    {
        var tenant = NewTenant();
        await using var context = fixture.CreateContext(tenant.Id);
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var change = Assert.Single(tenant.License.Changes);

        context.Entry(change).Property(value => value.NewStatus).CurrentValue = LicenseStatus.Cancelled;
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        context.Entry(change).Property(value => value.NewStatus).CurrentValue = LicenseStatus.Active;
        context.Entry(change).State = EntityState.Unchanged;
        context.LicenseChanges.Remove(change);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConcurrentRenewalCannotOverwriteExpirationOrAppendPartialHistory()
    {
        var actor = Guid.NewGuid();
        await using var services = CreateServices();
        LicenseDetails created;
        await using (var scope = services.CreateAsyncScope())
        {
            created = await CreateTenantAsync(scope.ServiceProvider, actor);
        }

        await using var firstScope = services.CreateAsyncScope();
        await using var secondScope = services.CreateAsyncScope();
        var firstStore = firstScope.ServiceProvider.GetRequiredService<ITenancyLicensingStore>();
        var secondStore = secondScope.ServiceProvider.GetRequiredService<ITenancyLicensingStore>();
        var first = await firstStore.FindLicenseAsync(created.TenantId, created.LicenseId, TestContext.Current.CancellationToken);
        var second = await secondStore.FindLicenseAsync(created.TenantId, created.LicenseId, TestContext.Current.CancellationToken);
        Assert.NotNull(first);
        Assert.NotNull(second);
        first.Renew(Now.AddMonths(3), actor, Now);
        second.Renew(Now.AddMonths(2), actor, Now);

        await firstStore.SaveLicenseAsync(first, TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => secondStore.SaveLicenseAsync(second, TestContext.Current.CancellationToken));
        Assert.IsType<DbUpdateConcurrencyException>(error.InnerException);

        await using var context = fixture.CreateContext(created.TenantId);
        var persisted = await context.Licenses.Include(value => value.Changes)
            .SingleAsync(value => value.Id == created.LicenseId, TestContext.Current.CancellationToken);
        Assert.Equal(Now.AddMonths(3), persisted.ExpiresAt);
        Assert.Equal(new[] { LicenseChangeKind.Created, LicenseChangeKind.Renewed }, persisted.Changes.Select(value => value.Kind).Order());
    }

    private ServiceProvider CreateServices()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MediPosDatabase"] = fixture.ConnectionString,
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider());
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static Task<LicenseDetails> CreateTenantAsync(IServiceProvider services, Guid actor) =>
        services.GetRequiredService<CreateTenantHandler>().HandleAsync(
            new("Botica", Now, Now.AddMonths(1), 1, LicenseStatus.Active, actor), TestContext.Current.CancellationToken);

    private async Task AssertStoredStatusAsync(LicenseDetails created, LicenseStatus status, int historyCount)
    {
        await using var context = fixture.CreateContext(created.TenantId);
        var license = await context.Licenses.Include(value => value.Changes)
            .SingleAsync(value => value.TenantId == created.TenantId && value.Id == created.LicenseId, TestContext.Current.CancellationToken);
        Assert.Equal(status, license.Status);
        Assert.Equal(historyCount, license.Changes.Count);
    }

    private static Tenant NewTenant() => Tenant.Create("Botica", Now, Now.AddMonths(1), 1, LicenseStatus.Active, Guid.NewGuid(), Now);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
