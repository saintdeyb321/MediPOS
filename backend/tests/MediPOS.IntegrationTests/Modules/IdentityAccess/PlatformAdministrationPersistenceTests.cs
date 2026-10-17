using MediPOS.Api.Authentication;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.CreateMembership;
using MediPOS.Application.Modules.IdentityAccess.DeactivateMembership;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.IdentityAccess.UpsertGoogleUser;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Application.Modules.TenancyLicensing.CreateTenant;
using MediPOS.Application.Modules.TenancyLicensing.ReactivateLicense;
using MediPOS.Application.Modules.TenancyLicensing.RenewLicense;
using MediPOS.Application.Modules.TenancyLicensing.RequestTenantPurge;
using MediPOS.Application.Modules.TenancyLicensing.SuspendLicense;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.Infrastructure;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.IdentityAccess;

// Prepared for external PostgreSQL only: real restricted runtime stores/transactions/RLS,
// actual platform and tenant adapters; the completed authentication feature is synthetic.
[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class PlatformAdministrationPersistenceTests(PostgreSqlFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ConcurrentBootstrapRecordsOneAuthenticatedActorAndHistoricalOwnerCannotReopenIt()
    {
        var users = await Users(3);
        await using var services = Services(users[0]);
        Guid tenant;
        await using (var scope = services.CreateAsyncScope())
        {
            Session(scope.ServiceProvider, users[0]);
            tenant = (await CreateTenant(scope.ServiceProvider, users[0])).TenantId;
            var denied = await Assert.ThrowsAsync<ApplicationErrorException>(() => scope.ServiceProvider.GetRequiredService<ITenantOperationAuthorizer>()
                .RequireAsync(tenant, null, users[0], TenantOperation.Administration, Token));
            Assert.Equal("access.membership_missing", denied.Error.Code);
        }
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        async Task<(MembershipDetails? Member, string? Error)> Attempt(Guid target)
        {
            await using var scope = services.CreateAsyncScope(); Session(scope.ServiceProvider, users[0]);
            var provisioning = new ConcurrentProvisioning(scope.ServiceProvider.GetRequiredService<ITenantLicenseProvisioning>(), async () =>
            {
                if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
            });
            var handler = ActivatorUtilities.CreateInstance<CreateMembershipHandler>(scope.ServiceProvider, provisioning);
            try { return (await handler.HandleAsync(new(tenant, target, TenantRole.Owner, users[0]), Token), null); }
            catch (ApplicationErrorException error) { return (null, error.Error.Code); }
        }
        var results = await Task.WhenAll(Attempt(users[1]), Attempt(users[2]));
        var owner = Assert.Single(results, result => result.Member is not null).Member!;
        Assert.Equal("access.bootstrap_denied", Assert.Single(results, result => result.Error is not null).Error);
        await using (var scope = services.CreateAsyncScope())
        {
            Session(scope.ServiceProvider, owner.UserId);
            Assert.Null(await scope.ServiceProvider.GetRequiredService<IPlatformAdministration>().GetAuthorizedActorAsync(Token));
            var denied = await Assert.ThrowsAsync<ApplicationErrorException>(() => scope.ServiceProvider.GetRequiredService<DeactivateMembershipHandler>()
                .HandleAsync(new(tenant, owner.Id, owner.UserId), Token));
            Assert.Equal(ApplicationErrors.LastActiveOwner, denied.Error);
        }
        await HistoricalOwnerFixture.RecordInactiveAsync(fixture, tenant, owner.Id, owner.UserId, Token);
        await using (var scope = services.CreateAsyncScope())
        {
            Session(scope.ServiceProvider, users[0]);
            var denied = await Assert.ThrowsAsync<ApplicationErrorException>(() => scope.ServiceProvider.GetRequiredService<CreateMembershipHandler>()
                .HandleAsync(new(tenant, users[0], TenantRole.Owner, users[0]), Token));
            Assert.Equal("access.bootstrap_denied", denied.Error.Code);
        }
        await using var verification = fixture.CreateContext(tenant);
        var membership = Assert.Single(await verification.Memberships.AsNoTracking().ToListAsync(Token));
        Assert.False(membership.IsActive);
        var audit = Assert.Single(await verification.AuditLogs.Where(value => value.Action == AuditAction.MembershipCreated).ToListAsync(Token));
        Assert.Equal(users[0], audit.ActorId); Assert.Equal(owner.Id, audit.EntityId);
        Assert.Equal(users[0], (await verification.AuditLogs.SingleAsync(value => value.Action == AuditAction.TenantCreated, Token)).ActorId);
    }

    [Fact]
    public async Task ActorMismatchAndEffectiveAllowlistRevocationProduceNoTenantOrAuditWrites()
    {
        var users = await Users(2); var name = "Synthetic platform denied " + Guid.NewGuid().ToString("N");
        Guid existingTenant;
        await using (var services = Services(users[0]))
        await using (var scope = services.CreateAsyncScope())
        {
            Session(scope.ServiceProvider, users[0]);
            existingTenant = (await CreateTenant(scope.ServiceProvider, users[0])).TenantId;
            var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => CreateTenant(scope.ServiceProvider, users[1], name));
            Assert.Equal("access.actor_mismatch", error.Error.Code);
        }
        await using (var services = Services(users[1]))
        await using (var scope = services.CreateAsyncScope())
        {
            Session(scope.ServiceProvider, users[0]);
            var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => CreateTenant(scope.ServiceProvider, users[0], name));
            Assert.Equal("access.platform_denied", error.Error.Code);
        }
        await using var verification = fixture.CreateContext(existingTenant);
        Assert.False(await verification.Tenants.AnyAsync(value => value.TradingName == name, Token));
        var unchanged = Assert.Single(await verification.AuditLogs.ToListAsync(Token));
        Assert.Equal(AuditAction.TenantCreated, unchanged.Action); Assert.Equal(users[0], unchanged.ActorId);
        Assert.Equal(2, await verification.Users.CountAsync(value => users.Contains(value.Id), Token));
    }

    [Fact]
    public async Task AuthorizedPurgeRequestPreservesTenantUserAndLicenseHistoryWithoutPhysicalPurge()
    {
        var actor = (await Users(1))[0]; await using var services = Services(actor); await using var scope = services.CreateAsyncScope();
        Session(scope.ServiceProvider, actor);
        var tenant = await CreateTenant(scope.ServiceProvider, actor);
        var purged = await scope.ServiceProvider.GetRequiredService<RequestTenantPurgeHandler>().HandleAsync(new(tenant.TenantId, tenant.LicenseId, actor), Token);
        Assert.Equal(LicenseStatus.PurgePending, purged.Status);
        await using var verification = fixture.CreateContext(tenant.TenantId);
        Assert.True(await verification.Tenants.AnyAsync(value => value.Id == tenant.TenantId, Token));
        Assert.True(await verification.Users.AnyAsync(value => value.Id == actor, Token));
        var license = await verification.Licenses.Include(value => value.Changes).SingleAsync(Token);
        Assert.Equal(2, license.Changes.Count); Assert.All(license.Changes, change => Assert.Equal(actor, change.ActorId));
        Assert.Equal(2, await verification.AuditLogs.CountAsync(Token));
        Assert.Equal(actor, (await verification.AuditLogs.SingleAsync(value => value.Action == AuditAction.TenantPurgeRequested, Token)).ActorId);
    }

    [Fact]
    public async Task LicenseMutationRetriesRetainExistingIdempotencyConflictsAndAuthenticatedAudit()
    {
        var actor = (await Users(1))[0]; await using var services = Services(actor);
        LicenseDetails initial;
        await using (var scope = services.CreateAsyncScope())
        { Session(scope.ServiceProvider, actor); initial = await CreateTenant(scope.ServiceProvider, actor); }
        async Task Mutate(Func<IServiceProvider, Task> operation)
        { await using var scope = services.CreateAsyncScope(); Session(scope.ServiceProvider, actor); await operation(scope.ServiceProvider); }
        var expiration = initial.ExpiresAt.AddDays(10);
        for (var repeat = 0; repeat < 2; repeat++)
            await Mutate(provider => provider.GetRequiredService<RenewLicenseHandler>().HandleAsync(new(initial.TenantId, initial.LicenseId, expiration, actor), Token));
        for (var repeat = 0; repeat < 2; repeat++)
            await Mutate(provider => provider.GetRequiredService<SuspendLicenseHandler>().HandleAsync(new(initial.TenantId, initial.LicenseId, actor), Token));
        await Mutate(provider => provider.GetRequiredService<ReactivateLicenseHandler>().HandleAsync(new(initial.TenantId, initial.LicenseId, LicenseStatus.Active, actor), Token));
        await Mutate(async provider =>
        {
            var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => provider.GetRequiredService<ReactivateLicenseHandler>()
                .HandleAsync(new(initial.TenantId, initial.LicenseId, LicenseStatus.Active, actor), Token));
            Assert.Equal(ApplicationErrors.LicenseStateConflict, error.Error);
        });
        await using var verification = fixture.CreateContext(initial.TenantId);
        var license = await verification.Licenses.Include(value => value.Changes).SingleAsync(Token);
        Assert.Equal(expiration, license.ExpiresAt); Assert.Equal(LicenseStatus.Active, license.Status); Assert.Equal(4, license.Changes.Count);
        var audits = await verification.AuditLogs.ToListAsync(Token); Assert.Equal(4, audits.Count); Assert.All(audits, audit => Assert.Equal(actor, audit.ActorId));
    }

    [Fact]
    public async Task ConcurrentRenewalLoserRollsBackBothAppendedHistoryAndAuditWithRestrictedRuntime()
    {
        var actor = (await Users(1))[0]; await using var services = Services(actor);
        LicenseDetails initial;
        await using (var scope = services.CreateAsyncScope())
        { Session(scope.ServiceProvider, actor); initial = await CreateTenant(scope.ServiceProvider, actor); }
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var arrivals = 0;
        async Task<bool> Attempt(int days)
        {
            await using var scope = services.CreateAsyncScope(); Session(scope.ServiceProvider, actor);
            var store = new ConcurrentLicenseStore(scope.ServiceProvider.GetRequiredService<ITenancyLicensingStore>(), async () =>
            {
                if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
            });
            var handler = new RenewLicenseHandler(store, TimeProvider.System, scope.ServiceProvider.GetRequiredService<IPlatformAdministration>());
            try { await handler.HandleAsync(new(initial.TenantId, initial.LicenseId, initial.ExpiresAt.AddDays(days), actor), Token); return true; }
            catch (ApplicationErrorException error) { Assert.Equal(ApplicationErrors.LicenseConcurrency, error.Error); return false; }
        }
        var outcomes = await Task.WhenAll(Attempt(10), Attempt(20)); Assert.Single(outcomes, success => success); Assert.Single(outcomes, success => !success);
        await using var verification = fixture.CreateContext(initial.TenantId);
        var license = await verification.Licenses.Include(value => value.Changes).SingleAsync(Token);
        Assert.Equal(2, license.Changes.Count);
        Assert.True(license.ExpiresAt == initial.ExpiresAt.AddDays(10) || license.ExpiresAt == initial.ExpiresAt.AddDays(20));
        var audits = await verification.AuditLogs.ToListAsync(Token); Assert.Equal(2, audits.Count); Assert.All(audits, audit => Assert.Equal(actor, audit.ActorId));
        Assert.Single(audits, audit => audit.Action == AuditAction.LicenseRenewed);
    }

    private sealed class ConcurrentLicenseStore(ITenancyLicensingStore original, Func<Task> ready) : ITenancyLicensingStore
    {
        public Task CreateTenantAsync(Tenant tenant, AuditLog audit, CancellationToken cancellationToken) => original.CreateTenantAsync(tenant, audit, cancellationToken);
        public async Task<License?> FindLicenseAsync(Guid tenantId, Guid licenseId, CancellationToken cancellationToken)
        { var license = await original.FindLicenseAsync(tenantId, licenseId, cancellationToken); await ready(); return license; }
        public Task SaveLicenseAsync(License license, AuditLog audit, CancellationToken cancellationToken) => original.SaveLicenseAsync(license, audit, cancellationToken);
    }

    private async Task<Guid[]> Users(int count)
    {
        await using var services = Services(Guid.NewGuid()); await using var scope = services.CreateAsyncScope(); var result = new List<Guid>();
        for (var index = 0; index < count; index++)
        {
            var source = (IdentityAccessTestSetup.TestGoogleIdentitySource)scope.ServiceProvider.GetRequiredService<IVerifiedGoogleIdentitySource>();
            source.Identity = new(Guid.NewGuid().ToString("N"), "synthetic-platform@example.test", "Synthetic platform identity");
            result.Add((await scope.ServiceProvider.GetRequiredService<UpsertGoogleUserHandler>().HandleAsync(Token)).Id);
        }
        return result.ToArray();
    }
    private ServiceProvider Services(Guid approved)
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:MediPosDatabase"] = fixture.ConnectionString }).Build());
        services.AddHttpContextAccessor();
        services.AddIdentityAuthentication<IdentityAccessTestSetup.TestGoogleIdentitySource, AuthenticatedMediPosUser>();
        services.AddSingleton(PlatformSessionFixture.Settings(approved));
        services.AddScoped<IPlatformAdministration, SessionPlatformAdministration>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
    private static void Session(IServiceProvider services, Guid actor) =>
        services.GetRequiredService<IHttpContextAccessor>().HttpContext = PlatformSessionFixture.Context(actor).HttpContext;
    private static Task<MediPOS.Application.Modules.TenancyLicensing.LicenseDetails> CreateTenant(IServiceProvider services, Guid actor, string name = "Synthetic platform tenant") =>
        services.GetRequiredService<CreateTenantHandler>().HandleAsync(new(name, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), 3, LicenseStatus.Active, actor), Token);
    private sealed class ConcurrentProvisioning(ITenantLicenseProvisioning original, Func<Task> ready) : ITenantLicenseProvisioning
    {
        public async Task<ITenantLicenseProvisioningScope?> BeginAsync(Guid tenantId, CancellationToken cancellationToken)
        { await ready(); return await original.BeginAsync(tenantId, cancellationToken); }
    }
}
