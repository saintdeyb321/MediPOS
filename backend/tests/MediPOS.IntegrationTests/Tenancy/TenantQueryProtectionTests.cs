using System.Data;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Branches;
using MediPOS.Application.Modules.IdentityAccess;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.Branches;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Tenancy;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class TenantQueryProtectionTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task NormalQueriesAndFindOnlyReturnSelectedTenantWithoutManualPredicates()
    {
        var (first, second) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var context = fixture.CreateContext(first.TenantId);
        AssertOnlyTenant(await context.Licenses.Select(value => value.TenantId).ToListAsync(TestContext.Current.CancellationToken), first.TenantId);
        AssertOnlyTenant(await context.LicenseChanges.Select(value => value.TenantId).ToListAsync(TestContext.Current.CancellationToken), first.TenantId);
        AssertOnlyTenant(await context.LegalEntities.Select(value => value.TenantId).ToListAsync(TestContext.Current.CancellationToken), first.TenantId);
        AssertOnlyTenant(await context.Branches.Select(value => value.TenantId).ToListAsync(TestContext.Current.CancellationToken), first.TenantId);
        AssertOnlyTenant(await context.Memberships.Select(value => value.TenantId).ToListAsync(TestContext.Current.CancellationToken), first.TenantId);
        AssertOnlyTenant(await context.MembershipBranches.Select(value => value.TenantId).ToListAsync(TestContext.Current.CancellationToken), first.TenantId);
        AssertOnlyTenant(await context.WorkSchedules.Select(value => value.TenantId).ToListAsync(TestContext.Current.CancellationToken), first.TenantId);
        AssertOnlyTenant(await context.AuditLogs.Select(value => value.TenantId).ToListAsync(TestContext.Current.CancellationToken), first.TenantId);
        AssertOnlyTenant(await context.BusinessProducts.Select(value => value.TenantId).ToListAsync(TestContext.Current.CancellationToken), first.TenantId);
        AssertOnlyTenant(await context.ProductUnits.Select(value => value.TenantId).ToListAsync(TestContext.Current.CancellationToken), first.TenantId);
        Assert.Null(await context.BusinessProducts.FindAsync([second.BusinessProductId], TestContext.Current.CancellationToken));
        Assert.Null(await context.Branches.FindAsync([second.Identity.BranchId], TestContext.Current.CancellationToken));
        Assert.Null(await context.Memberships.FindAsync([second.Identity.MembershipId], TestContext.Current.CancellationToken));
        Assert.Null(await context.WorkSchedules.FindAsync([second.ScheduleId], TestContext.Current.CancellationToken));

        // Independent filter check while RLS is absent for the superuser: this is not an RLS assertion.
        await using var filterOnly = fixture.CreateConstraintContext(first.TenantId);
        var rows = await filterOnly.Branches.ToListAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(rows);
        Assert.All(rows, value => Assert.Equal(first.TenantId, value.TenantId));
    }

    [Fact]
    public async Task MissingDataScopeReadsNoPrivateDataButGlobalUsersAndPlatformRootRemainAvailable()
    {
        var (first, second) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var context = fixture.CreateContext();
        Assert.Empty(await context.Licenses.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.LicenseChanges.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.LegalEntities.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.Branches.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.Memberships.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.MembershipBranches.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.WorkSchedules.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.AuditLogs.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.BusinessProducts.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.ProductUnits.ToListAsync(TestContext.Current.CancellationToken));
        Assert.True(await context.Categories.AnyAsync(value => value.Id == first.CategoryId, TestContext.Current.CancellationToken));
        Assert.True(await context.Users.AnyAsync(value => value.Id == first.Identity.UserId, TestContext.Current.CancellationToken));
        Assert.True(await context.Users.AnyAsync(value => value.Id == second.Identity.UserId, TestContext.Current.CancellationToken));
        Assert.True(await context.Tenants.AnyAsync(value => value.Id == first.TenantId, TestContext.Current.CancellationToken));
        // EF filtering must also fail closed independently of RLS.
        await using var filterOnly = fixture.CreateConstraintContext();
        Assert.Empty(await filterOnly.Branches.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveChangesRejectsMissingScopeForeignWritesAndOwnershipChangesBeforeDatabaseAccess()
    {
        var (first, second) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var context = fixture.CreateContext(first.TenantId);
        context.Branches.Add(Branch.Create(second.TenantId, second.LegalEntityId, second.TenantId, "Foreign", IdentityAccessTestSetup.Now));
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        context.ChangeTracker.Clear();
        await using var foreignContext = fixture.CreateContext(second.TenantId);
        var foreign = await foreignContext.Branches.AsNoTracking().SingleAsync(value =>
            value.Id == second.Identity.BranchId, TestContext.Current.CancellationToken);
        context.Branches.Attach(foreign);
        foreign.MarkAsMainHub();
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        context.ChangeTracker.Clear();
        context.Branches.Remove(foreign);
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        context.ChangeTracker.Clear();

        // Both original and current tenant ownership matter for entities whose tenant_id is not a key.
        var window = await foreignContext.WorkSchedules.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        context.WorkSchedules.Attach(window);
        context.Entry(window).Property(value => value.TenantId).CurrentValue = first.TenantId;
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));

        await using var empty = fixture.CreateContext();
        empty.LegalEntities.Add(LegalEntity.Create(first.TenantId, "No scope", "123", IdentityAccessTestSetup.Now));
        await Assert.ThrowsAsync<InvalidOperationException>(() => empty.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ConnectionState.Closed, empty.Database.GetDbConnection().State);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        await using var verification = fixture.CreateContext(second.TenantId);
        Assert.False(await verification.Branches.AnyAsync(value => value.IsMainHub, TestContext.Current.CancellationToken));
        Assert.Equal(2, await verification.Branches.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExistingModulePortsRejectChangingScopeBeforeQueryingOrProvisioning()
    {
        var (first, second) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        provider.GetRequiredService<ITenantDataContext>().SelectTenant(first.TenantId);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => provider.GetRequiredService<IBranchesStore>().FindBranchAsync(
            second.TenantId, second.Identity.BranchId, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ApplicationErrorException>(() => provider.GetRequiredService<ITenancyLicensingStore>().FindLicenseAsync(
            second.TenantId, second.Identity.LicenseId, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ApplicationErrorException>(() => provider.GetRequiredService<IIdentityAccessStore>().FindMembershipAsync(
            second.TenantId, second.Identity.MembershipId, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ApplicationErrorException>(() => provider.GetRequiredService<IOperationalAccessReader>().ReadAsync(
            first.Identity.UserId, second.TenantId, second.Identity.BranchId, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ApplicationErrorException>(() => provider.GetRequiredService<ITenantLicenseProvisioning>().BeginAsync(
            second.TenantId, TestContext.Current.CancellationToken));
        Assert.Equal(first.TenantId, provider.GetRequiredService<ITenantDataContext>().TenantId);
        Assert.Equal(ConnectionState.Closed, provider.GetRequiredService<MediPOS.Infrastructure.Persistence.MediPosDbContext>().Database.GetDbConnection().State);
    }
    private static void AssertOnlyTenant(IReadOnlyList<Guid> tenantIds, Guid expected)
    {
        Assert.NotEmpty(tenantIds);
        Assert.All(tenantIds, tenantId => Assert.Equal(expected, tenantId));
    }
}
