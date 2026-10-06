using System.Diagnostics;
using System.Text.Json;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.Branches;
using MediPOS.Application.Modules.Branches.CreateBranch;
using MediPOS.Application.Modules.Branches.CreateLegalEntity;
using MediPOS.Application.Modules.Branches.SetMainHubBranch;
using MediPOS.Application.Modules.IdentityAccess.DeactivateMembership;
using MediPOS.Application.Modules.IdentityAccess.ReplaceWorkSchedule;
using MediPOS.Application.Modules.IdentityAccess.SetMembershipBranches;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Application.Modules.TenancyLicensing.ReactivateLicense;
using MediPOS.Application.Modules.TenancyLicensing.RenewLicense;
using MediPOS.Application.Modules.TenancyLicensing.RequestTenantPurge;
using MediPOS.Application.Modules.TenancyLicensing.SuspendLicense;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.Infrastructure;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.AuditSupport;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class AuditFoundationTests(PostgreSqlFixture fixture)
{
    private static DateTimeOffset Now => IdentityAccessTestSetup.Now;

    [Fact]
    public async Task MigrationAppliesAndRuntimeHasOnlyReadInsertPrivilegesWithForcedRls()
    {
        await using var context = fixture.CreateContext();
        Assert.Contains(await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken),
            value => value.EndsWith("_AddAuditFoundation", StringComparison.Ordinal));
        Assert.False(context.Database.HasPendingModelChanges());
        await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await AssertRestrictedRoleAsync((NpgsqlConnection)context.Database.GetDbConnection());
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT relrowsecurity, relforcerowsecurity FROM pg_class WHERE relname = 'audit_logs'";
        await using (var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
            Assert.True(reader.GetBoolean(0));
            Assert.True(reader.GetBoolean(1));
        }
        command.CommandText = """
            SELECT has_table_privilege(current_user, 'audit_logs', 'SELECT'),
                   has_table_privilege(current_user, 'audit_logs', 'INSERT'),
                   has_table_privilege(current_user, 'audit_logs', 'UPDATE'),
                   has_table_privilege(current_user, 'audit_logs', 'DELETE'),
                   has_table_privilege(current_user, 'audit_logs', 'TRUNCATE')
            """;
        await using var privileges = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await privileges.ReadAsync(TestContext.Current.CancellationToken));
        Assert.True(privileges.GetBoolean(0));
        Assert.True(privileges.GetBoolean(1));
        Assert.False(privileges.GetBoolean(2));
        Assert.False(privileges.GetBoolean(3));
        Assert.False(privileges.GetBoolean(4));
    }

    [Fact]
    public async Task EveryFoundationMutationPersistsItsAuditWithServerActorTraceAndMinimalSnapshots()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        using var activity = new Activity("postgres-audit-test").SetIdFormat(ActivityIdFormat.W3C).Start();
        var setup = await IdentityAccessTestSetup.CreateAsync(provider);
        await provider.GetRequiredService<RenewLicenseHandler>().HandleAsync(
            new(setup.TenantId, setup.LicenseId, Now.AddMonths(2), setup.ActorId), TestContext.Current.CancellationToken);
        await provider.GetRequiredService<SuspendLicenseHandler>().HandleAsync(
            new(setup.TenantId, setup.LicenseId, setup.ActorId), TestContext.Current.CancellationToken);
        await provider.GetRequiredService<ReactivateLicenseHandler>().HandleAsync(
            new(setup.TenantId, setup.LicenseId, LicenseStatus.Active, setup.ActorId), TestContext.Current.CancellationToken);
        await provider.GetRequiredService<SetMainHubBranchHandler>().HandleAsync(
            new(setup.TenantId, setup.BranchId, setup.ActorId), TestContext.Current.CancellationToken);
        await provider.GetRequiredService<SetMembershipBranchesHandler>().HandleAsync(
            new(setup.TenantId, setup.MembershipId, [setup.BranchId], setup.ActorId), TestContext.Current.CancellationToken);
        await provider.GetRequiredService<ReplaceWorkScheduleHandler>().HandleAsync(
            new(setup.TenantId, setup.MembershipId, [new(DayOfWeek.Tuesday, new(9, 0), new(18, 0))], setup.ActorId), TestContext.Current.CancellationToken);
        await provider.GetRequiredService<DeactivateMembershipHandler>().HandleAsync(
            new(setup.TenantId, setup.MembershipId, setup.ActorId), TestContext.Current.CancellationToken);
        await provider.GetRequiredService<RequestTenantPurgeHandler>().HandleAsync(
            new(setup.TenantId, setup.LicenseId, setup.ActorId), TestContext.Current.CancellationToken);

        await using var verification = fixture.CreateContext(setup.TenantId);
        var rows = await verification.AuditLogs.ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(12, rows.Count);
        Assert.Equal(Enum.GetValues<AuditAction>().Where(value => AuditCodes.EntityFor(value) != AuditEntityType.BusinessProduct).Order(), rows.Select(value => value.Action).Order());
        Assert.All(rows, row =>
        {
            Assert.Equal(setup.TenantId, row.TenantId);
            Assert.Equal(setup.ActorId, row.ActorId);
            Assert.Equal(Now, row.OccurredAt);
            Assert.Equal(TimeSpan.Zero, row.OccurredAt.Offset);
            Assert.Equal(activity.TraceId.ToHexString(), row.CorrelationId);
        });
        var deactivated = Assert.Single(rows, value => value.Action == AuditAction.MembershipDeactivated);
        Assert.Equal(setup.MembershipId, deactivated.EntityId);
        using var after = JsonDocument.Parse(deactivated.AfterJson!);
        Assert.False(after.RootElement.GetProperty("isActive").GetBoolean());
        Assert.False(after.RootElement.TryGetProperty("email", out _));
        Assert.Equal(1, await verification.Users.CountAsync(value => value.Id == setup.ActorId, TestContext.Current.CancellationToken));
        Assert.Equal(LicenseStatus.PurgePending, (await verification.Licenses.SingleAsync(TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task RuntimeDirectSqlIsTenantScopedAndCannotUpdateOrDeleteEvenOwnAudit()
    {
        var (first, second) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await AssertRestrictedRoleAsync(connection);
        await SetTenantAsync(connection, first.TenantId);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT tenant_id FROM audit_logs";
        await using (var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
            Assert.Equal(first.TenantId, reader.GetGuid(0));
            Assert.False(await reader.ReadAsync(TestContext.Current.CancellationToken));
        }
        command.CommandText = """
            INSERT INTO audit_logs (id, tenant_id, actor_id, action, entity_type, entity_id, occurred_at, correlation_id)
            VALUES (@id, @tenant, @actor, 'membership.created', 'membership', @entity, @time, @trace)
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("tenant", second.TenantId);
        command.Parameters.AddWithValue("actor", first.Identity.ActorId);
        command.Parameters.AddWithValue("entity", second.Identity.MembershipId);
        command.Parameters.AddWithValue("time", Now);
        command.Parameters.AddWithValue("trace", ActivityTraceId.CreateRandom().ToHexString());
        var crossTenant = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, crossTenant.SqlState);

        // Same statement/privileges succeed for A: the preceding failure was tenant enforcement.
        command.Parameters["tenant"].Value = first.TenantId;
        command.Parameters["entity"].Value = first.Identity.MembershipId;
        Assert.Equal(1, await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        command.Parameters.Clear();
        command.CommandText = "UPDATE audit_logs SET actor_id = actor_id WHERE tenant_id = @tenant";
        command.Parameters.AddWithValue("tenant", first.TenantId);
        var update = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, update.SqlState);
        command.CommandText = "DELETE FROM audit_logs WHERE tenant_id = @tenant";
        var delete = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, delete.SqlState);
        await SetTenantAsync(connection, second.TenantId);
        command.CommandText = "SELECT DISTINCT tenant_id FROM audit_logs";
        command.Parameters.Clear();
        await using var secondReader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await secondReader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(second.TenantId, secondReader.GetGuid(0));
        Assert.False(await secondReader.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MissingScopeCannotReadOrInsertAuditThroughEfOrDirectSql()
    {
        var (first, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var context = fixture.CreateContext();
        Assert.Empty(await context.AuditLogs.ToListAsync(TestContext.Current.CancellationToken));
        await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await AssertRestrictedRoleAsync((NpgsqlConnection)context.Database.GetDbConnection());
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT count(*) FROM audit_logs";
        Assert.Equal(0L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO audit_logs (id, tenant_id, actor_id, action, entity_type, entity_id, occurred_at, correlation_id)
            VALUES ({Guid.NewGuid()}, {first.TenantId}, {first.Identity.ActorId}, 'tenant.created', 'tenant',
                    {first.TenantId}, {Now}, {ActivityTraceId.CreateRandom().ToHexString()})
            """, TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    [Fact]
    public async Task EfRejectsAuditModificationAndDeletionBeforeDatabaseWrite()
    {
        var (first, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var context = fixture.CreateContext(first.TenantId);
        var row = await context.AuditLogs.FirstAsync(TestContext.Current.CancellationToken);
        context.Entry(row).Property(value => value.ActorId).CurrentValue = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        context.ChangeTracker.Clear();
        context.AuditLogs.Remove(row);
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Fact]
    public async Task UncommittedProvisioningRollsBackBusinessAndAuditTogether()
    {
        var (first, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        Guid id;
        Guid branchId;
        await using (var scope = services.CreateAsyncScope())
        {
            var provider = scope.ServiceProvider;
            await using var transaction = await provider.GetRequiredService<ITenantLicenseProvisioning>().BeginAsync(
                first.TenantId, TestContext.Current.CancellationToken);
            Assert.NotNull(transaction);
            var branch = Branch.Create(first.TenantId, first.LegalEntityId, first.TenantId, "Rollback", Now);
            branchId = branch.Id;
            var audit = AuditTrail.Record(first.TenantId, first.Identity.ActorId, AuditAction.BranchCreated, branch.Id, Now, null, AuditTrail.BranchCreated(branch));
            id = audit.Id;
            await provider.GetRequiredService<IBranchesStore>().AddBranchAsync(branch, audit, TestContext.Current.CancellationToken);
            // Omit CompleteAsync deliberately.
        }
        await using var verification = fixture.CreateContext(first.TenantId);
        Assert.False(await verification.Branches.AnyAsync(value => value.Id == branchId, TestContext.Current.CancellationToken));
        Assert.False(await verification.AuditLogs.AnyAsync(value => value.Id == id, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("hub")]
    [InlineData("branches")]
    [InlineData("schedule")]
    [InlineData("deactivate")]
    public async Task AuditInsertFailureRollsBackEarlierBulkMutationAndLeavesNoAudit(string operation)
    {
        var (first, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        long beforeCount;
        await using (var before = fixture.CreateContext(first.TenantId))
            beforeCount = await before.AuditLogs.LongCountAsync(TestContext.Current.CancellationToken);
        var interceptor = new RejectAuditInsert();
        Guid? auditId;
        await using var provider = CreateFailingAuditServices(first.TenantId, interceptor);
        await using (var scope = provider.CreateAsyncScope())
        {
            var source = scope.ServiceProvider;
            var failedContext = source.GetRequiredService<MediPosDbContext>();
            var error = await Assert.ThrowsAnyAsync<Exception>(() => operation switch
            {
                "hub" => source.GetRequiredService<SetMainHubBranchHandler>().HandleAsync(
                    new(first.TenantId, first.SpareBranchId, first.Identity.ActorId), TestContext.Current.CancellationToken),
                "branches" => source.GetRequiredService<SetMembershipBranchesHandler>().HandleAsync(
                    new(first.TenantId, first.Identity.MembershipId, [first.SpareBranchId], first.Identity.ActorId), TestContext.Current.CancellationToken),
                "schedule" => source.GetRequiredService<ReplaceWorkScheduleHandler>().HandleAsync(
                    new(first.TenantId, first.Identity.MembershipId, [new(DayOfWeek.Wednesday, new(10, 0), new(15, 0))], first.Identity.ActorId), TestContext.Current.CancellationToken),
                "deactivate" => source.GetRequiredService<DeactivateMembershipHandler>().HandleAsync(
                    new(first.TenantId, first.Identity.MembershipId, first.Identity.ActorId), TestContext.Current.CancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(operation)),
            });
            Assert.NotNull(error);
            Assert.True(interceptor.Rejected);
            Assert.Empty(failedContext.ChangeTracker.Entries<AuditLog>());
            auditId = interceptor.AuditId;
        }
        await using var verification = fixture.CreateContext(first.TenantId);
        Assert.Equal(beforeCount, await verification.AuditLogs.LongCountAsync(TestContext.Current.CancellationToken));
        if (auditId.HasValue)
            Assert.False(await verification.AuditLogs.AnyAsync(value => value.Id == auditId, TestContext.Current.CancellationToken));
        Assert.False(await verification.Branches.AnyAsync(value => value.IsMainHub, TestContext.Current.CancellationToken));
        Assert.Equal(first.Identity.BranchId, (await verification.MembershipBranches.SingleAsync(TestContext.Current.CancellationToken)).BranchId);
        Assert.Equal(DayOfWeek.Tuesday, (await verification.WorkSchedules.SingleAsync(TestContext.Current.CancellationToken)).DayOfWeek);
        Assert.True((await verification.Memberships.SingleAsync(TestContext.Current.CancellationToken)).IsActive);
    }

    [Fact]
    public async Task DatabaseAuditConstraintFailureRollsBackImplicitBusinessSaveAndClearsStagedAudit()
    {
        var (first, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        var interceptor = new RejectAuditInsert(rejectAtDatabase: true);
        await using var provider = CreateFailingAuditServices(first.TenantId, interceptor);
        Guid entityId;
        Guid auditId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var source = scope.ServiceProvider;
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => source.GetRequiredService<CreateLegalEntityHandler>().HandleAsync(
                new(first.TenantId, "Atomic failure", "20100070970", first.Identity.ActorId), TestContext.Current.CancellationToken));
            var postgres = Assert.IsType<PostgresException>(error.InnerException);
            Assert.Equal(PostgresErrorCodes.CheckViolation, postgres.SqlState);
            Assert.Equal("ck_audit_logs_before", postgres.ConstraintName);
            Assert.True(interceptor.Rejected);
            entityId = interceptor.EntityId!.Value;
            auditId = interceptor.AuditId!.Value;
            Assert.Empty(source.GetRequiredService<MediPosDbContext>().ChangeTracker.Entries());
        }
        await using var verification = fixture.CreateContext(first.TenantId);
        Assert.False(await verification.LegalEntities.AnyAsync(value => value.Id == entityId, TestContext.Current.CancellationToken));
        Assert.False(await verification.AuditLogs.AnyAsync(value => value.Id == auditId, TestContext.Current.CancellationToken));
    }

    private ServiceProvider CreateFailingAuditServices(Guid tenantId, SaveChangesInterceptor interceptor)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:MediPosDatabase"] = fixture.ConnectionString }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new IdentityAccessTestSetup.Clock());
        services.AddInfrastructure(configuration);
        // Keep one scoped database/context for all ports, using real PostgreSQL with an injected audit failure.
        services.AddScoped(source =>
        {
            var tenantContext = source.GetRequiredService<ITenantDataContext>();
            tenantContext.SelectTenant(tenantId);
            var options = new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql(fixture.ConnectionString)
                .AddInterceptors(interceptor).Options;
            return new MediPosDbContext(options, tenantContext);
        });
        return services.BuildServiceProvider();
    }

    private sealed class RejectAuditInsert(bool rejectAtDatabase = false) : SaveChangesInterceptor
    {
        public bool Rejected { get; private set; }
        public Guid? AuditId { get; private set; }
        public Guid? EntityId { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var added = eventData.Context!.ChangeTracker.Entries<AuditLog>().FirstOrDefault(value => value.State == EntityState.Added);
            if (added is not null)
            {
                Rejected = true;
                AuditId = added.Entity.Id;
                EntityId = added.Entity.EntityId;
                if (!rejectAtDatabase)
                    throw new InvalidOperationException("Injected audit insert failure.");
                added.Property(value => value.BeforeJson).CurrentValue = "[]";
            }
            return ValueTask.FromResult(result);
        }
    }

    private static async Task AssertRestrictedRoleAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user";
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.False(reader.GetBoolean(0));
        Assert.False(reader.GetBoolean(1));
    }

    private static async Task SetTenantAsync(NpgsqlConnection connection, Guid? tenantId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('medipos.tenant_id', @tenant, false)";
        command.Parameters.AddWithValue("tenant", tenantId?.ToString("D") ?? string.Empty);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
