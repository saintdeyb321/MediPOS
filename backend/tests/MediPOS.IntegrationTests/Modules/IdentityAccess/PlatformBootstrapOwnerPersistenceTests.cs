using MediPOS.Api.Authentication;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.CreateMembership;
using MediPOS.Application.Modules.IdentityAccess.DeactivateMembership;
using MediPOS.Application.Modules.IdentityAccess.UpsertGoogleUser;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Application.Modules.TenancyLicensing.CreateTenant;
using MediPOS.Application.Modules.TenancyLicensing.SuspendLicense;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.Infrastructure;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.IdentityAccess;

// External PostgreSQL 18 only. Real runtime stores/RLS/transactions and platform adapter;
// the trusted authentication feature is synthetic, not evidence of real Google validation.
[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class PlatformBootstrapOwnerPersistenceTests(PostgreSqlFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TwoSuperadminsWaitOnLicenseAndCommitExactlyOneHistoricalOwnerAndAudit()
    {
        var users = await Users(4);
        await using var services = Services(new Clock(), users[0], users[1]);
        var initial = await Tenant(services, users[0]);
        var acquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pid = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Attempt(services, initial.TenantId, users[2], users[0], async () =>
        { acquired.SetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(15), Token); });
        await acquired.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
        var second = Attempt(services, initial.TenantId, users[3], users[1], pid: pid);
        try { await WaitForDatabaseLock(await pid.Task.WaitAsync(TimeSpan.FromSeconds(15), Token)); }
        finally { release.TrySetResult(); }
        var owner = await first;
        Assert.Equal("access.bootstrap_denied", (await Assert.ThrowsAsync<ApplicationErrorException>(() => second)).Error.Code);
        // Lost-response retry uses a new scope/connection, never a fabricated successful replay.
        Assert.Equal("access.bootstrap_denied", (await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            Attempt(services, initial.TenantId, users[2], users[0]))).Error.Code);
        await using (var scope = services.CreateAsyncScope())
        {
            Session(scope.ServiceProvider, owner.UserId);
            await scope.ServiceProvider.GetRequiredService<DeactivateMembershipHandler>()
                .HandleAsync(new(initial.TenantId, owner.Id, owner.UserId), Token);
        }
        Assert.Equal("access.bootstrap_denied", (await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            Attempt(services, initial.TenantId, users[3], users[1]))).Error.Code);
        await using var verification = fixture.CreateContext(initial.TenantId);
        var member = Assert.Single(await verification.Memberships.AsNoTracking().ToListAsync(Token));
        Assert.Equal(owner.Id, member.Id); Assert.False(member.IsActive); Assert.Equal(users[2], member.UserId);
        var audit = Assert.Single(await verification.AuditLogs.Where(value => value.Action == AuditAction.MembershipCreated).ToListAsync(Token));
        Assert.Equal(users[0], audit.ActorId); Assert.Equal(owner.Id, audit.EntityId);
        Assert.Single(await verification.Licenses.SelectMany(value => value.Changes).ToListAsync(Token));
        Assert.False(await verification.BusinessProducts.AnyAsync(Token));
        Assert.False(await verification.StockMovements.AnyAsync(Token));
        Assert.False(await verification.Sales.AnyAsync(Token));
        Assert.False(await verification.CashSessions.AnyAsync(Token));
    }

    [Theory]
    [InlineData("persistence")]
    [InlineData("license_expiry")]
    [InlineData("session_expiry")]
    public async Task FailureAfterRealAuditedSaveRollsBackMembershipAndAudit(string failure)
    {
        var users = await Users(2); var clock = new Clock();
        await using var services = Services(clock, users[0]);
        var initial = await Tenant(services, users[0]);
        await using (var scope = services.CreateAsyncScope())
        {
            Session(scope.ServiceProvider, users[0]);
            var store = new AfterSaveStore(scope.ServiceProvider.GetRequiredService<IIdentityAccessStore>(), () =>
            {
                if (failure == "persistence") throw new InvalidOperationException("Synthetic persistence failure after save.");
                clock.Offset = failure == "license_expiry" ? TimeSpan.FromMinutes(6) : TimeSpan.FromHours(2);
            });
            var handler = ActivatorUtilities.CreateInstance<CreateMembershipHandler>(scope.ServiceProvider, store);
            if (failure == "persistence")
                await Assert.ThrowsAsync<InvalidOperationException>(() => handler.BootstrapOwnerAsync(initial.TenantId, users[1], users[0], Token));
            else
                Assert.Equal(failure == "license_expiry" ? "license.operation_denied" : "access.platform_denied",
                    (await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.BootstrapOwnerAsync(initial.TenantId, users[1], users[0], Token))).Error.Code);
            Assert.Empty(scope.ServiceProvider.GetRequiredService<MediPosDbContext>().ChangeTracker.Entries());
        }
        clock.Offset = TimeSpan.Zero;
        await AssertUnprovisioned(initial.TenantId);
        var result = await Attempt(services, initial.TenantId, users[1], users[0]);
        Assert.Equal(users[1], result.UserId);
        await using var verification = fixture.CreateContext(initial.TenantId);
        Assert.Single(await verification.Memberships.ToListAsync(Token));
        Assert.Single(await verification.AuditLogs.Where(value => value.Action == AuditAction.MembershipCreated).ToListAsync(Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BootstrapWaitingOnLicenseObservesCommittedSuspensionOrCurrentExpiration(bool expire)
    {
        var users = await Users(3); var clock = new Clock();
        await using var services = Services(clock, users[0], users[1]);
        var initial = await Tenant(services, users[0]);
        await using var locker = services.CreateAsyncScope(); Session(locker.ServiceProvider, users[0]);
        await using var license = await locker.ServiceProvider.GetRequiredService<ITenantLicenseProvisioning>().BeginAsync(initial.TenantId, Token);
        Assert.NotNull(license);
        if (!expire)
            await locker.ServiceProvider.GetRequiredService<SuspendLicenseHandler>().HandleAsync(new(initial.TenantId, initial.LicenseId, users[0]), Token);
        var pid = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempt = Attempt(services, initial.TenantId, users[2], users[1], pid: pid);
        try
        {
            await WaitForDatabaseLock(await pid.Task.WaitAsync(TimeSpan.FromSeconds(15), Token));
            if (expire) clock.Offset = TimeSpan.FromMinutes(6);
        }
        finally { await license.CompleteAsync(Token); }
        Assert.Equal("license.operation_denied", (await Assert.ThrowsAsync<ApplicationErrorException>(() => attempt)).Error.Code);
        clock.Offset = TimeSpan.Zero;
        await AssertUnprovisioned(initial.TenantId, expire ? 1 : 2);
        await using var verification = fixture.CreateContext(initial.TenantId);
        var persisted = await verification.Licenses.Include(value => value.Changes).SingleAsync(Token);
        Assert.Equal(expire ? LicenseStatus.Active : LicenseStatus.Suspended, persisted.Status);
        Assert.Equal(expire ? 1 : 2, persisted.Changes.Count);
    }

    [Fact]
    public async Task MissingGlobalUserWritesNothingAndDoesNotCreateAnIdentity()
    {
        var actor = (await Users(1))[0]; var missing = Guid.NewGuid();
        await using var services = Services(new Clock(), actor); var initial = await Tenant(services, actor);
        Assert.Equal("user.not_found", (await Assert.ThrowsAsync<ApplicationErrorException>(() => Attempt(services, initial.TenantId, missing, actor))).Error.Code);
        await AssertUnprovisioned(initial.TenantId);
        await using var verification = fixture.CreateContext(initial.TenantId);
        Assert.False(await verification.Users.AnyAsync(value => value.Id == missing, Token));
    }

    [Fact]
    public async Task RuntimeRoleAndForceRlsHideNewOwnerAndAuditFromAnotherTenantWithoutQueryFilters()
    {
        var users = await Users(2); await using var services = Services(new Clock(), users[0]);
        var initial = await Tenant(services, users[0]); var other = await Tenant(services, users[0]);
        var member = await Attempt(services, initial.TenantId, users[1], users[0]);
        await using var runtime = fixture.CreateContext(other.TenantId);
        Assert.True(await runtime.Database.SqlQueryRaw<bool>("""
            SELECT NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb AND NOT rolcreaterole AS "Value"
            FROM pg_roles WHERE rolname = current_user
            """).SingleAsync(Token));
        Assert.False(await runtime.Memberships.IgnoreQueryFilters().AnyAsync(value => value.Id == member.Id, Token));
        Assert.False(await runtime.AuditLogs.IgnoreQueryFilters().AnyAsync(value => value.EntityId == member.Id, Token));
        await using var own = fixture.CreateContext(initial.TenantId);
        Assert.True(await own.Memberships.IgnoreQueryFilters().AnyAsync(value => value.Id == member.Id, Token));
        Assert.Single(await own.AuditLogs.Where(value => value.Action == AuditAction.MembershipCreated).ToListAsync(Token));
    }

    private static async Task<MembershipDetails> Attempt(ServiceProvider services, Guid tenant, Guid target, Guid actor,
        Func<Task>? afterLock = null, TaskCompletionSource<int>? pid = null)
    {
        await using var scope = services.CreateAsyncScope(); Session(scope.ServiceProvider, actor);
        var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>();
        await context.Database.OpenConnectionAsync(Token);
        if (pid is not null) pid.SetResult(await context.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(Token));
        var handler = afterLock is null ? scope.ServiceProvider.GetRequiredService<CreateMembershipHandler>() :
            ActivatorUtilities.CreateInstance<CreateMembershipHandler>(scope.ServiceProvider,
                new AfterLockProvisioning(scope.ServiceProvider.GetRequiredService<ITenantLicenseProvisioning>(), afterLock));
        return await handler.BootstrapOwnerAsync(tenant, target, actor, Token);
    }

    private async Task WaitForDatabaseLock(int pid)
    {
        // Observe a database-reported blocking relationship, never infer it from a delay.
        await using var observer = fixture.CreateConstraintContext();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!await observer.Database.SqlQuery<bool>($"SELECT cardinality(pg_blocking_pids({pid})) > 0 AS \"Value\"").SingleAsync(timeout.Token))
            await Task.Delay(20, timeout.Token);
    }

    private async Task AssertUnprovisioned(Guid tenant, int expectedAudits = 1)
    {
        await using var context = fixture.CreateContext(tenant);
        Assert.Empty(await context.Memberships.ToListAsync(Token));
        var audits = await context.AuditLogs.ToListAsync(Token); Assert.Equal(expectedAudits, audits.Count);
        Assert.DoesNotContain(audits, value => value.Action == AuditAction.MembershipCreated);
    }

    private async Task<Guid[]> Users(int count)
    {
        await using var services = Services(new Clock(), Guid.NewGuid()); await using var scope = services.CreateAsyncScope();
        var source = (IdentityAccessTestSetup.TestGoogleIdentitySource)scope.ServiceProvider.GetRequiredService<IVerifiedGoogleIdentitySource>();
        var users = new List<Guid>();
        for (var index = 0; index < count; index++)
        {
            source.Identity = new(Guid.NewGuid().ToString("N"), "synthetic-bootstrap@example.test", "Synthetic bootstrap identity");
            users.Add((await scope.ServiceProvider.GetRequiredService<UpsertGoogleUserHandler>().HandleAsync(Token)).Id);
        }
        return users.ToArray();
    }

    private ServiceProvider Services(Clock clock, params Guid[] actors)
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:MediPosDatabase"] = fixture.ConnectionString }).Build());
        services.AddHttpContextAccessor();
        services.AddIdentityAuthentication<IdentityAccessTestSetup.TestGoogleIdentitySource, AuthenticatedMediPosUser>();
        services.AddSingleton(PlatformSessionFixture.Settings(actors));
        services.AddScoped<IPlatformAdministration, SessionPlatformAdministration>();
        services.AddSingleton<TimeProvider>(clock);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static void Session(IServiceProvider services, Guid actor) =>
        services.GetRequiredService<IHttpContextAccessor>().HttpContext = PlatformSessionFixture.Context(actor).HttpContext;

    private static async Task<LicenseDetails> Tenant(ServiceProvider services, Guid actor)
    {
        await using var scope = services.CreateAsyncScope(); Session(scope.ServiceProvider, actor);
        return await scope.ServiceProvider.GetRequiredService<CreateTenantHandler>().HandleAsync(
            new("Synthetic bootstrap tenant", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddMinutes(5), 3, LicenseStatus.Active, actor), Token);
    }

    private sealed class Clock : TimeProvider
    {
        public TimeSpan Offset { get; set; }
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + Offset;
    }

    private sealed class AfterLockProvisioning(ITenantLicenseProvisioning original, Func<Task> afterLock) : ITenantLicenseProvisioning
    {
        public async Task<ITenantLicenseProvisioningScope?> BeginAsync(Guid tenantId, CancellationToken cancellationToken)
        {
            var scope = await original.BeginAsync(tenantId, cancellationToken);
            try { await afterLock(); return scope; }
            catch { if (scope is not null) await scope.DisposeAsync(); throw; }
        }
    }

    private sealed class AfterSaveStore(IIdentityAccessStore original, Action afterSave) : IIdentityAccessStore
    {
        public async Task AddMembershipAsync(Membership member, AuditLog audit, CancellationToken ct)
        { await original.AddMembershipAsync(member, audit, ct); afterSave(); }
        public Task<User> UpsertGoogleUserAsync(User user, CancellationToken ct) => original.UpsertGoogleUserAsync(user, ct);
        public Task<bool> UserExistsAsync(Guid user, CancellationToken ct) => original.UserExistsAsync(user, ct);
        public Task<bool> HasActiveMembershipAsync(Guid tenant, Guid user, CancellationToken ct) => original.HasActiveMembershipAsync(tenant, user, ct);
        public Task<int> CountActiveOwnersAsync(Guid tenant, CancellationToken ct) => original.CountActiveOwnersAsync(tenant, ct);
        public Task<bool> HasOwnerHistoryAsync(Guid tenant, CancellationToken ct) => original.HasOwnerHistoryAsync(tenant, ct);
        public Task<Membership?> FindMembershipAsync(Guid tenant, Guid member, CancellationToken ct) => original.FindMembershipAsync(tenant, member, ct);
        public Task<IReadOnlyList<Guid>> FindBranchAssignmentsAsync(Guid tenant, Guid member, CancellationToken ct) => original.FindBranchAssignmentsAsync(tenant, member, ct);
        public Task<IReadOnlyList<WorkSchedule>> FindWorkScheduleAsync(Guid tenant, Guid member, CancellationToken ct) => original.FindWorkScheduleAsync(tenant, member, ct);
        public Task SaveDeactivationAsync(Membership member, AuditLog audit, CancellationToken ct) => original.SaveDeactivationAsync(member, audit, ct);
        public Task ReplaceBranchesAsync(Guid tenant, Guid member, IReadOnlyList<MembershipBranch> branches, AuditLog audit, CancellationToken ct) => original.ReplaceBranchesAsync(tenant, member, branches, audit, ct);
        public Task ReplaceScheduleAsync(Guid tenant, Guid member, IReadOnlyList<WorkSchedule> schedule, AuditLog audit, CancellationToken ct) => original.ReplaceScheduleAsync(tenant, member, schedule, audit, ct);
    }
}
