using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.UnitTests.Modules.IdentityAccess;

public sealed class TenantOperationAuthorizationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 14, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(TenantRole.Owner, TenantOperation.Administration, true)]
    [InlineData(TenantRole.Pharmacist, TenantOperation.Administration, false)]
    [InlineData(TenantRole.Cashier, TenantOperation.Administration, false)]
    [InlineData(TenantRole.Owner, TenantOperation.Purchasing, true)]
    [InlineData(TenantRole.Pharmacist, TenantOperation.Purchasing, true)]
    [InlineData(TenantRole.Cashier, TenantOperation.Purchasing, false)]
    [InlineData(TenantRole.Owner, TenantOperation.Inventory, true)]
    [InlineData(TenantRole.Pharmacist, TenantOperation.Inventory, true)]
    [InlineData(TenantRole.Cashier, TenantOperation.Inventory, false)]
    [InlineData(TenantRole.Owner, TenantOperation.ProductSearch, true)]
    [InlineData(TenantRole.Pharmacist, TenantOperation.ProductSearch, true)]
    [InlineData(TenantRole.Cashier, TenantOperation.ProductSearch, true)]
    public async Task EnforcesApprovedOperationMatrix(TenantRole role, TenantOperation operation, bool allowed)
    {
        var setup = new Setup(role);
        var task = setup.Policy.RequireAsync(setup.Tenant.Id, setup.Branch, setup.User.UserId, operation, TestContext.Current.CancellationToken);
        if (allowed) Assert.Equal(setup.Membership.Id, (await task).MembershipId);
        else Assert.Equal("access.role_denied", (await Assert.ThrowsAsync<ApplicationErrorException>(() => task)).Error.Code);
    }

    [Theory]
    [InlineData("inactive", "access.membership_inactive")]
    [InlineData("expired", "access.license_denied")]
    [InlineData("suspended", "access.license_denied")]
    [InlineData("schedule", "access.schedule_denied")]
    [InlineData("assignment", "access.branch_unassigned")]
    [InlineData("branch", "access.branch_denied")]
    [InlineData("tenant", "access.tenant_mismatch")]
    [InlineData("actor", "access.actor_mismatch")]
    [InlineData("unauthenticated", "access.unauthenticated")]
    public async Task RevalidatesIdentityLicenseMembershipAndBranchEveryTime(string change, string code)
    {
        var setup = new Setup(TenantRole.Pharmacist);
        await setup.Policy.RequireAsync(setup.Tenant.Id, setup.Branch, setup.User.UserId, TenantOperation.Purchasing, TestContext.Current.CancellationToken);
        switch (change)
        {
            case "inactive": setup.Membership.Deactivate(Now); break;
            case "expired": setup.Clock.Instant = setup.Tenant.License.ExpiresAt; break;
            case "suspended": setup.Tenant.License.Suspend(Guid.NewGuid(), Now); break;
            case "schedule": setup.Reader.Snapshot = setup.Reader.Snapshot with { Schedule = [] }; break;
            case "assignment": setup.Reader.Snapshot = setup.Reader.Snapshot with { BranchAssigned = false }; break;
            case "branch": setup.Reader.Snapshot = setup.Reader.Snapshot with { BranchBelongsToTenant = false }; break;
            case "tenant": setup.Reader.Snapshot = setup.Reader.Snapshot with { Membership = Membership.Create(Guid.NewGuid(), setup.User.UserId!.Value, TenantRole.Owner, Now) }; break;
            case "unauthenticated": setup.User.UserId = null; break;
        }
        var actor = change == "actor" ? Guid.NewGuid() : setup.User.UserId;
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Policy.RequireAsync(
            setup.Tenant.Id, setup.Branch, actor, TenantOperation.Purchasing, TestContext.Current.CancellationToken));
        Assert.Equal(code, error.Error.Code);
        Assert.Equal(ErrorCategory.Forbidden, error.Error.Category);
    }

    [Fact]
    public async Task BranchLookupCannotAuthorizeTheOperationOrAnUnassignedBranch()
    {
        var setup = new Setup(TenantRole.Pharmacist);
        var lookup = await setup.Policy.IdentifyForBranchLookupAsync(setup.Tenant.Id, setup.User.UserId, TenantOperation.Inventory, TestContext.Current.CancellationToken);
        Assert.Null(lookup.BranchId);
        Assert.Equal("access.branch_required", (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Policy.RequireAsync(
            setup.Tenant.Id, null, setup.User.UserId, TenantOperation.Inventory, TestContext.Current.CancellationToken))).Error.Code);
        setup.Reader.Snapshot = setup.Reader.Snapshot with { BranchAssigned = false };
        Assert.Equal("access.branch_unassigned", (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Policy.RequireAsync(
            setup.Tenant.Id, setup.Branch, setup.User.UserId, TenantOperation.Inventory, TestContext.Current.CancellationToken))).Error.Code);
    }

    [Fact]
    public async Task SameUserCanActInTwoTenantsOnlyThroughIndependentAuthorizedScopes()
    {
        var first = new Setup(TenantRole.Owner);
        var second = new Setup(TenantRole.Owner, first.User.UserId);
        var a = await first.Policy.RequireAsync(first.Tenant.Id, null, first.User.UserId, TenantOperation.Administration, TestContext.Current.CancellationToken);
        var b = await second.Policy.RequireAsync(second.Tenant.Id, null, second.User.UserId, TenantOperation.Administration, TestContext.Current.CancellationToken);
        Assert.Equal(a.UserId, b.UserId);
        Assert.NotEqual(a.TenantId, b.TenantId);
        Assert.Equal("tenant.scope_conflict", (await Assert.ThrowsAsync<ApplicationErrorException>(() => first.Policy.RequireAsync(
            second.Tenant.Id, null, first.User.UserId, TenantOperation.Administration, TestContext.Current.CancellationToken))).Error.Code);
    }

    [Fact]
    public async Task TenantOwnerDoesNotProvidePlatformAuthorityAndForgedActorIsRejected()
    {
        var owner = new Setup(TenantRole.Owner);
        await owner.Policy.RequireAsync(owner.Tenant.Id, null, owner.User.UserId, TenantOperation.Administration, TestContext.Current.CancellationToken);
        Assert.Equal("access.platform_denied", (await Assert.ThrowsAsync<ApplicationErrorException>(() => PlatformAdministrationAccess.RequireAsync(
            new PlatformAdministrationStub(null), owner.User.UserId, TestContext.Current.CancellationToken))).Error.Code);
        Assert.Equal("access.actor_mismatch", (await Assert.ThrowsAsync<ApplicationErrorException>(() => PlatformAdministrationAccess.RequireAsync(
            new PlatformAdministrationStub(Guid.NewGuid()), owner.User.UserId, TestContext.Current.CancellationToken))).Error.Code);
    }

    internal sealed class Setup
    {
        public Tenant Tenant { get; } = Tenant.Create("Synthetic pharmacy", Now.AddDays(-1), Now.AddMonths(1), 3, LicenseStatus.Active, Guid.NewGuid(), Now);
        public Guid Branch { get; } = Guid.NewGuid();
        public Membership Membership { get; }
        public Session User { get; }
        public Reader Reader { get; }
        public Clock Clock { get; } = new();
        public TenantOperationAuthorizer Policy { get; }
        public Setup(TenantRole role, Guid? user = null)
        {
            User = new() { UserId = user ?? Guid.NewGuid() };
            Membership = Membership.Create(Tenant.Id, User.UserId.Value, role, Now);
            Reader = new(new(true, Membership, Tenant.License, true, true,
                [WorkSchedule.Create(Tenant.Id, Membership.Id, Tenant.Id, DayOfWeek.Tuesday, new(9, 0), new(18, 0))]));
            Policy = new(new(User, Reader, new TenantDataContext()), Clock);
        }
    }
    internal sealed class Clock : TimeProvider
    {
        public DateTimeOffset Instant { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => Instant;
    }
    internal sealed class Session : IAuthenticatedMediPosUser { public Guid? UserId { get; set; } }
    internal sealed class Reader(OperationalAccessSnapshot snapshot) : IOperationalAccessReader
    {
        public OperationalAccessSnapshot Snapshot { get; set; } = snapshot;
        public Task<OperationalAccessSnapshot> ReadAsync(Guid userId, Guid tenantId, Guid? branchId, CancellationToken cancellationToken) => Task.FromResult(Snapshot);
    }
}
