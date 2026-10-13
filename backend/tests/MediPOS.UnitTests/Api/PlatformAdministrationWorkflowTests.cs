using System.Reflection;
using MediPOS.Api.Authentication;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess;
using MediPOS.Application.Modules.IdentityAccess.CreateMembership;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Application.Modules.TenancyLicensing.CreateTenant;
using MediPOS.Application.Modules.TenancyLicensing.RequestTenantPurge;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.UnitTests.Api;

public sealed class PlatformAdministrationWorkflowTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("anonymous", "access.platform_denied")]
    [InlineData("unlisted", "access.platform_denied")]
    [InlineData("forged-actor", "access.actor_mismatch")]
    public async Task CreateTenantDeniesBeforePersistenceWhenIndependentAuthorityFails(string defect, string code)
    {
        var id = Guid.NewGuid(); var context = PlatformSessionFixture.Context(defect == "anonymous" ? null : id);
        var administration = PlatformSessionFixture.Administration(context, PlatformSessionFixture.Settings(defect == "unlisted" ? Guid.NewGuid() : id));
        var store = new TenantStore();
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => new CreateTenantHandler(store, TimeProvider.System, administration).HandleAsync(
            new("Synthetic tenant", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), 3, LicenseStatus.Active,
                defect == "forged-actor" ? Guid.NewGuid() : id), Token));
        Assert.Equal(code, error.Error.Code); Assert.Empty(store.Audits); Assert.Null(store.Tenant);
    }

    [Fact]
    public async Task AuthorizedPlatformCreationAndPurgeRequestRetainActorAndOnlyRequestState()
    {
        var id = Guid.NewGuid(); var administration = PlatformSessionFixture.Administration(PlatformSessionFixture.Context(id), PlatformSessionFixture.Settings(id));
        var store = new TenantStore();
        var tenant = await new CreateTenantHandler(store, TimeProvider.System, administration).HandleAsync(
            new("Synthetic tenant", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), 3, LicenseStatus.Active, id), Token);
        Assert.Equal(id, Assert.Single(store.Audits).ActorId);
        await new RequestTenantPurgeHandler(store, TimeProvider.System, administration).HandleAsync(new(tenant.TenantId, tenant.LicenseId, id), Token);
        Assert.NotNull(store.Tenant); Assert.Equal(LicenseStatus.PurgePending, store.Tenant.License.Status);
        Assert.Equal(2, store.Audits.Count); Assert.All(store.Audits, audit => Assert.Equal(id, audit.ActorId));
        Assert.Equal(id, store.Tenant.License.Changes[^1].ActorId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BootstrapNeverReopensForActiveOrInactiveHistoricalOwners(bool deactivate)
    {
        var actor = Guid.NewGuid(); var context = PlatformSessionFixture.Context(actor);
        var administration = PlatformSessionFixture.Administration(context, PlatformSessionFixture.Settings(actor));
        var state = new IdentityState(); var store = DispatchProxy.Create<IIdentityAccessStore, IdentityPort>(); ((IdentityPort)store).State = state;
        var user = new AuthenticatedMediPosUser(context);
        var tenantAuthorization = new TenantOperationAuthorizer(new(user, new NoMembership(), new TenantDataContext()), TimeProvider.System);
        var provisioning = new Provisioning(state);
        var handler = new CreateMembershipHandler(store, provisioning, TimeProvider.System, tenantAuthorization, administration);
        var created = await handler.HandleAsync(new(state.Tenant, state.User, TenantRole.Owner, actor), Token);
        Assert.Equal(actor, Assert.Single(state.Audits).ActorId); Assert.Equal(created.Id, Assert.Single(state.Memberships).Id);
        Assert.Equal(1, state.Completions);
        if (deactivate) state.Memberships[0].Deactivate(DateTimeOffset.UtcNow);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.HandleAsync(new(state.Tenant, Guid.NewGuid(), TenantRole.Owner, actor), Token));
        Assert.Equal("access.bootstrap_denied", error.Error.Code);
        Assert.Single(state.Memberships); Assert.Single(state.Audits); Assert.Equal(1, state.Completions);
    }

    [Theory]
    [InlineData(TenantRole.Cashier)]
    [InlineData(TenantRole.Pharmacist)]
    public async Task PlatformBootstrapCannotCreateOperationalEmployees(TenantRole role)
    {
        var actor = Guid.NewGuid(); var context = PlatformSessionFixture.Context(actor);
        var state = new IdentityState(); var store = DispatchProxy.Create<IIdentityAccessStore, IdentityPort>(); ((IdentityPort)store).State = state;
        var handler = new CreateMembershipHandler(store, new Provisioning(state), TimeProvider.System,
            new TenantOperationAuthorizer(new(new AuthenticatedMediPosUser(context), new NoMembership(), new TenantDataContext()), TimeProvider.System),
            PlatformSessionFixture.Administration(context, PlatformSessionFixture.Settings(actor)));
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.HandleAsync(new(state.Tenant, state.User, role, actor), Token));
        Assert.Equal("access.bootstrap_denied", error.Error.Code);
        Assert.Empty(state.Memberships); Assert.Empty(state.Audits); Assert.Equal(0, state.Completions);
    }

    private sealed class TenantStore : ITenancyLicensingStore
    {
        internal Tenant? Tenant;
        internal List<AuditLog> Audits = [];
        public Task CreateTenantAsync(Tenant tenant, AuditLog audit, CancellationToken token) { Tenant = tenant; Audits.Add(audit); return Task.CompletedTask; }
        public Task<License?> FindLicenseAsync(Guid tenant, Guid license, CancellationToken token) => Task.FromResult(Tenant?.License);
        public Task SaveLicenseAsync(License license, AuditLog audit, CancellationToken token) { Audits.Add(audit); return Task.CompletedTask; }
    }
    private sealed class NoMembership : IOperationalAccessReader
    { public Task<OperationalAccessSnapshot> ReadAsync(Guid user, Guid tenant, Guid? branch, CancellationToken token) => Task.FromResult(new OperationalAccessSnapshot(true, null, null, false, false, [])); }
    public sealed class IdentityState
    {
        internal Guid Tenant = Guid.NewGuid(); internal Guid User = Guid.NewGuid(); internal int Completions;
        internal List<Membership> Memberships = []; internal List<AuditLog> Audits = [];
    }
    public class IdentityPort : DispatchProxy
    {
        internal IdentityState State = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case nameof(IIdentityAccessStore.HasOwnerHistoryAsync): return Task.FromResult(State.Memberships.Any(member => member.Role == TenantRole.Owner));
                case nameof(IIdentityAccessStore.UserExistsAsync): return Task.FromResult(true);
                case nameof(IIdentityAccessStore.HasActiveMembershipAsync): return Task.FromResult(false);
                case nameof(IIdentityAccessStore.CountActiveOwnersAsync): return Task.FromResult(State.Memberships.Count(member => member.IsActive && member.Role == TenantRole.Owner));
                case nameof(IIdentityAccessStore.AddMembershipAsync): State.Memberships.Add((Membership)args![0]!); State.Audits.Add((AuditLog)args[1]!); return Task.CompletedTask;
                default: throw new InvalidOperationException("Unexpected persistence call in platform workflow unit fixture.");
            }
        }
    }
    private sealed class Provisioning(IdentityState state) : ITenantLicenseProvisioning
    {
        public Task<ITenantLicenseProvisioningScope?> BeginAsync(Guid tenant, CancellationToken token) => Task.FromResult<ITenantLicenseProvisioningScope?>(new Scope(state));
        private sealed class Scope(IdentityState state) : ITenantLicenseProvisioningScope
        {
            public Guid TenantId => state.Tenant; public int MaxBranches => 3;
            public bool AllowsOperation(DateTimeOffset at) => true;
            public Task CompleteAsync(CancellationToken token) { state.Completions++; return Task.CompletedTask; }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
