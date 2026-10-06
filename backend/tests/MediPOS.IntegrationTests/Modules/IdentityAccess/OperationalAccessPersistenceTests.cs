using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.CreateMembership;
using MediPOS.Application.Modules.IdentityAccess.DeactivateMembership;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.IdentityAccess.ReplaceWorkSchedule;
using MediPOS.Application.Modules.IdentityAccess.SetMembershipBranches;
using MediPOS.Application.Modules.TenancyLicensing.ReactivateLicense;
using MediPOS.Application.Modules.TenancyLicensing.SuspendLicense;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.IdentityAccess;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class OperationalAccessPersistenceTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task ResolverScopesMembershipBranchAssignmentAndScheduleByUserAndTenant()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var first = await IdentityAccessTestSetup.CreateAsync(scope.ServiceProvider);
        var other = await IdentityAccessTestSetup.CreateAsync(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<SetMembershipBranchesHandler>().HandleAsync(
            new(first.TenantId, first.MembershipId, [first.BranchId]), TestContext.Current.CancellationToken);
        await scope.ServiceProvider.GetRequiredService<ReplaceWorkScheduleHandler>().HandleAsync(
            new(first.TenantId, first.MembershipId, [new(DayOfWeek.Tuesday, new(9, 0), new(18, 0))]), TestContext.Current.CancellationToken);
        var session = (IdentityAccessTestSetup.TestServerSession)scope.ServiceProvider.GetRequiredService<IAuthenticatedMediPosUser>();
        session.UserId = first.UserId;
        var resolver = scope.ServiceProvider.GetRequiredService<ResolveAccessContextHandler>();
        Assert.True((await resolver.HandleAsync(first.TenantId, first.BranchId, IdentityAccessTestSetup.Now, TestContext.Current.CancellationToken)).IsAllowed);
        Assert.Equal("access.branch_denied",
            (await resolver.HandleAsync(first.TenantId, other.BranchId, IdentityAccessTestSetup.Now, TestContext.Current.CancellationToken)).Code);
        Assert.Equal("access.membership_missing",
            (await resolver.HandleAsync(other.TenantId, other.BranchId, IdentityAccessTestSetup.Now, TestContext.Current.CancellationToken)).Code);

        // Same global user can belong to two tenants; the first tenant's schedule/assignment must not leak.
        var secondMembership = await scope.ServiceProvider.GetRequiredService<CreateMembershipHandler>().HandleAsync(
            new(other.TenantId, first.UserId, TenantRole.Pharmacist), TestContext.Current.CancellationToken);
        Assert.Equal("access.branch_unassigned",
            (await resolver.HandleAsync(other.TenantId, other.BranchId, IdentityAccessTestSetup.Now, TestContext.Current.CancellationToken)).Code);
        await scope.ServiceProvider.GetRequiredService<SetMembershipBranchesHandler>().HandleAsync(
            new(other.TenantId, secondMembership.Id, [other.BranchId]), TestContext.Current.CancellationToken);
        Assert.Equal("access.schedule_denied",
            (await resolver.HandleAsync(other.TenantId, other.BranchId, IdentityAccessTestSetup.Now, TestContext.Current.CancellationToken)).Code);
        var snapshot = await scope.ServiceProvider.GetRequiredService<IOperationalAccessReader>().ReadAsync(
            first.UserId, other.TenantId, other.BranchId, TestContext.Current.CancellationToken);
        Assert.Empty(snapshot.Schedule);
        Assert.Equal(secondMembership.Id, snapshot.Membership!.Id);

        session.UserId = other.UserId;
        Assert.Equal("access.membership_missing",
            (await resolver.HandleAsync(first.TenantId, first.BranchId, IdentityAccessTestSetup.Now, TestContext.Current.CancellationToken)).Code);
        session.UserId = Guid.NewGuid();
        Assert.Equal("access.user_missing",
            (await resolver.HandleAsync(first.TenantId, first.BranchId, IdentityAccessTestSetup.Now, TestContext.Current.CancellationToken)).Code);
    }

    [Fact]
    public async Task ResolverReadsCurrentLicenseAndMembershipDespitePreviouslyTrackedEntities()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var setup = await IdentityAccessTestSetup.CreateAsync(scope.ServiceProvider, TenantRole.Owner);
        ((IdentityAccessTestSetup.TestServerSession)scope.ServiceProvider.GetRequiredService<IAuthenticatedMediPosUser>()).UserId = setup.UserId;
        var resolver = scope.ServiceProvider.GetRequiredService<ResolveAccessContextHandler>();
        Assert.True((await resolver.HandleAsync(setup.TenantId, null, IdentityAccessTestSetup.Now, TestContext.Current.CancellationToken)).IsAllowed);
        await scope.ServiceProvider.GetRequiredService<SuspendLicenseHandler>().HandleAsync(
            new(setup.TenantId, setup.LicenseId, setup.UserId), TestContext.Current.CancellationToken);
        Assert.Equal("access.license_denied",
            (await resolver.HandleAsync(setup.TenantId, null, IdentityAccessTestSetup.Now, TestContext.Current.CancellationToken)).Code);
        await scope.ServiceProvider.GetRequiredService<ReactivateLicenseHandler>().HandleAsync(
            new(setup.TenantId, setup.LicenseId, LicenseStatus.Active, setup.UserId), TestContext.Current.CancellationToken);
        Assert.True((await resolver.HandleAsync(setup.TenantId, null, IdentityAccessTestSetup.Now, TestContext.Current.CancellationToken)).IsAllowed);
        await scope.ServiceProvider.GetRequiredService<DeactivateMembershipHandler>().HandleAsync(
            new(setup.TenantId, setup.MembershipId), TestContext.Current.CancellationToken);
        Assert.Equal("access.membership_inactive",
            (await resolver.HandleAsync(setup.TenantId, null, IdentityAccessTestSetup.Now, TestContext.Current.CancellationToken)).Code);
    }

    [Fact]
    public async Task NewActiveMembershipWinsOverRetainedInactiveMembership()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var setup = await IdentityAccessTestSetup.CreateAsync(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<DeactivateMembershipHandler>().HandleAsync(
            new(setup.TenantId, setup.MembershipId), TestContext.Current.CancellationToken);
        var replacement = await scope.ServiceProvider.GetRequiredService<CreateMembershipHandler>().HandleAsync(
            new(setup.TenantId, setup.UserId, TenantRole.Owner), TestContext.Current.CancellationToken);
        var snapshot = await scope.ServiceProvider.GetRequiredService<IOperationalAccessReader>().ReadAsync(
            setup.UserId, setup.TenantId, null, TestContext.Current.CancellationToken);
        Assert.Equal(replacement.Id, snapshot.Membership!.Id);
        Assert.True(EvaluateOperationalAccess.Evaluate(setup.UserId, setup.TenantId, null, IdentityAccessTestSetup.Now, snapshot).IsAllowed);
    }
}
