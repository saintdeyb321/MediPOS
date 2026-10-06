using MediPOS.Application.Modules.Branches;
using MediPOS.Application.Modules.Branches.CreateBranch;
using MediPOS.Application.Modules.Branches.CreateLegalEntity;
using MediPOS.Application.Modules.Branches.SetMainHubBranch;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Application.Modules.TenancyLicensing.CreateTenant;
using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.Infrastructure;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.Branches;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class BranchesPersistenceTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SecondMigrationAppliesWithoutPendingModelChanges()
    {
        await using var context = fixture.CreateContext();
        var applied = await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);
        Assert.Contains(applied, migration => migration.EndsWith("_AddLegalEntitiesAndBranches", StringComparison.Ordinal));
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LegalEntityAndBranchPersistTenantBoundaryAndUtcDates()
    {
        await using var services = CreateServices();
        await using var scope = services.CreateAsyncScope();
        var setup = await CreateTenantAndLegalEntityAsync(scope.ServiceProvider);
        var created = await scope.ServiceProvider.GetRequiredService<CreateBranchHandler>().HandleAsync(
            new(setup.TenantId, setup.LegalEntityId, " Centro "), TestContext.Current.CancellationToken);

        await using var context = fixture.CreateContext(setup.TenantId);
        var legalEntity = await context.LegalEntities.SingleAsync(value => value.Id == setup.LegalEntityId, TestContext.Current.CancellationToken);
        var branch = await context.Branches.SingleAsync(value => value.Id == created.Id, TestContext.Current.CancellationToken);
        Assert.Equal(setup.TenantId, legalEntity.TenantId);
        Assert.Equal(setup.TenantId, branch.TenantId);
        Assert.Equal(legalEntity.Id, branch.LegalEntityId);
        Assert.Equal("Centro", branch.Name);
        Assert.False(branch.IsMainHub);
        Assert.Equal(Now, legalEntity.CreatedAt);
        Assert.Equal(Now, branch.CreatedAt);
        Assert.Equal(TimeSpan.Zero, branch.CreatedAt.Offset);
    }

    [Fact]
    public async Task CompositeForeignKeyRejectsLegalEntityFromAnotherTenant()
    {
        await using var services = CreateServices();
        await using var scope = services.CreateAsyncScope();
        var first = await CreateTenantAndLegalEntityAsync(scope.ServiceProvider);
        await using var secondScope = services.CreateAsyncScope();
        var second = await CreateTenantAndLegalEntityAsync(secondScope.ServiceProvider);
        await using var context = fixture.CreateContext(first.TenantId);

        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO branches (id, tenant_id, legal_entity_id, name, is_main_hub, created_at) VALUES ({Guid.NewGuid()}, {first.TenantId}, {second.LegalEntityId}, 'Foreign', false, {Now})",
            TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => scope.ServiceProvider.GetRequiredService<CreateBranchHandler>().HandleAsync(
            new(first.TenantId, second.LegalEntityId, "Foreign"), TestContext.Current.CancellationToken));
        Assert.Empty(await context.Branches.Where(value => value.TenantId == first.TenantId).ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ForeignKeysRejectLegalEntityAndBranchWithoutTenant()
    {
        var missingTenant = Guid.NewGuid();
        await using var context = fixture.CreateContext(missingTenant);
        context.LegalEntities.Add(LegalEntity.Create(missingTenant, "Missing", "123", Now));
        var legalError = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(legalError.InnerException).SqlState);

        await using var services = CreateServices();
        await using var scope = services.CreateAsyncScope();
        var setup = await CreateTenantAndLegalEntityAsync(scope.ServiceProvider);
        await using var constraintContext = fixture.CreateConstraintContext();
        var branchError = await Assert.ThrowsAsync<PostgresException>(() => constraintContext.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO branches (id, tenant_id, legal_entity_id, name, is_main_hub, created_at) VALUES ({Guid.NewGuid()}, {Guid.NewGuid()}, {setup.LegalEntityId}, 'Missing', false, {Now})",
            TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, branchError.SqlState);
    }

    [Fact]
    public async Task PartialUniqueIndexRejectsTwoMainHubsForSameTenant()
    {
        await using var services = CreateServices();
        await using var scope = services.CreateAsyncScope();
        var setup = await CreateTenantAndLegalEntityAsync(scope.ServiceProvider);
        var handler = scope.ServiceProvider.GetRequiredService<CreateBranchHandler>();
        await handler.HandleAsync(new(setup.TenantId, setup.LegalEntityId, "Centro"), TestContext.Current.CancellationToken);
        await handler.HandleAsync(new(setup.TenantId, setup.LegalEntityId, "Norte"), TestContext.Current.CancellationToken);
        await using var context = fixture.CreateContext(setup.TenantId);

        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE branches SET is_main_hub = true WHERE tenant_id = {setup.TenantId}", TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        Assert.Equal("ux_branches_tenant_main_hub", error.ConstraintName);
        Assert.False(await context.Branches.AnyAsync(value => value.TenantId == setup.TenantId && value.IsMainHub, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MovingHubPreservesOneHubPerTenantAndRejectsForeignTarget()
    {
        await using var services = CreateServices();
        await using var scope = services.CreateAsyncScope();
        var setup = await CreateTenantAndLegalEntityAsync(scope.ServiceProvider);
        await using var foreignScope = services.CreateAsyncScope();
        var foreign = await CreateTenantAndLegalEntityAsync(foreignScope.ServiceProvider);
        var creator = scope.ServiceProvider.GetRequiredService<CreateBranchHandler>();
        var first = await creator.HandleAsync(new(setup.TenantId, setup.LegalEntityId, "Centro"), TestContext.Current.CancellationToken);
        var second = await creator.HandleAsync(new(setup.TenantId, setup.LegalEntityId, "Norte"), TestContext.Current.CancellationToken);
        var foreignBranch = await foreignScope.ServiceProvider.GetRequiredService<CreateBranchHandler>().HandleAsync(
            new(foreign.TenantId, foreign.LegalEntityId, "Otra"), TestContext.Current.CancellationToken);
        var hubHandler = scope.ServiceProvider.GetRequiredService<SetMainHubBranchHandler>();
        await foreignScope.ServiceProvider.GetRequiredService<SetMainHubBranchHandler>().HandleAsync(
            new(foreign.TenantId, foreignBranch.Id), TestContext.Current.CancellationToken);
        await hubHandler.HandleAsync(new(setup.TenantId, first.Id), TestContext.Current.CancellationToken);
        await hubHandler.HandleAsync(new(setup.TenantId, second.Id), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => hubHandler.HandleAsync(new(setup.TenantId, foreignBranch.Id), TestContext.Current.CancellationToken));
        await using var context = fixture.CreateContext(setup.TenantId);
        var hubs = await context.Branches.Where(value => value.TenantId == setup.TenantId && value.IsMainHub).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(second.Id, Assert.Single(hubs).Id);
        await using var foreignContext = fixture.CreateContext(foreign.TenantId);
        Assert.True(await foreignContext.Branches.AnyAsync(value => value.Id == foreignBranch.Id && value.IsMainHub, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LicensedLimitRejectsCreationAfterLastAvailableSlot()
    {
        await using var services = CreateServices();
        await using var scope = services.CreateAsyncScope();
        var setup = await CreateTenantAndLegalEntityAsync(scope.ServiceProvider, maxBranches: 1);
        var creator = scope.ServiceProvider.GetRequiredService<CreateBranchHandler>();
        await creator.HandleAsync(new(setup.TenantId, setup.LegalEntityId, "Centro"), TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => creator.HandleAsync(
            new(setup.TenantId, setup.LegalEntityId, "Norte"), TestContext.Current.CancellationToken));
        Assert.Equal("The licensed branch limit has been reached.", error.Message);
        await using var context = fixture.CreateContext(setup.TenantId);
        Assert.Single(await context.Branches.Where(value => value.TenantId == setup.TenantId).ToListAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(LicenseStatus.Suspended, false)]
    [InlineData(LicenseStatus.Active, true)]
    public async Task SuspendedOrExpiredLicenseRejectsBranchCreation(LicenseStatus status, bool expired)
    {
        await using var services = CreateServices();
        await using var scope = services.CreateAsyncScope();
        var setup = await CreateTenantAndLegalEntityAsync(scope.ServiceProvider, status: status, expired: expired);
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<CreateBranchHandler>().HandleAsync(
            new(setup.TenantId, setup.LegalEntityId, "Centro"), TestContext.Current.CancellationToken));
        await using var context = fixture.CreateContext(setup.TenantId);
        Assert.Empty(await context.Branches.Where(value => value.TenantId == setup.TenantId).ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConcurrentCreationsSerializeAndCannotExceedLicenseLimit()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await using var services = CreateServices();
        (Guid TenantId, Guid LegalEntityId) setup;
        await using (var setupScope = services.CreateAsyncScope())
        {
            setup = await CreateTenantAndLegalEntityAsync(setupScope.ServiceProvider, maxBranches: 2);
            await setupScope.ServiceProvider.GetRequiredService<CreateBranchHandler>().HandleAsync(new(setup.TenantId, setup.LegalEntityId, "Inicial"), timeout.Token);
        }

        await using var firstScope = services.CreateAsyncScope();
        await using var secondScope = services.CreateAsyncScope();
        var firstProvisioning = new ObservedProvisioning(firstScope.ServiceProvider.GetRequiredService<ITenantLicenseProvisioning>(), pauseAfterAcquire: true);
        var secondProvisioning = new ObservedProvisioning(secondScope.ServiceProvider.GetRequiredService<ITenantLicenseProvisioning>(), pauseAfterAcquire: false);
        var firstHandler = new CreateBranchHandler(firstScope.ServiceProvider.GetRequiredService<IBranchesStore>(), firstProvisioning, new FixedTimeProvider());
        var secondHandler = new CreateBranchHandler(secondScope.ServiceProvider.GetRequiredService<IBranchesStore>(), secondProvisioning, new FixedTimeProvider());
        var firstTask = firstHandler.HandleAsync(new(setup.TenantId, setup.LegalEntityId, "Primera"), timeout.Token);
        Task<BranchDetails>? secondTask = null;

        try
        {
            await firstProvisioning.Acquired.Task.WaitAsync(timeout.Token);
            secondTask = secondHandler.HandleAsync(new(setup.TenantId, setup.LegalEntityId, "Segunda"), timeout.Token);
            await secondProvisioning.Started.Task.WaitAsync(timeout.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
            Assert.False(secondProvisioning.Acquired.Task.IsCompleted);
        }
        finally
        {
            firstProvisioning.Release.TrySetResult();
            await firstTask;
        }

        Assert.NotNull(secondTask);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => secondTask);
        Assert.Equal("The licensed branch limit has been reached.", error.Message);
        await using var context = fixture.CreateContext(setup.TenantId);
        Assert.Equal(2, await context.Branches.CountAsync(value => value.TenantId == setup.TenantId, timeout.Token));
    }

    [Fact]
    public async Task UncommittedProvisioningRollsBackAndReleasesLicenseLock()
    {
        await using var services = CreateServices();
        (Guid TenantId, Guid LegalEntityId) setup;
        await using (var setupScope = services.CreateAsyncScope())
        {
            setup = await CreateTenantAndLegalEntityAsync(setupScope.ServiceProvider);
        }

        await using (var scope = services.CreateAsyncScope())
        {
            var provisioning = scope.ServiceProvider.GetRequiredService<ITenantLicenseProvisioning>();
            await using var transaction = await provisioning.BeginAsync(setup.TenantId, TestContext.Current.CancellationToken);
            Assert.NotNull(transaction);
            var branch = Branch.Create(setup.TenantId, setup.LegalEntityId, setup.TenantId, "Sin commit", Now);
            await scope.ServiceProvider.GetRequiredService<IBranchesStore>().AddBranchAsync(branch, TestContext.Current.CancellationToken);
        }

        await using var verification = fixture.CreateContext(setup.TenantId);
        Assert.Empty(await verification.Branches.Where(value => value.TenantId == setup.TenantId).ToListAsync(TestContext.Current.CancellationToken));
        await using var nextScope = services.CreateAsyncScope();
        await nextScope.ServiceProvider.GetRequiredService<CreateBranchHandler>().HandleAsync(new(setup.TenantId, setup.LegalEntityId, "Con commit"), TestContext.Current.CancellationToken);
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
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private static async Task<(Guid TenantId, Guid LegalEntityId)> CreateTenantAndLegalEntityAsync(
        IServiceProvider services,
        int maxBranches = 3,
        LicenseStatus status = LicenseStatus.Active,
        bool expired = false)
    {
        var tenant = await services.GetRequiredService<CreateTenantHandler>().HandleAsync(
            new("Botica", Now.AddDays(-1), expired ? Now : Now.AddMonths(1), maxBranches, status, Guid.NewGuid()), TestContext.Current.CancellationToken);
        var legalEntity = await services.GetRequiredService<CreateLegalEntityHandler>().HandleAsync(
            new(tenant.TenantId, "Botica SAC", "123"), TestContext.Current.CancellationToken);
        return (tenant.TenantId, legalEntity.Id);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    // Observes actual PostgreSQL lock acquisition; it does not replace the database provisioning implementation.
    private sealed class ObservedProvisioning(ITenantLicenseProvisioning inner, bool pauseAfterAcquire) : ITenantLicenseProvisioning
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Acquired { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ITenantLicenseProvisioningScope?> BeginAsync(Guid tenantId, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            var scope = await inner.BeginAsync(tenantId, cancellationToken)
                ?? throw new InvalidOperationException("Test tenant license was not found.");
            Acquired.TrySetResult();

            try
            {
                if (pauseAfterAcquire)
                {
                    await Release.Task.WaitAsync(cancellationToken);
                }
                return scope;
            }
            catch
            {
                await scope.DisposeAsync();
                throw;
            }
        }
    }
}
