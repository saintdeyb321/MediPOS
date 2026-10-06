using MediPOS.Application.Modules.IdentityAccess;
using MediPOS.Application.Modules.IdentityAccess.CreateMembership;
using MediPOS.Application.Modules.IdentityAccess.DeactivateMembership;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.IdentityAccess.ReplaceWorkSchedule;
using MediPOS.Application.Modules.IdentityAccess.SetMembershipBranches;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.IdentityAccess;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class IdentityPersistenceTests(PostgreSqlFixture fixture)
{
    private static DateTimeOffset Now => IdentityAccessTestSetup.Now;

    [Fact]
    public async Task IdentityMigrationAppliesWithoutPendingChanges()
    {
        await using var context = fixture.CreateContext();
        var applied = await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);
        Assert.Contains(applied, migration => migration.EndsWith("_AddIdentityAccess", StringComparison.Ordinal));
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GoogleSubjectIsUniqueAndUpsertPreservesIdAndCreationTime()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var subject = Guid.NewGuid().ToString("N");
        var first = await IdentityAccessTestSetup.CreateUserAsync(scope.ServiceProvider, subject);
        var updated = await IdentityAccessTestSetup.CreateUserAsync(scope.ServiceProvider, subject, "updated@example.test");
        Assert.Equal(first.Id, updated.Id);
        await using var context = fixture.CreateContext();
        var persisted = await context.Users.AsNoTracking().SingleAsync(value => value.Id == first.Id, TestContext.Current.CancellationToken);
        Assert.Equal(Now, persisted.CreatedAt);
        Assert.Equal("updated@example.test", persisted.Email);
        context.Users.Add(User.Create(subject, "duplicate@example.test", "Duplicate", Now));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Fact]
    public async Task ConcurrentFirstGoogleSignInsShareOneGlobalUser()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var firstScope = services.CreateAsyncScope();
        await using var secondScope = services.CreateAsyncScope();
        var subject = Guid.NewGuid().ToString("N");
        var users = await Task.WhenAll(
            IdentityAccessTestSetup.CreateUserAsync(firstScope.ServiceProvider, subject),
            IdentityAccessTestSetup.CreateUserAsync(secondScope.ServiceProvider, subject));
        Assert.Equal(users[0].Id, users[1].Id);
        await using var context = fixture.CreateContext();
        Assert.Equal(1, await context.Users.CountAsync(value => value.GoogleSubject == subject, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ActiveUserTenantPairIsUniqueAndDeactivatedMembershipIsRetained()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var setup = await IdentityAccessTestSetup.CreateAsync(scope.ServiceProvider);
        await using var context = fixture.CreateContext();
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO memberships (id, tenant_id, user_id, role, is_active, created_at)
            VALUES ({Guid.NewGuid()}, {setup.TenantId}, {setup.UserId}, 'cashier', true, {Now})
            """, TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
        Assert.Equal("ux_memberships_tenant_user_active", duplicate.ConstraintName);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<CreateMembershipHandler>().HandleAsync(
                new(setup.TenantId, setup.UserId, TenantRole.Owner), TestContext.Current.CancellationToken));
        await scope.ServiceProvider.GetRequiredService<DeactivateMembershipHandler>().HandleAsync(
            new(setup.TenantId, setup.MembershipId), TestContext.Current.CancellationToken);
        var replacement = await scope.ServiceProvider.GetRequiredService<CreateMembershipHandler>().HandleAsync(
            new(setup.TenantId, setup.UserId, TenantRole.Cashier), TestContext.Current.CancellationToken);
        Assert.NotEqual(setup.MembershipId, replacement.Id);
        Assert.Equal(2, await context.Memberships.CountAsync(value =>
            value.TenantId == setup.TenantId && value.UserId == setup.UserId, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MembershipForeignKeysRejectMissingTenantOrUser(bool missingTenant)
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var setup = await IdentityAccessTestSetup.CreateAsync(scope.ServiceProvider);
        var tenantId = missingTenant ? Guid.NewGuid() : setup.TenantId;
        var userId = missingTenant ? setup.UserId : Guid.NewGuid();
        await using var context = fixture.CreateContext();
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO memberships (id, tenant_id, user_id, role, is_active, created_at)
            VALUES ({Guid.NewGuid()}, {tenantId}, {userId}, 'cashier', true, {Now})
            """, TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MembershipBranchForeignKeysProtectBothTenantRelations(bool foreignMembership)
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var first = await IdentityAccessTestSetup.CreateAsync(scope.ServiceProvider);
        var other = await IdentityAccessTestSetup.CreateAsync(scope.ServiceProvider);
        var membershipId = foreignMembership ? other.MembershipId : first.MembershipId;
        var branchId = foreignMembership ? first.BranchId : other.BranchId;
        await using var context = fixture.CreateContext();
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO membership_branches (tenant_id, membership_id, branch_id)
            VALUES ({first.TenantId}, {membershipId}, {branchId})
            """, TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [Fact]
    public async Task WorkScheduleCannotReferenceMembershipFromAnotherTenant()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var first = await IdentityAccessTestSetup.CreateAsync(scope.ServiceProvider);
        var other = await IdentityAccessTestSetup.CreateAsync(scope.ServiceProvider);
        await using var context = fixture.CreateContext();
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO work_schedules (id, tenant_id, membership_id, day_of_week, start_time, end_time)
            VALUES ({Guid.NewGuid()}, {first.TenantId}, {other.MembershipId}, 'tue', TIME '09:00', TIME '18:00')
            """, TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [Theory]
    [InlineData("tue", "18:00", "09:00", "ck_work_schedules_window")]
    [InlineData("tue", "09:00", "09:00", "ck_work_schedules_window")]
    [InlineData("tue", "09:00", "24:00", "ck_work_schedules_window")]
    [InlineData("invalid", "09:00", "18:00", "ck_work_schedules_day")]
    public async Task DatabaseRejectsInvalidDayOrTimeWindow(string day, string start, string end, string constraint)
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var setup = await IdentityAccessTestSetup.CreateAsync(scope.ServiceProvider);
        await using var context = fixture.CreateContext();
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO work_schedules (id, tenant_id, membership_id, day_of_week, start_time, end_time)
            VALUES ({Guid.NewGuid()}, {setup.TenantId}, {setup.MembershipId}, {day}, CAST({start} AS time), CAST({end} AS time))
            """, TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal(constraint, error.ConstraintName);
    }

    [Fact]
    public async Task ExactWorkWindowDuplicateIsRejectedAndCodesAreStable()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var setup = await IdentityAccessTestSetup.CreateAsync(scope.ServiceProvider, TenantRole.Pharmacist);
        await scope.ServiceProvider.GetRequiredService<ReplaceWorkScheduleHandler>().HandleAsync(
            new(setup.TenantId, setup.MembershipId, [new(DayOfWeek.Tuesday, new(9, 0), new(18, 0))]), TestContext.Current.CancellationToken);
        await using var context = fixture.CreateContext();
        var role = await context.Database.SqlQuery<string>($"""SELECT role AS "Value" FROM memberships WHERE tenant_id = {setup.TenantId} AND id = {setup.MembershipId}""")
            .SingleAsync(TestContext.Current.CancellationToken);
        var day = await context.Database.SqlQuery<string>($"""SELECT day_of_week AS "Value" FROM work_schedules WHERE tenant_id = {setup.TenantId} AND membership_id = {setup.MembershipId}""")
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("pharmacist", role);
        Assert.Equal("tue", day);
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO work_schedules (id, tenant_id, membership_id, day_of_week, start_time, end_time)
            VALUES ({Guid.NewGuid()}, {setup.TenantId}, {setup.MembershipId}, 'tue', TIME '09:00', TIME '18:00')
            """, TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        Assert.Equal("ux_work_schedules_exact_window", error.ConstraintName);
    }

    [Fact]
    public async Task ReplacementCanRepeatInOneScopeAndDeactivationPreservesAllReferences()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var setup = await IdentityAccessTestSetup.CreateAsync(scope.ServiceProvider);
        var branchHandler = scope.ServiceProvider.GetRequiredService<SetMembershipBranchesHandler>();
        await branchHandler.HandleAsync(new(setup.TenantId, setup.MembershipId, [setup.BranchId]), TestContext.Current.CancellationToken);
        await branchHandler.HandleAsync(new(setup.TenantId, setup.MembershipId, [setup.BranchId]), TestContext.Current.CancellationToken);
        var scheduleHandler = scope.ServiceProvider.GetRequiredService<ReplaceWorkScheduleHandler>();
        await scheduleHandler.HandleAsync(new(setup.TenantId, setup.MembershipId, [new(DayOfWeek.Tuesday, new(9, 0), new(18, 0))]), TestContext.Current.CancellationToken);
        await scheduleHandler.HandleAsync(new(setup.TenantId, setup.MembershipId, [new(DayOfWeek.Tuesday, new(9, 0), new(18, 0))]), TestContext.Current.CancellationToken);
        await scope.ServiceProvider.GetRequiredService<DeactivateMembershipHandler>().HandleAsync(
            new(setup.TenantId, setup.MembershipId), TestContext.Current.CancellationToken);
        await using var verification = fixture.CreateContext();
        var membership = await verification.Memberships.SingleAsync(value => value.Id == setup.MembershipId, TestContext.Current.CancellationToken);
        Assert.False(membership.IsActive);
        Assert.Equal(Now, membership.DeactivatedAt);
        Assert.True(await verification.Users.AnyAsync(value => value.Id == setup.UserId, TestContext.Current.CancellationToken));
        Assert.Single(await verification.MembershipBranches.Where(value =>
            value.TenantId == setup.TenantId && value.MembershipId == setup.MembershipId).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Single(await verification.WorkSchedules.Where(value =>
            value.TenantId == setup.TenantId && value.MembershipId == setup.MembershipId).ToListAsync(TestContext.Current.CancellationToken));
        var snapshot = await scope.ServiceProvider.GetRequiredService<IOperationalAccessReader>().ReadAsync(
            setup.UserId, setup.TenantId, setup.BranchId, TestContext.Current.CancellationToken);
        Assert.Equal("access.membership_inactive",
            EvaluateOperationalAccess.Evaluate(setup.UserId, setup.TenantId, setup.BranchId, Now, snapshot).Code);
    }

    [Fact]
    public async Task FailedReplacementRollsBackDeletionAndPreservesExistingAssignments()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        IdentityAccessTestSetup.Setup setup;
        await using (var setupScope = services.CreateAsyncScope())
        {
            setup = await IdentityAccessTestSetup.CreateAsync(setupScope.ServiceProvider);
            await setupScope.ServiceProvider.GetRequiredService<SetMembershipBranchesHandler>().HandleAsync(
                new(setup.TenantId, setup.MembershipId, [setup.BranchId]), TestContext.Current.CancellationToken);
        }
        await using (var scope = services.CreateAsyncScope())
        {
            await using var transaction = await scope.ServiceProvider.GetRequiredService<ITenantLicenseProvisioning>()
                .BeginAsync(setup.TenantId, TestContext.Current.CancellationToken);
            Assert.NotNull(transaction);
            var invalid = MembershipBranch.Create(setup.TenantId, setup.MembershipId, setup.TenantId, Guid.NewGuid(), setup.TenantId);
            var error = await Assert.ThrowsAsync<DbUpdateException>(() =>
                scope.ServiceProvider.GetRequiredService<IIdentityAccessStore>().ReplaceBranchesAsync(
                    setup.TenantId, setup.MembershipId, [invalid], TestContext.Current.CancellationToken));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
            // No CompleteAsync: the entire transaction, including deletion, must roll back.
        }
        await using var verification = fixture.CreateContext();
        Assert.Equal(setup.BranchId, Assert.Single(await verification.MembershipBranches.Where(value =>
            value.TenantId == setup.TenantId && value.MembershipId == setup.MembershipId).ToListAsync(TestContext.Current.CancellationToken)).BranchId);
    }
}
