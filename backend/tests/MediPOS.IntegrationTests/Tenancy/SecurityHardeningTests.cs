using System.Data.Common;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.Branches.CreateBranch;
using MediPOS.Application.Modules.Catalog.CreateCategory;
using MediPOS.Application.Modules.Catalog.ProductImport;
using MediPOS.Application.Modules.IdentityAccess;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.CreateMembership;
using MediPOS.Application.Modules.IdentityAccess.DeactivateMembership;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.IdentityAccess.ReplaceWorkSchedule;
using MediPOS.Application.Modules.IdentityAccess.SetMembershipBranches;
using MediPOS.Application.Modules.Purchasing;
using MediPOS.Application.Modules.Purchasing.CreatePurchase;
using MediPOS.Application.Modules.Purchasing.FindPurchasesByDocumentReference;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Application.Modules.TenancyLicensing.CreateTenant;
using MediPOS.Application.Modules.TenancyLicensing.SuspendLicense;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Catalog.ProductImport;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.Purchasing;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.Infrastructure;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Tenancy;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class SecurityHardeningTests(PostgreSqlFixture fixture)
{
    private static DateTimeOffset Now => IdentityAccessTestSetup.Now;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReferenceQueriesScopeSqlAndMaterializationToThePharmacistsBranch()
    {
        var seed = await SeedAsync();
        Guid otherBranch;
        await using (var seedServices = IdentityAccessTestSetup.CreateServices(fixture))
        await using (var seedScope = seedServices.CreateAsyncScope())
        {
            var provider = seedScope.ServiceProvider;
            await using var context = fixture.CreateContext(seed.Owner.TenantId);
            var legal = await context.LegalEntities.SingleAsync(Token);
            otherBranch = (await provider.GetRequiredService<CreateBranchHandler>().HandleAsync(
                new(seed.Owner.TenantId, legal.Id, "Other synthetic branch", seed.Owner.UserId), Token)).Id;
            await provider.GetRequiredService<CreatePurchaseHandler>().HandleAsync(
                new(seed.Owner.TenantId, seed.Owner.BranchId, null, "F001-SCOPE", seed.Owner.UserId), Token);
            await provider.GetRequiredService<CreatePurchaseHandler>().HandleAsync(
                new(seed.Owner.TenantId, otherBranch, null, "F001-SCOPE", seed.Owner.UserId), Token);
        }
        var observation = new PurchaseObservation();
        await using var services = Services(observation);
        await using (var scope = services.CreateAsyncScope())
        {
            Session(scope.ServiceProvider).UserId = seed.PharmacistUser;
            var rows = await scope.ServiceProvider.GetRequiredService<FindPurchasesByDocumentReferenceHandler>().HandleAsync(
                new(seed.Owner.TenantId, "F001-SCOPE", seed.Owner.BranchId), Token);
            Assert.Single(rows);
            Assert.All(observation.Branches, branch => Assert.Equal(seed.Owner.BranchId, branch));
            Assert.Single(observation.Branches);
            Assert.Contains(observation.Commands, sql => sql.Contains("branch_id =", StringComparison.Ordinal) && sql.Contains("LIMIT", StringComparison.Ordinal));
            observation.Branches.Clear(); observation.Commands.Clear();
            var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => scope.ServiceProvider.GetRequiredService<FindPurchasesByDocumentReferenceHandler>()
                .HandleAsync(new(seed.Owner.TenantId, "F001-SCOPE", otherBranch), Token));
            Assert.Equal("access.branch_unassigned", error.Error.Code);
            Assert.Empty(observation.Branches);
            Assert.Empty(observation.Commands);
        }
        await using (var scope = services.CreateAsyncScope())
        {
            Session(scope.ServiceProvider).UserId = seed.Owner.UserId;
            var rows = await scope.ServiceProvider.GetRequiredService<FindPurchasesByDocumentReferenceHandler>().HandleAsync(
                new(seed.Owner.TenantId, "F001-SCOPE"), Token);
            Assert.Equal(2, rows.Count);
            Assert.Contains(rows, purchase => purchase.BranchId == otherBranch);
        }
        await using (var scope = services.CreateAsyncScope())
        {
            Session(scope.ServiceProvider).UserId = seed.PharmacistUser;
            var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => scope.ServiceProvider.GetRequiredService<FindPurchasesByDocumentReferenceHandler>()
                .HandleAsync(new(Guid.NewGuid(), "F001-SCOPE", seed.Owner.BranchId), Token));
            Assert.Equal("access.membership_missing", error.Error.Code);
        }
    }

    [Theory]
    [InlineData("membership", "access.membership_inactive")]
    [InlineData("license", "license.operation_denied")]
    [InlineData("assignment", "access.branch_unassigned")]
    public async Task AuthorizationChangesDuringLicenseLockWaitRollBackWithoutPurchaseOrStock(string change, string expectedCode)
    {
        var seed = await SeedAsync();
        await using var services = Services();
        await using var blocker = services.CreateAsyncScope();
        var provider = blocker.ServiceProvider;
        Session(provider).UserId = seed.Owner.UserId;
        await provider.GetRequiredService<ITenantOperationAuthorizer>().RequireAsync(seed.Owner.TenantId, null, seed.Owner.UserId,
            TenantOperation.Administration, Token);
        var context = provider.GetRequiredService<MediPosDbContext>();
        context.SelectTenant(seed.Owner.TenantId);
        await using var transaction = await context.Database.BeginTransactionAsync(Token);
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM licenses WHERE tenant_id={seed.Owner.TenantId} FOR UPDATE", Token);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var target = services.CreateAsyncScope();
        Session(target.ServiceProvider).UserId = seed.PharmacistUser;
        var licensing = new ObservedProvisioning(target.ServiceProvider.GetRequiredService<ITenantLicenseProvisioning>(), () => started.TrySetResult());
        var handler = ActivatorUtilities.CreateInstance<CreatePurchaseHandler>(target.ServiceProvider, licensing);
        var reference = "SECURITY-" + Guid.NewGuid().ToString("N");
        var task = handler.HandleAsync(new(seed.Owner.TenantId, seed.Owner.BranchId, null, reference, seed.PharmacistUser), Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        var identityStore = provider.GetRequiredService<IIdentityAccessStore>();
        if (change == "membership")
        {
            var membership = (await identityStore.FindMembershipAsync(seed.Owner.TenantId, seed.PharmacistMembership, Token))!;
            var before = AuditTrail.MembershipState(membership); membership.Deactivate(Now);
            await identityStore.SaveDeactivationAsync(membership, AuditTrail.Record(seed.Owner.TenantId, seed.Owner.UserId,
                AuditAction.MembershipDeactivated, membership.Id, Now, before, AuditTrail.MembershipState(membership)), Token);
        }
        else if (change == "assignment")
        {
            await identityStore.ReplaceBranchesAsync(seed.Owner.TenantId, seed.PharmacistMembership, [], AuditTrail.Record(seed.Owner.TenantId,
                seed.Owner.UserId, AuditAction.MembershipBranchesReplaced, seed.PharmacistMembership, Now,
                AuditTrail.BranchAssignments([seed.Owner.BranchId]), AuditTrail.BranchAssignments([])), Token);
        }
        else
        {
            ((PlatformSession)provider.GetRequiredService<IPlatformAdministration>()).Actor = seed.Owner.ActorId;
            await provider.GetRequiredService<SuspendLicenseHandler>().HandleAsync(new(seed.Owner.TenantId, seed.Owner.LicenseId, seed.Owner.ActorId), Token);
        }
        await transaction.CommitAsync(Token);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => task);
        Assert.Equal(expectedCode, error.Error.Code);
        await using var verification = fixture.CreateContext(seed.Owner.TenantId);
        Assert.False(await verification.Purchases.AnyAsync(purchase => purchase.DocumentReference == reference, Token));
        Assert.Empty(await verification.InventoryLots.ToListAsync(Token));
        Assert.Empty(await verification.StockMovements.ToListAsync(Token));
        Assert.False(await verification.AuditLogs.AnyAsync(audit => audit.Action == AuditAction.PurchaseConfirmed, Token));
    }

    [Fact]
    public async Task ConcurrentFirstOwnerBootstrapAllowsOneWinnerAndSecondOwnerUsesOrdinaryPolicy()
    {
        var users = await UsersAsync(3);
        await using var services = Services();
        Guid tenant;
        await using (var scope = services.CreateAsyncScope())
        {
            ((PlatformSession)scope.ServiceProvider.GetRequiredService<IPlatformAdministration>()).Actor = users[0];
            tenant = (await scope.ServiceProvider.GetRequiredService<CreateTenantHandler>().HandleAsync(
                new("Bootstrap synthetic tenant", Now.AddDays(-1), Now.AddMonths(1), 3, LicenseStatus.Active, users[0]), Token)).TenantId;
        }
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        async Task<(MembershipDetails? Member, string? Error)> AttemptAsync(Guid user)
        {
            await using var scope = services.CreateAsyncScope();
            ((PlatformSession)scope.ServiceProvider.GetRequiredService<IPlatformAdministration>()).Actor = users[0];
            var provisioning = new ObservedProvisioning(scope.ServiceProvider.GetRequiredService<ITenantLicenseProvisioning>(), async () =>
            {
                if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
            });
            var handler = ActivatorUtilities.CreateInstance<CreateMembershipHandler>(scope.ServiceProvider, provisioning);
            try { return (await handler.HandleAsync(new(tenant, user, TenantRole.Owner, users[0]), Token), null); }
            catch (ApplicationErrorException error) { return (null, error.Error.Code); }
        }
        var results = await Task.WhenAll(AttemptAsync(users[0]), AttemptAsync(users[1]));
        var winner = Assert.Single(results, result => result.Member is not null).Member!;
        Assert.Equal("access.bootstrap_denied", Assert.Single(results, result => result.Error is not null).Error);
        await using var ordinary = services.CreateAsyncScope();
        Session(ordinary.ServiceProvider).UserId = winner.UserId;
        var secondUser = users.Take(2).Single(user => user != winner.UserId);
        var second = await ordinary.ServiceProvider.GetRequiredService<CreateMembershipHandler>().HandleAsync(new(tenant, secondUser, TenantRole.Owner, winner.UserId), Token);
        Assert.Equal(TenantRole.Owner, second.Role);
        var limit = await Assert.ThrowsAsync<ApplicationErrorException>(() => ordinary.ServiceProvider.GetRequiredService<CreateMembershipHandler>()
            .HandleAsync(new(tenant, users[2], TenantRole.Owner, winner.UserId), Token));
        Assert.Equal(ApplicationErrors.OwnerLimitReached, limit.Error);
        await using var verification = fixture.CreateContext(tenant);
        Assert.Equal(2, await verification.Memberships.CountAsync(membership => membership.Role == TenantRole.Owner, Token));
        Assert.Equal(2, await verification.AuditLogs.CountAsync(audit => audit.Action == AuditAction.MembershipCreated, Token));
    }

    [Fact]
    public async Task InactiveHistoricalOwnerDoesNotReopenPlatformBootstrap()
    {
        var seed = await SeedAsync();
        var user = (await UsersAsync(1))[0];
        await using var services = Services();
        await using (var scope = services.CreateAsyncScope())
        {
            Session(scope.ServiceProvider).UserId = seed.Owner.UserId;
            var denied = await Assert.ThrowsAsync<ApplicationErrorException>(() => scope.ServiceProvider.GetRequiredService<DeactivateMembershipHandler>().HandleAsync(
                new(seed.Owner.TenantId, seed.Owner.MembershipId, seed.Owner.UserId), Token));
            Assert.Equal(ApplicationErrors.LastActiveOwner, denied.Error);
        }
        await HistoricalOwnerFixture.RecordInactiveAsync(fixture, seed.Owner.TenantId, seed.Owner.MembershipId, seed.Owner.UserId, Token);
        await using (var scope = services.CreateAsyncScope())
        {
            ((PlatformSession)scope.ServiceProvider.GetRequiredService<IPlatformAdministration>()).Actor = seed.Owner.ActorId;
            var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => scope.ServiceProvider.GetRequiredService<CreateMembershipHandler>()
                .HandleAsync(new(seed.Owner.TenantId, user, TenantRole.Owner, seed.Owner.ActorId), Token));
            Assert.Equal("access.bootstrap_denied", error.Error.Code);
        }
        await using var verification = fixture.CreateContext(seed.Owner.TenantId);
        Assert.Equal(1, await verification.Memberships.CountAsync(membership => membership.Role == TenantRole.Owner, Token));
        Assert.False(await verification.Memberships.AnyAsync(membership => membership.UserId == user, Token));
    }

    [Fact]
    public async Task ImportedRowRollsBackWhenAuthorityIsRevokedBeforeItsCommit()
    {
        var seed = await SeedAsync();
        var successor = (await UsersAsync(1))[0];
        Guid category;
        await using (var seedServices = IdentityAccessTestSetup.CreateServices(fixture))
        await using (var scope = seedServices.CreateAsyncScope())
            category = (await scope.ServiceProvider.GetRequiredService<CreateCategoryHandler>().HandleAsync(new("Synthetic security category"), Token)).Id;
        await using var services = Services();
        await using var target = services.CreateAsyncScope();
        var provider = target.ServiceProvider;
        Session(provider).UserId = seed.Owner.UserId;
        await provider.GetRequiredService<CreateMembershipHandler>().HandleAsync(
            new(seed.Owner.TenantId, successor, TenantRole.Owner, seed.Owner.UserId), Token);
        var authorization = provider.GetRequiredService<ITenantOperationAuthorizer>();
        await authorization.RequireAsync(seed.Owner.TenantId, null, seed.Owner.UserId, TenantOperation.Administration, Token);
        var store = provider.GetRequiredService<IProductImportStore>();
        var job = ImportJob.Create(seed.Owner.TenantId, seed.Owner.UserId, "synthetic.xlsx", ProductImportTemplate.Version, 1, Now);
        await store.StartAsync(job, Token);
        var product = BusinessProduct.CreateLocal(seed.Owner.TenantId, "SEC-" + Guid.NewGuid().ToString("N"), ProductType.Retail,
            "Synthetic retail product", category, "Synthetic brand", null, null, 1m, null, Now);
        var unit = ProductUnit.Create(seed.Owner.TenantId, product.Id, seed.Owner.TenantId, "Unit", 1m, true);
        async Task ReauthorizeAsync(CancellationToken token)
        {
            await using var revocation = services.CreateAsyncScope();
            Session(revocation.ServiceProvider).UserId = seed.Owner.UserId;
            await revocation.ServiceProvider.GetRequiredService<DeactivateMembershipHandler>().HandleAsync(
                new(seed.Owner.TenantId, seed.Owner.MembershipId, seed.Owner.UserId), token);
            await authorization.RequireAsync(seed.Owner.TenantId, null, seed.Owner.UserId, TenantOperation.Administration, token);
        }
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => store.ImportRowAsync(job, 4, product, unit, ReauthorizeAsync, Token));
        Assert.Equal("access.membership_inactive", error.Error.Code);
        await using var verification = fixture.CreateContext(seed.Owner.TenantId);
        Assert.False(await verification.BusinessProducts.AnyAsync(value => value.Id == product.Id, Token));
        Assert.False(await verification.ProductUnits.AnyAsync(value => value.Id == unit.Id, Token));
        Assert.False(await verification.ImportRowResults.AnyAsync(value => value.ImportJobId == job.Id, Token));
    }

    private ServiceProvider Services(IInterceptor? observation = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:MediPosDatabase"] = fixture.ConnectionString }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new IdentityAccessTestSetup.Clock());
        services.AddInfrastructure(configuration);
        services.AddIdentityAuthentication<IdentityAccessTestSetup.TestGoogleIdentitySource, IdentityAccessTestSetup.TestServerSession>();
        services.AddScoped<IPlatformAdministration, PlatformSession>();
        if (observation is not null) services.AddDbContext<MediPosDbContext>(options => options.AddInterceptors(observation));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private async Task<Seed> SeedAsync()
    {
        // Historical business suites seed data through their isolated authorization fixture. Every operation
        // under test above uses the actual policy/reader/store/RLS; only server authentication is faked.
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var owner = await IdentityAccessTestSetup.CreateAsync(provider, TenantRole.Owner);
        var user = await IdentityAccessTestSetup.CreateUserAsync(provider);
        var pharmacist = await provider.GetRequiredService<CreateMembershipHandler>().HandleAsync(new(owner.TenantId, user.Id, TenantRole.Pharmacist, owner.UserId), Token);
        await provider.GetRequiredService<SetMembershipBranchesHandler>().HandleAsync(new(owner.TenantId, pharmacist.Id, [owner.BranchId], owner.UserId), Token);
        await provider.GetRequiredService<ReplaceWorkScheduleHandler>().HandleAsync(new(owner.TenantId, pharmacist.Id,
            [new(DayOfWeek.Tuesday, new(9, 0), new(18, 0))], owner.UserId), Token);
        return new(owner, user.Id, pharmacist.Id);
    }

    private async Task<Guid[]> UsersAsync(int count)
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var users = new List<Guid>();
        for (var i = 0; i < count; i++) users.Add((await IdentityAccessTestSetup.CreateUserAsync(scope.ServiceProvider)).Id);
        return users.ToArray();
    }

    private static IdentityAccessTestSetup.TestServerSession Session(IServiceProvider provider) =>
        (IdentityAccessTestSetup.TestServerSession)provider.GetRequiredService<IAuthenticatedMediPosUser>();
    private sealed record Seed(IdentityAccessTestSetup.Setup Owner, Guid PharmacistUser, Guid PharmacistMembership);
    public sealed class PlatformSession : IPlatformAdministration
    {
        public Guid? Actor { get; set; }
        public Task<Guid?> GetAuthorizedActorAsync(CancellationToken cancellationToken) => Task.FromResult(Actor);
    }
    private sealed class ObservedProvisioning : ITenantLicenseProvisioning
    {
        private readonly ITenantLicenseProvisioning _inner;
        private readonly Func<Task> _before;
        public ObservedProvisioning(ITenantLicenseProvisioning inner, Func<Task> before) { _inner = inner; _before = before; }
        public ObservedProvisioning(ITenantLicenseProvisioning inner, Action before) : this(inner, () => { before(); return Task.CompletedTask; }) { }
        public async Task<ITenantLicenseProvisioningScope?> BeginAsync(Guid tenantId, CancellationToken cancellationToken)
        { await _before(); return await _inner.BeginAsync(tenantId, cancellationToken); }
    }
    private sealed class PurchaseObservation : DbCommandInterceptor, IMaterializationInterceptor
    {
        public List<Guid> Branches { get; } = [];
        public List<string> Commands { get; } = [];
        public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
        { if (entity is Purchase purchase) Branches.Add(purchase.BranchId); return entity; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM purchases", StringComparison.Ordinal)) Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
