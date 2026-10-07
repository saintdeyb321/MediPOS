using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.Cash.GetActiveCashSessions;
using MediPOS.Application.Modules.Cash.OpenCashSession;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Infrastructure;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.Cash;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class CashSessionIsolationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task RuntimeRlsForcesTenantOnSelectInsertUpdateAndCannotMoveAnOwnedRow()
    {
        var pair = await CreatePairAsync();
        await using var context = fixture.CreateContext(pair.A.TenantId);
        Assert.Equal(1, await context.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relname = 'cash_sessions' AND c.relrowsecurity AND c.relforcerowsecurity
            """).SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM pg_roles WHERE rolname = current_user AND NOT rolsuper AND NOT rolbypassrls
            """).SingleAsync(TestContext.Current.CancellationToken));
        Assert.Single(await context.CashSessions.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await context.Database.SqlQuery<int>(
            $"SELECT count(*)::int AS \"Value\" FROM cash_sessions WHERE tenant_id = {pair.B.TenantId}").SingleAsync(TestContext.Current.CancellationToken));
        var insert = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(context, pair.B, pair.B.BranchId, pair.B.MembershipId, 0m));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, insert.SqlState);
        Assert.Equal(0, await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE cash_sessions SET opening_amount = 999 WHERE id = {pair.OpenB.CashSessionId}", TestContext.Current.CancellationToken));
        var move = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE cash_sessions SET tenant_id = {pair.B.TenantId}, branch_id = {pair.B.BranchId}, membership_id = {pair.B.MembershipId} WHERE id = {pair.OpenA.CashSessionId}", TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, move.SqlState);
        var retained = await context.CashSessions.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(pair.OpenA.OpeningAmount, retained.OpeningAmount);
        Assert.Equal(pair.A.TenantId, retained.TenantId);
    }

    [Fact]
    public async Task MissingTenantFailsClosedEvenWhenEfFiltersAreIgnored()
    {
        var pair = await CreatePairAsync();
        await using var context = fixture.CreateContext();
        Assert.Empty(await context.CashSessions.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.CashSessions.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
        var insert = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(context, pair.A, pair.A.BranchId, pair.A.MembershipId, 0m));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, insert.SqlState);
        Assert.Equal(0, await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE cash_sessions SET opening_amount = 999 WHERE id = {pair.OpenA.CashSessionId}", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompositeForeignKeysRejectCrossTenantBranchOrMembershipIndependentlyOfRls(bool foreignBranch)
    {
        var pair = await CreatePairAsync();
        await using var constraints = fixture.CreateConstraintContext(pair.A.TenantId);
        var error = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(constraints, pair.A,
            foreignBranch ? pair.B.BranchId : pair.A.BranchId, foreignBranch ? pair.A.MembershipId : pair.B.MembershipId, 0m));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
        Assert.Equal(foreignBranch ? "FK_cash_sessions_branches_tenant_id_branch_id" : "FK_cash_sessions_memberships_tenant_id_membership_id", error.ConstraintName);
    }

    [Fact]
    public async Task DatabaseRejectsNegativeOpeningAmountAndUnknownStatus()
    {
        var pair = await CreatePairAsync();
        await using var constraints = fixture.CreateConstraintContext(pair.A.TenantId);
        var amount = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(constraints, pair.A, pair.A.BranchId, pair.A.MembershipId, -1m));
        Assert.Equal(PostgresErrorCodes.CheckViolation, amount.SqlState);
        Assert.Equal("ck_cash_sessions_opening_amount", amount.ConstraintName);
        var nan = await Assert.ThrowsAsync<PostgresException>(() => constraints.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE cash_sessions SET opening_amount = 'NaN'::numeric WHERE id = {pair.OpenA.CashSessionId}", TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.CheckViolation, nan.SqlState);
        Assert.Equal("ck_cash_sessions_opening_amount", nan.ConstraintName);
        var status = await Assert.ThrowsAsync<PostgresException>(() => constraints.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE cash_sessions SET status = 'unknown' WHERE id = {pair.OpenA.CashSessionId}", TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.CheckViolation, status.SqlState);
        Assert.Equal("ck_cash_sessions_status", status.ConstraintName);
    }

    [Fact]
    public async Task ContextRejectsUnauditedOpeningsAndCrossTenantWritesBeforeSaving()
    {
        var pair = await CreatePairAsync();
        await using var context = fixture.CreateContext(pair.A.TenantId);
        await using var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var session = CashSession.Open(pair.B.TenantId, pair.B.BranchId, pair.B.MembershipId, 1m, IdentityAccessTestSetup.Now, pair.B.UserId);
        context.CashSessions.Add(session);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        context.AuditLogs.Add(AuditTrail.Record(session.TenantId, session.OpenedByActorId, AuditAction.CashSessionOpened,
            session.Id, session.OpenedAt, null, "{}"));
        var tenantError = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Contains("selected tenant", tenantError.Message, StringComparison.Ordinal);
        context.ChangeTracker.Clear();
        var existing = await context.CashSessions.SingleAsync(TestContext.Current.CancellationToken);
        context.CashSessions.Remove(existing);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ContextRequiresExplicitTransactionAndMatchingAuditActor(bool wrongActor)
    {
        var pair = await CreatePairAsync();
        await using var context = fixture.CreateContext(pair.A.TenantId);
        await using var transaction = wrongActor ? await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken) : null;
        var session = CashSession.Open(pair.A.TenantId, pair.A.BranchId, pair.A.MembershipId, 0m, IdentityAccessTestSetup.Now, pair.A.UserId);
        context.CashSessions.Add(session);
        context.AuditLogs.Add(AuditTrail.Record(session.TenantId, wrongActor ? pair.B.UserId : pair.A.UserId,
            AuditAction.CashSessionOpened, session.Id, session.OpenedAt, null, "{}"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        context.ChangeTracker.Clear();
        Assert.Equal(1, await context.CashSessions.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.AuditLogs.CountAsync(value => value.Action == AuditAction.CashSessionOpened, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OwnerActiveQueryAndLookupNeverMixTenantsAndAccessRejectsForeignBranch()
    {
        var pair = await CreatePairAsync();
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var owner = await CashSessionTestData.AddOwnerAsync(source, pair.A);
        CashSessionTestData.Authenticate(source, owner);
        var handler = source.GetRequiredService<GetActiveCashSessionsHandler>();
        var own = Assert.Single(await handler.HandleAsync(new(pair.A.TenantId), TestContext.Current.CancellationToken));
        Assert.Equal(pair.OpenA.CashSessionId, own.CashSessionId);
        Assert.Equal(pair.A.UserId, own.UserId);
        var branch = await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.HandleAsync(
            new(pair.A.TenantId, pair.B.BranchId), TestContext.Current.CancellationToken));
        Assert.Equal(CashSessionErrors.BranchAccessConflict, branch.Error);
        Assert.Null(await source.GetRequiredService<IFindOpenCashSession>().FindAsync(
            pair.A.TenantId, pair.B.BranchId, pair.B.MembershipId, TestContext.Current.CancellationToken));
        var switchTenant = await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<IFindOpenCashSession>().FindAsync(
            pair.B.TenantId, pair.B.BranchId, pair.B.MembershipId, TestContext.Current.CancellationToken));
        Assert.Equal(ApplicationErrors.TenantScopeConflict, switchTenant.Error);
        await using var foreign = services.CreateAsyncScope();
        CashSessionTestData.Authenticate(foreign.ServiceProvider, owner);
        var tenant = await Assert.ThrowsAsync<ApplicationErrorException>(() => foreign.ServiceProvider.GetRequiredService<GetActiveCashSessionsHandler>().HandleAsync(
            new(pair.B.TenantId), TestContext.Current.CancellationToken));
        Assert.Equal("access.membership_missing", tenant.Error.Code);
        var context = source.GetRequiredService<MediPosDbContext>();
        Assert.Equal(1, await context.AuditLogs.CountAsync(value => value.Action == AuditAction.CashSessionOpened, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReusingPhysicalConnectionClearsCashTenantBeforeUnscopedAndNextTenantQueries()
    {
        var pair = await CreatePairAsync();
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        { ApplicationName = Guid.NewGuid().ToString("N"), MaxPoolSize = 1, NoResetOnClose = true }.ConnectionString;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:MediPosDatabase"] = connection }).Build();
        var registrations = new ServiceCollection();
        registrations.AddSingleton<TimeProvider>(new IdentityAccessTestSetup.Clock());
        registrations.AddInfrastructure(configuration);
        registrations.AddIdentityAuthentication<IdentityAccessTestSetup.TestGoogleIdentitySource, IdentityAccessTestSetup.TestServerSession>();
        await using var services = registrations.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        int pid;
        await using (var scope = services.CreateAsyncScope())
        {
            var source = scope.ServiceProvider;
            Assert.Equal(pair.OpenA, await source.GetRequiredService<IFindOpenCashSession>().FindAsync(
                pair.A.TenantId, pair.A.BranchId, pair.A.MembershipId, TestContext.Current.CancellationToken));
            var context = source.GetRequiredService<MediPosDbContext>();
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            pid = await BackendPidAsync(context);
            Assert.Equal(0, await context.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cash_sessions WHERE tenant_id = {pair.B.TenantId}")
                .SingleAsync(TestContext.Current.CancellationToken));
        }
        await using (var scope = services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>();
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            Assert.Equal(pid, await BackendPidAsync(context));
            Assert.Equal(0, await context.Database.SqlQueryRaw<int>("""SELECT count(*)::int AS "Value" FROM cash_sessions""").SingleAsync(TestContext.Current.CancellationToken));
        }
        await using (var scope = services.CreateAsyncScope())
        {
            var source = scope.ServiceProvider;
            var reader = source.GetRequiredService<IFindOpenCashSession>();
            Assert.Equal(pair.OpenB, await reader.FindAsync(pair.B.TenantId, pair.B.BranchId, pair.B.MembershipId, TestContext.Current.CancellationToken));
            Assert.Null(await reader.FindAsync(pair.B.TenantId, pair.A.BranchId, pair.A.MembershipId, TestContext.Current.CancellationToken));
            var context = source.GetRequiredService<MediPosDbContext>();
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            Assert.Equal(pid, await BackendPidAsync(context));
            Assert.Equal(pair.OpenB.CashSessionId, (await context.CashSessions.IgnoreQueryFilters().SingleAsync(TestContext.Current.CancellationToken)).Id);
        }
    }

    private async Task<Pair> CreatePairAsync()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        IdentityAccessTestSetup.Setup first;
        IdentityAccessTestSetup.Setup second;
        OpenCashSessionDetails openFirst;
        OpenCashSessionDetails openSecond;
        await using (var scope = services.CreateAsyncScope())
        {
            first = await CashSessionTestData.CreateAsync(scope.ServiceProvider);
            openFirst = await CashSessionTestData.OpenAsync(scope.ServiceProvider, first);
        }
        await using (var scope = services.CreateAsyncScope())
        {
            second = await CashSessionTestData.CreateAsync(scope.ServiceProvider);
            openSecond = await CashSessionTestData.OpenAsync(scope.ServiceProvider, second);
        }
        return new(first, second, openFirst, openSecond);
    }

    private static Task<int> InsertAsync(MediPosDbContext context, IdentityAccessTestSetup.Setup setup, Guid branchId, Guid membershipId, decimal amount) =>
        context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cash_sessions (id, tenant_id, branch_id, membership_id, opening_amount, status, opened_at, opened_by_actor_id)
            VALUES ({Guid.NewGuid()}, {setup.TenantId}, {branchId}, {membershipId}, {amount}, 'open', {IdentityAccessTestSetup.Now}, {setup.UserId})
            """, TestContext.Current.CancellationToken);

    private static async Task<int> BackendPidAsync(MediPosDbContext context)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_backend_pid()";
        return Assert.IsType<int>(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    private sealed record Pair(IdentityAccessTestSetup.Setup A, IdentityAccessTestSetup.Setup B, OpenCashSessionDetails OpenA, OpenCashSessionDetails OpenB);
}
