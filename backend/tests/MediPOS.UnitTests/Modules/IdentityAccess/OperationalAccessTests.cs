using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.UnitTests.Modules.IdentityAccess;

public sealed class OperationalAccessTests
{
    // Tuesday 09:00 in Lima. Tests evaluate explicit instants, independent of the machine timezone.
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 14, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(TenantRole.Pharmacist)]
    [InlineData(TenantRole.Cashier)]
    public void OperationalRolesRequireBranchAssignmentAndSchedule(TenantRole role)
    {
        var setup = Create(role);
        Assert.Equal("access.branch_required", Evaluate(setup, null).Code);
        Assert.Equal("access.branch_unassigned", Evaluate(setup, setup.BranchId, setup.Snapshot with { BranchAssigned = false }).Code);
        Assert.Equal("access.schedule_denied", Evaluate(setup, setup.BranchId, setup.Snapshot with { Schedule = [] }).Code);
        var result = Evaluate(setup, setup.BranchId);
        Assert.True(result.IsAllowed);
        Assert.True(result.Context!.WithinSchedule);
        Assert.Equal(setup.Membership.Id, result.Context.MembershipId);
        Assert.Equal(setup.Membership.UserId, result.Context.UserId);
    }

    [Fact]
    public void OwnerIsTenantWideAndExemptFromAssignmentAndSchedule()
    {
        var setup = Create(TenantRole.Owner);
        var snapshot = setup.Snapshot with { BranchAssigned = false, Schedule = [] };
        Assert.True(Evaluate(setup, null, snapshot).IsAllowed);
        Assert.True(Evaluate(setup, setup.BranchId, snapshot).IsAllowed);
        Assert.Equal("access.branch_denied", Evaluate(setup, setup.BranchId, snapshot with { BranchBelongsToTenant = false }).Code);
    }

    [Theory]
    [InlineData(13, 59, false)]
    [InlineData(14, 0, true)]
    [InlineData(22, 59, true)]
    [InlineData(23, 0, false)]
    public void EvaluatesLimaWindowAtItsBoundaries(int utcHour, int minute, bool expected)
    {
        var setup = Create();
        var instant = new DateTimeOffset(2026, 10, 6, utcHour, minute, 0, TimeSpan.Zero);
        Assert.Equal(expected, EvaluateOperationalAccess.Evaluate(
            setup.Membership.UserId, setup.Membership.TenantId, setup.BranchId, instant, setup.Snapshot).IsAllowed);
    }

    [Fact]
    public void LimaDayCanDifferFromUtcDay()
    {
        var setup = Create();
        var window = WorkSchedule.Create(setup.Membership.TenantId, setup.Membership.Id, setup.Membership.TenantId,
            DayOfWeek.Monday, new(20, 0), new(23, 0));
        var snapshot = setup.Snapshot with { Schedule = [window] };
        var instant = new DateTimeOffset(2026, 10, 6, 2, 0, 0, TimeSpan.Zero); // Monday 21:00 Lima.
        Assert.True(EvaluateOperationalAccess.Evaluate(
            setup.Membership.UserId, setup.Membership.TenantId, setup.BranchId, instant, snapshot).IsAllowed);
        Assert.False(EvaluateOperationalAccess.Evaluate(
            setup.Membership.UserId, setup.Membership.TenantId, setup.BranchId, instant.AddDays(1), snapshot).IsAllowed);
    }

    [Fact]
    public void ScheduleForAnotherMembershipOrTenantCannotAuthorize()
    {
        var setup = Create();
        var foreignMembershipWindow = WorkSchedule.Create(setup.Membership.TenantId, Guid.NewGuid(), setup.Membership.TenantId,
            DayOfWeek.Tuesday, new(9, 0), new(18, 0));
        var foreignTenant = Guid.NewGuid();
        var foreignTenantWindow = WorkSchedule.Create(foreignTenant, setup.Membership.Id, foreignTenant,
            DayOfWeek.Tuesday, new(9, 0), new(18, 0));
        Assert.Equal("access.schedule_denied",
            Evaluate(setup, setup.BranchId, setup.Snapshot with { Schedule = [foreignMembershipWindow, foreignTenantWindow] }).Code);
    }

    [Theory]
    [InlineData(LicenseStatus.Suspended)]
    [InlineData(LicenseStatus.Cancelled)]
    [InlineData(LicenseStatus.PurgePending)]
    [InlineData(LicenseStatus.Purged)]
    public void LicenseDeniesEvenOwners(LicenseStatus status)
    {
        var setup = Create(TenantRole.Owner, status);
        Assert.Equal("access.license_denied", Evaluate(setup, null).Code);
    }

    [Theory]
    [InlineData(LicenseStatus.Active)]
    [InlineData(LicenseStatus.Trial)]
    [InlineData(LicenseStatus.Grace)]
    public void OperationalLicenseStatusesCanAuthorize(LicenseStatus status) =>
        Assert.True(Evaluate(Create(TenantRole.Cashier, status)).IsAllowed);

    [Fact]
    public void LicensePeriodAndTenantMustMatch()
    {
        var setup = Create();
        var expired = Tenant.Create("Expired", Now.AddDays(-1), Now, 3, LicenseStatus.Active, Guid.NewGuid(), Now).License;
        // Expired license also belongs to another tenant: neither mismatch nor invalid period grants access.
        Assert.Equal("access.license_denied", Evaluate(setup, setup.BranchId, setup.Snapshot with { License = expired }).Code);
        var license = setup.Snapshot.License!;
        Assert.False(EvaluateOperationalAccess.Evaluate(setup.Membership.UserId, setup.Membership.TenantId,
            setup.BranchId, license.ExpiresAt, setup.Snapshot).IsAllowed);
        Assert.False(EvaluateOperationalAccess.Evaluate(setup.Membership.UserId, setup.Membership.TenantId,
            setup.BranchId, license.StartsAt.AddTicks(-1), setup.Snapshot).IsAllowed);
        Assert.Equal("access.license_denied", Evaluate(setup, setup.BranchId, setup.Snapshot with { License = null }).Code);
    }

    [Theory]
    [InlineData(TenantRole.Owner)]
    [InlineData(TenantRole.Cashier)]
    public void InactiveMembershipAlwaysDenies(TenantRole role)
    {
        var setup = Create(role);
        setup.Membership.Deactivate(Now);
        var result = Evaluate(setup);
        Assert.False(result.IsAllowed);
        Assert.Equal("access.membership_inactive", result.Code);
        Assert.False(result.Context!.MembershipIsActive);
    }

    [Fact]
    public void MissingUserMembershipForeignTenantAndForeignUserDeny()
    {
        var setup = Create();
        Assert.Equal("access.user_missing", Evaluate(setup, setup.BranchId, setup.Snapshot with { UserExists = false }).Code);
        Assert.Equal("access.membership_missing", Evaluate(setup, setup.BranchId, setup.Snapshot with { Membership = null }).Code);
        Assert.Equal("access.tenant_mismatch", EvaluateOperationalAccess.Evaluate(
            setup.Membership.UserId, Guid.NewGuid(), setup.BranchId, Now, setup.Snapshot).Code);
        Assert.Equal("access.tenant_mismatch", EvaluateOperationalAccess.Evaluate(
            Guid.NewGuid(), setup.Membership.TenantId, setup.BranchId, Now, setup.Snapshot).Code);
        Assert.Equal("access.branch_denied", Evaluate(setup, setup.BranchId, setup.Snapshot with { BranchBelongsToTenant = false }).Code);
    }

    [Fact]
    public async Task ResolverGetsIdentityFromServerSessionAndOnlySelectsRequestedTenantBranch()
    {
        var setup = Create();
        var reader = new RecordingReader(setup.Snapshot);
        var handler = new ResolveAccessContextHandler(new Session(setup.Membership.UserId), reader);
        Assert.True((await handler.HandleAsync(setup.Membership.TenantId, setup.BranchId, Now, TestContext.Current.CancellationToken)).IsAllowed);
        Assert.Equal((setup.Membership.UserId, setup.Membership.TenantId, (Guid?)setup.BranchId), reader.Selection);
    }

    [Fact]
    public async Task UnauthenticatedSessionDoesNotQueryPrivateData()
    {
        var setup = Create();
        var reader = new RecordingReader(setup.Snapshot);
        var handler = new ResolveAccessContextHandler(new Session(null), reader);
        Assert.Equal("access.unauthenticated",
            (await handler.HandleAsync(setup.Membership.TenantId, setup.BranchId, Now, TestContext.Current.CancellationToken)).Code);
        Assert.Null(reader.Selection);
    }

    private static OperationalAccessResult Evaluate(Setup setup) => Evaluate(setup, setup.BranchId);

    private static OperationalAccessResult Evaluate(Setup setup, Guid? branchId, OperationalAccessSnapshot? snapshot = null) =>
        EvaluateOperationalAccess.Evaluate(setup.Membership.UserId, setup.Membership.TenantId, branchId, Now, snapshot ?? setup.Snapshot);

    private static Setup Create(TenantRole role = TenantRole.Cashier, LicenseStatus status = LicenseStatus.Active)
    {
        var tenant = Tenant.Create("Botica", Now.AddDays(-1), Now.AddMonths(1), 3, status, Guid.NewGuid(), Now.AddDays(-1));
        var membership = Membership.Create(tenant.Id, Guid.NewGuid(), role, Now.AddHours(-1));
        var window = WorkSchedule.Create(tenant.Id, membership.Id, tenant.Id, DayOfWeek.Tuesday, new(9, 0), new(18, 0));
        return new Setup(membership, Guid.NewGuid(), new OperationalAccessSnapshot(true, membership, tenant.License, true, true, [window]));
    }

    private sealed record Setup(Membership Membership, Guid BranchId, OperationalAccessSnapshot Snapshot);
    private sealed record Session(Guid? UserId) : IAuthenticatedMediPosUser;

    private sealed class RecordingReader(OperationalAccessSnapshot snapshot) : IOperationalAccessReader
    {
        public (Guid UserId, Guid TenantId, Guid? BranchId)? Selection { get; private set; }
        public Task<OperationalAccessSnapshot> ReadAsync(Guid userId, Guid tenantId, Guid? branchId, CancellationToken cancellationToken)
        {
            Selection = (userId, tenantId, branchId);
            return Task.FromResult(snapshot);
        }
    }
}
