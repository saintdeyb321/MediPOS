using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.CreateMembership;
using MediPOS.Application.Modules.IdentityAccess.DeactivateMembership;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.IdentityAccess;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class OwnerConcurrencyTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task LastOwnerRejectionPreservesMembershipAndAuditedHistory()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var setup = await IdentityAccessTestSetup.CreateAsync(scope.ServiceProvider, TenantRole.Owner);
        var handler = RealDeactivation(scope.ServiceProvider, setup.UserId);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.HandleAsync(
            new(setup.TenantId, setup.MembershipId, setup.UserId), TestContext.Current.CancellationToken));
        Assert.Equal("membership.last_active_owner", error.Error.Code);
        await using var context = fixture.CreateContext(setup.TenantId);
        Assert.True((await context.Memberships.SingleAsync(value => value.Id == setup.MembershipId, TestContext.Current.CancellationToken)).IsActive);
        Assert.False(await context.AuditLogs.AnyAsync(value => value.TenantId == setup.TenantId && value.Action == AuditAction.MembershipDeactivated, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentOwnerDeactivationsSerializeAndReauthorizeAfterTheRealLock(bool mutuallyDeactivate)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        IdentityAccessTestSetup.Setup setup;
        MembershipDetails second;
        Guid secondUserId;
        await using (var scope = services.CreateAsyncScope())
        {
            setup = await IdentityAccessTestSetup.CreateAsync(scope.ServiceProvider, TenantRole.Owner);
            secondUserId = (await IdentityAccessTestSetup.CreateUserAsync(scope.ServiceProvider)).Id;
            second = await scope.ServiceProvider.GetRequiredService<CreateMembershipHandler>().HandleAsync(
                new(setup.TenantId, secondUserId, TenantRole.Owner, setup.UserId), timeout.Token);
        }
        await using var firstScope = services.CreateAsyncScope();
        await using var secondScope = services.CreateAsyncScope();
        var firstLock = new ObservedProvisioning(firstScope.ServiceProvider.GetRequiredService<ITenantLicenseProvisioning>(), true);
        var secondLock = new ObservedProvisioning(secondScope.ServiceProvider.GetRequiredService<ITenantLicenseProvisioning>(), false);
        var first = RealDeactivation(firstScope.ServiceProvider, setup.UserId, firstLock);
        var other = RealDeactivation(secondScope.ServiceProvider, secondUserId, secondLock);
        var firstTarget = mutuallyDeactivate ? second.Id : setup.MembershipId;
        var secondTarget = mutuallyDeactivate ? setup.MembershipId : second.Id;
        var firstTask = first.HandleAsync(new(setup.TenantId, firstTarget, setup.UserId), timeout.Token);
        Task<MembershipDetails>? secondTask = null;
        try
        {
            await firstLock.Acquired.Task.WaitAsync(timeout.Token);
            secondTask = other.HandleAsync(new(setup.TenantId, secondTarget, secondUserId), timeout.Token);
            await secondLock.Started.Task.WaitAsync(timeout.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
            Assert.False(secondLock.Acquired.Task.IsCompleted);
        }
        finally
        {
            firstLock.Release.TrySetResult();
            await firstTask;
        }
        Assert.NotNull(secondTask);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => secondTask);
        Assert.Equal(mutuallyDeactivate ? "access.membership_inactive" : "membership.last_active_owner", error.Error.Code);
        await using var context = fixture.CreateContext(setup.TenantId);
        Assert.Equal(1, await context.Memberships.CountAsync(value => value.TenantId == setup.TenantId && value.Role == TenantRole.Owner && value.IsActive, timeout.Token));
        var audit = await context.AuditLogs.SingleAsync(value => value.TenantId == setup.TenantId && value.Action == AuditAction.MembershipDeactivated, timeout.Token);
        Assert.Equal(firstTarget, audit.EntityId);
        Assert.Equal(setup.UserId, audit.ActorId);
    }

    [Fact]
    public async Task CommitFailureRollsBackOwnerDeactivationAndItsAuditTogether()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var setup = await IdentityAccessTestSetup.CreateAsync(scope.ServiceProvider, TenantRole.Owner);
        var secondUser = await IdentityAccessTestSetup.CreateUserAsync(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<CreateMembershipHandler>().HandleAsync(
            new(setup.TenantId, secondUser.Id, TenantRole.Owner, setup.UserId), TestContext.Current.CancellationToken);
        var provisioning = new FailCommitProvisioning(scope.ServiceProvider.GetRequiredService<ITenantLicenseProvisioning>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => RealDeactivation(scope.ServiceProvider, setup.UserId, provisioning).HandleAsync(
            new(setup.TenantId, setup.MembershipId, setup.UserId), TestContext.Current.CancellationToken));
        await using var context = fixture.CreateContext(setup.TenantId);
        Assert.Equal(2, await context.Memberships.CountAsync(value => value.TenantId == setup.TenantId && value.Role == TenantRole.Owner && value.IsActive, TestContext.Current.CancellationToken));
        Assert.False(await context.AuditLogs.AnyAsync(value => value.TenantId == setup.TenantId && value.Action == AuditAction.MembershipDeactivated, TestContext.Current.CancellationToken));
    }

    private static DeactivateMembershipHandler RealDeactivation(IServiceProvider services, Guid actor, ITenantLicenseProvisioning? provisioning = null)
    {
        ((IdentityAccessTestSetup.TestServerSession)services.GetRequiredService<IAuthenticatedMediPosUser>()).UserId = actor;
        return new(services.GetRequiredService<IIdentityAccessStore>(), provisioning ?? services.GetRequiredService<ITenantLicenseProvisioning>(),
            new IdentityAccessTestSetup.Clock(), new TenantOperationAuthorizer(services.GetRequiredService<ResolveAccessContextHandler>(), new IdentityAccessTestSetup.Clock()));
    }

    private sealed class FailCommitProvisioning(ITenantLicenseProvisioning inner) : ITenantLicenseProvisioning
    {
        public async Task<ITenantLicenseProvisioningScope?> BeginAsync(Guid tenantId, CancellationToken cancellationToken) =>
            new Scope((await inner.BeginAsync(tenantId, cancellationToken))!);
        private sealed class Scope(ITenantLicenseProvisioningScope inner) : ITenantLicenseProvisioningScope
        {
            public Guid TenantId => inner.TenantId;
            public int MaxBranches => inner.MaxBranches;
            public bool AllowsOperation(DateTimeOffset at) => inner.AllowsOperation(at);
            public Task CompleteAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("Injected pre-commit failure.");
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }

    [Fact]
    public async Task ConcurrentOwnerCreationSerializesAndCannotExceedTwoActiveOwners()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        IdentityAccessTestSetup.Setup setup;
        Guid firstUserId;
        Guid secondUserId;
        await using (var setupScope = services.CreateAsyncScope())
        {
            setup = await IdentityAccessTestSetup.CreateAsync(setupScope.ServiceProvider, TenantRole.Owner);
            firstUserId = (await IdentityAccessTestSetup.CreateUserAsync(setupScope.ServiceProvider)).Id;
            secondUserId = (await IdentityAccessTestSetup.CreateUserAsync(setupScope.ServiceProvider)).Id;
        }

        await using var firstScope = services.CreateAsyncScope();
        await using var secondScope = services.CreateAsyncScope();
        var firstLock = new ObservedProvisioning(firstScope.ServiceProvider.GetRequiredService<ITenantLicenseProvisioning>(), true);
        var secondLock = new ObservedProvisioning(secondScope.ServiceProvider.GetRequiredService<ITenantLicenseProvisioning>(), false);
        var firstHandler = new CreateMembershipHandler(firstScope.ServiceProvider.GetRequiredService<IIdentityAccessStore>(), firstLock, new IdentityAccessTestSetup.Clock(), new BusinessRuleAuthorizationStub(), new PlatformAdministrationStub(null));
        var secondHandler = new CreateMembershipHandler(secondScope.ServiceProvider.GetRequiredService<IIdentityAccessStore>(), secondLock, new IdentityAccessTestSetup.Clock(), new BusinessRuleAuthorizationStub(), new PlatformAdministrationStub(null));
        var firstTask = firstHandler.HandleAsync(new(setup.TenantId, firstUserId, TenantRole.Owner, Guid.NewGuid()), timeout.Token);
        Task<MembershipDetails>? secondTask = null;
        try
        {
            await firstLock.Acquired.Task.WaitAsync(timeout.Token);
            secondTask = secondHandler.HandleAsync(new(setup.TenantId, secondUserId, TenantRole.Owner, Guid.NewGuid()), timeout.Token);
            await secondLock.Started.Task.WaitAsync(timeout.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
            Assert.False(secondLock.Acquired.Task.IsCompleted);
        }
        finally
        {
            firstLock.Release.TrySetResult();
            await firstTask;
        }
        Assert.NotNull(secondTask);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => secondTask);
        await using var context = fixture.CreateContext(setup.TenantId);
        Assert.Equal(2, await context.Memberships.CountAsync(value =>
            value.TenantId == setup.TenantId && value.Role == TenantRole.Owner && value.IsActive, timeout.Token));
    }

    [Fact]
    public async Task DeactivationReleasesOwnerSlotAndKeepsFormerOwner()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var setup = await IdentityAccessTestSetup.CreateAsync(scope.ServiceProvider, TenantRole.Owner);
        var second = await IdentityAccessTestSetup.CreateUserAsync(scope.ServiceProvider);
        var third = await IdentityAccessTestSetup.CreateUserAsync(scope.ServiceProvider);
        var creator = scope.ServiceProvider.GetRequiredService<CreateMembershipHandler>();
        await creator.HandleAsync(new(setup.TenantId, second.Id, TenantRole.Owner, Guid.NewGuid()), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            creator.HandleAsync(new(setup.TenantId, third.Id, TenantRole.Owner, Guid.NewGuid()), TestContext.Current.CancellationToken));
        await scope.ServiceProvider.GetRequiredService<DeactivateMembershipHandler>().HandleAsync(
            new(setup.TenantId, setup.MembershipId, Guid.NewGuid()), TestContext.Current.CancellationToken);
        await creator.HandleAsync(new(setup.TenantId, third.Id, TenantRole.Owner, Guid.NewGuid()), TestContext.Current.CancellationToken);
        await using var context = fixture.CreateContext(setup.TenantId);
        Assert.Equal(3, await context.Memberships.CountAsync(value =>
            value.TenantId == setup.TenantId && value.Role == TenantRole.Owner, TestContext.Current.CancellationToken));
        Assert.Equal(2, await context.Memberships.CountAsync(value =>
            value.TenantId == setup.TenantId && value.Role == TenantRole.Owner && value.IsActive, TestContext.Current.CancellationToken));
    }

    // Wraps the real PostgreSQL row lock; there is no simulated database or lock.
    private sealed class ObservedProvisioning(ITenantLicenseProvisioning inner, bool pause) : ITenantLicenseProvisioning
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
                if (pause)
                    await Release.Task.WaitAsync(cancellationToken);
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
