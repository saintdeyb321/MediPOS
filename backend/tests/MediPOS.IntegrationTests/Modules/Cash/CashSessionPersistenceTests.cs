using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Branches.CreateBranch;
using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.Cash.CloseCashSession;
using MediPOS.Application.Modules.Cash.GetActiveCashSessions;
using MediPOS.Application.Modules.Cash.OpenCashSession;
using MediPOS.Application.Modules.IdentityAccess.CreateMembership;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.Cash;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class CashSessionPersistenceTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task FreshMigrationMatchesModelAndCreatesOnlyRequiredIndexesWithStableMoneyAndStatus()
    {
        await using var context = fixture.CreateConstraintContext();
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Contains(await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken), name => name.EndsWith("_AddCashSessions", StringComparison.Ordinal));
        Assert.Equal(3, await context.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM pg_indexes WHERE schemaname = 'public' AND tablename = 'cash_sessions'
              AND indexname NOT LIKE 'PK_%' AND indexname NOT LIKE 'AK_%'
            """).SingleAsync(TestContext.Current.CancellationToken));
        var index = await context.Database.SqlQueryRaw<string>("""
            SELECT indexdef AS "Value" FROM pg_indexes WHERE schemaname = 'public' AND indexname = 'ux_cash_sessions_tenant_branch_membership_open'
            """).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Contains("UNIQUE", index, StringComparison.Ordinal);
        Assert.Contains("(tenant_id, branch_id, membership_id)", index, StringComparison.Ordinal);
        Assert.Contains("'open'", index, StringComparison.Ordinal);
        Assert.Equal(1, await context.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'cash_sessions' AND column_name = 'opening_amount'
              AND data_type = 'numeric' AND numeric_precision = 18 AND numeric_scale = 4
            """).SingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OpeningAndLookupPreserveAuthenticatedMembershipActorExactMoneyAndSingleAudit()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var setup = await CashSessionTestData.CreateAsync(source);
        var opened = await CashSessionTestData.OpenAsync(source, setup, 0.1234m);
        var reader = source.GetRequiredService<IFindOpenCashSession>();
        Assert.Equal(opened, await reader.FindAsync(setup.TenantId, setup.BranchId, setup.MembershipId, TestContext.Current.CancellationToken));
        Assert.Null(await reader.FindAsync(setup.TenantId, setup.BranchId, Guid.NewGuid(), TestContext.Current.CancellationToken));
        Assert.Null(await reader.FindAsync(setup.TenantId, Guid.NewGuid(), setup.MembershipId, TestContext.Current.CancellationToken));
        var context = source.GetRequiredService<MediPosDbContext>();
        var audit = await context.AuditLogs.AsNoTracking().SingleAsync(value => value.EntityId == opened.CashSessionId, TestContext.Current.CancellationToken);
        Assert.Equal(AuditAction.CashSessionOpened, audit.Action);
        Assert.Equal(AuditEntityType.CashSession, audit.EntityType);
        Assert.Equal(setup.UserId, audit.ActorId);
        Assert.NotEqual(setup.ActorId, audit.ActorId);
        Assert.Equal(0.1234m, opened.OpeningAmount);
        Assert.Equal(TimeSpan.Zero, opened.OpenedAt.Offset);
        var owner = await CashSessionTestData.AddOwnerAsync(source, setup);
        CashSessionTestData.Authenticate(source, owner);
        var active = Assert.Single(await source.GetRequiredService<GetActiveCashSessionsHandler>().HandleAsync(
            new(setup.TenantId), TestContext.Current.CancellationToken));
        Assert.Equal(opened.CashSessionId, active.CashSessionId);
        Assert.Equal((setup.MembershipId, setup.UserId, "Staff", "Centro"), (active.MembershipId, active.UserId, active.DisplayName, active.BranchName));
    }

    [Fact]
    public async Task ConcurrentOpeningsHaveOneWinnerAndOneStableConflictWithNoOrphanAudit()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        IdentityAccessTestSetup.Setup setup;
        await using (var scope = services.CreateAsyncScope()) setup = await CashSessionTestData.CreateAsync(scope.ServiceProvider);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var gate = new OpeningGate();
        async Task<(OpenCashSessionDetails? Session, ApplicationError? Error)> AttemptAsync()
        {
            await using var scope = services.CreateAsyncScope();
            var source = scope.ServiceProvider;
            CashSessionTestData.Authenticate(source, setup.UserId);
            var handler = new OpenCashSessionHandler(source.GetRequiredService<ResolveAccessContextHandler>(),
                new GatedWriter(source.GetRequiredService<IOpenCashSessionWriter>(), gate), new IdentityAccessTestSetup.Clock());
            try { return (await handler.HandleAsync(new(setup.TenantId, setup.BranchId, 0m), timeout.Token), null); }
            catch (ApplicationErrorException error) { return (null, error.Error); }
        }
        var results = await Task.WhenAll(AttemptAsync(), AttemptAsync());
        Assert.Single(results, value => value.Session != null);
        Assert.Equal(CashSessionErrors.AlreadyOpen, Assert.Single(results, value => value.Error != null).Error);
        await using var verify = fixture.CreateContext(setup.TenantId);
        Assert.Single(await verify.CashSessions.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Single(await verify.AuditLogs.Where(value => value.Action == AuditAction.CashSessionOpened).ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DifferentMembershipsCanOpenSameBranchAndSameMembershipCanOpenDifferentBranches()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var setup = await CashSessionTestData.CreateAsync(source, TenantRole.Owner);
        var first = await CashSessionTestData.OpenAsync(source, setup);
        var user = await IdentityAccessTestSetup.CreateUserAsync(source);
        var membership = await source.GetRequiredService<CreateMembershipHandler>().HandleAsync(
            new(setup.TenantId, user.Id, TenantRole.Owner, setup.ActorId), TestContext.Current.CancellationToken);
        var second = await CashSessionTestData.OpenAsync(source, setup with { UserId = user.Id, MembershipId = membership.Id });
        var context = source.GetRequiredService<MediPosDbContext>();
        var legal = await context.Branches.Select(value => value.LegalEntityId).SingleAsync(TestContext.Current.CancellationToken);
        var branch = await source.GetRequiredService<CreateBranchHandler>().HandleAsync(
            new(setup.TenantId, legal, "Otra sede", setup.ActorId), TestContext.Current.CancellationToken);
        var third = await CashSessionTestData.OpenAsync(source, setup with { BranchId = branch.Id });
        Assert.Equal(3, await context.CashSessions.CountAsync(TestContext.Current.CancellationToken));
        CashSessionTestData.Authenticate(source, setup.UserId);
        var list = source.GetRequiredService<GetActiveCashSessionsHandler>();
        var branchResults = await list.HandleAsync(new(setup.TenantId, setup.BranchId), TestContext.Current.CancellationToken);
        Assert.Equal(2, branchResults.Count);
        Assert.DoesNotContain(branchResults, value => value.CashSessionId == third.CashSessionId);
        var page = await list.HandleAsync(new(setup.TenantId, null, 0, 1), TestContext.Current.CancellationToken);
        var next = await list.HandleAsync(new(setup.TenantId, null, 1, 1), TestContext.Current.CancellationToken);
        Assert.Single(page);
        Assert.Single(next);
        Assert.NotEqual(page[0].CashSessionId, next[0].CashSessionId);
        Assert.NotEqual(first.CashSessionId, second.CashSessionId);
    }

    [Fact]
    public async Task ClosedHistoricalRowsDoNotBlockFutureOpeningAndAreExcludedFromReads()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var setup = await CashSessionTestData.CreateAsync(source, TenantRole.Owner);
        var historical = await CashSessionTestData.OpenAsync(source, setup);
        await source.GetRequiredService<CloseCashSessionHandler>().HandleAsync(
            CashCloseTestData.Command(setup.TenantId, setup.BranchId, historical.CashSessionId), TestContext.Current.CancellationToken);
        var current = await CashSessionTestData.OpenAsync(source, setup);
        Assert.Equal(current, await source.GetRequiredService<IFindOpenCashSession>().FindAsync(
            setup.TenantId, setup.BranchId, setup.MembershipId, TestContext.Current.CancellationToken));
        var active = Assert.Single(await source.GetRequiredService<GetActiveCashSessionsHandler>().HandleAsync(
            new(setup.TenantId), TestContext.Current.CancellationToken));
        Assert.Equal(current.CashSessionId, active.CashSessionId);
        Assert.NotEqual(historical.CashSessionId, active.CashSessionId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InjectedSessionOrAuditFailureRollsBackBothAndClearsTracker(bool failAudit)
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var setup = await CashSessionTestData.CreateAsync(source);
        var constraint = "test_cash_" + Guid.NewGuid().ToString("N");
        await using var admin = fixture.CreateConstraintContext();
        var ddl = failAudit
            ? await admin.Database.SqlQuery<string>($"SELECT format('ALTER TABLE audit_logs ADD CONSTRAINT %I CHECK (action <> ''cash_session.opened'' OR tenant_id <> %L::uuid)', {constraint}, {setup.TenantId.ToString()}) AS \"Value\"").SingleAsync(TestContext.Current.CancellationToken)
            : await admin.Database.SqlQuery<string>($"SELECT format('ALTER TABLE cash_sessions ADD CONSTRAINT %I CHECK (tenant_id <> %L::uuid)', {constraint}, {setup.TenantId.ToString()}) AS \"Value\"").SingleAsync(TestContext.Current.CancellationToken);
        await admin.Database.ExecuteSqlRawAsync(ddl, TestContext.Current.CancellationToken);
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => CashSessionTestData.OpenAsync(source, setup));
            var context = source.GetRequiredService<MediPosDbContext>();
            Assert.Empty(context.ChangeTracker.Entries());
            Assert.Empty(await context.CashSessions.ToListAsync(TestContext.Current.CancellationToken));
            Assert.Empty(await context.AuditLogs.Where(value => value.Action == AuditAction.CashSessionOpened).ToListAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            var drop = failAudit
                ? await admin.Database.SqlQuery<string>($"SELECT format('ALTER TABLE audit_logs DROP CONSTRAINT %I', {constraint}) AS \"Value\"").SingleAsync(TestContext.Current.CancellationToken)
                : await admin.Database.SqlQuery<string>($"SELECT format('ALTER TABLE cash_sessions DROP CONSTRAINT %I', {constraint}) AS \"Value\"").SingleAsync(TestContext.Current.CancellationToken);
            await admin.Database.ExecuteSqlRawAsync(drop, TestContext.Current.CancellationToken);
        }
        Assert.NotEqual(Guid.Empty, (await CashSessionTestData.OpenAsync(source, setup)).CashSessionId);
    }

    [Fact]
    public async Task OpeningDoesNotWaitForAnUnrelatedLicenseRowLock()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        IdentityAccessTestSetup.Setup setup;
        await using (var scope = services.CreateAsyncScope()) setup = await CashSessionTestData.CreateAsync(scope.ServiceProvider);
        await using var locked = fixture.CreateContext(setup.TenantId);
        await using var transaction = await locked.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await locked.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM licenses WHERE id = {setup.LicenseId} FOR UPDATE", TestContext.Current.CancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await using var opening = services.CreateAsyncScope();
        CashSessionTestData.Authenticate(opening.ServiceProvider, setup.UserId);
        var result = await opening.ServiceProvider.GetRequiredService<OpenCashSessionHandler>().HandleAsync(
            new(setup.TenantId, setup.BranchId, 0m), timeout.Token);
        Assert.NotEqual(Guid.Empty, result.CashSessionId);
    }

    private sealed class OpeningGate
    {
        private int _arrivals;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task WaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrivals) == 2) _ready.SetResult();
            return _ready.Task.WaitAsync(cancellationToken);
        }
    }
    private sealed class GatedWriter(IOpenCashSessionWriter inner, OpeningGate gate) : IOpenCashSessionWriter
    {
        public async Task SaveAsync(MediPOS.Domain.Modules.Cash.CashSession session, AuditLog audit, CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);
            await inner.SaveAsync(session, audit, cancellationToken);
        }
    }
}
