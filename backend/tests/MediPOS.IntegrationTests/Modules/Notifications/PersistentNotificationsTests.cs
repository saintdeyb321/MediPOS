using System.Data.Common;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog.SetBusinessProductStatus;
using MediPOS.Application.Modules.IdentityAccess.CreateMembership;
using MediPOS.Application.Modules.IdentityAccess.ReplaceWorkSchedule;
using MediPOS.Application.Modules.IdentityAccess.SetMembershipBranches;
using MediPOS.Application.Modules.Notifications;
using MediPOS.Application.Modules.Reporting.Operational;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.Notifications;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.Reporting;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.Notifications;

[Collection("PostgreSQL")]
public sealed class PersistentNotificationsTests(PostgreSqlFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TwoConcurrentJobsKeepOneAlertPerEligibleLotAndHistoryAfterSaleWithIndependentReads()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var first = OwnerOverviewTestData.CreateServices(fixture);
        await using var second = OwnerOverviewTestData.CreateServices(fixture);
        await using var a = first.CreateAsyncScope(); await using var b = first.CreateAsyncScope();
        var scenarioA = await NotificationTestData.SeedAsync(a.ServiceProvider, pair.A);
        var scenarioB = await NotificationTestData.SeedAsync(b.ServiceProvider, pair.B);
        await Task.WhenAll(first.GetRequiredService<RunExpirationAlertsJob>().HandleAsync(Token), second.GetRequiredService<RunExpirationAlertsJob>().HandleAsync(Token));
        var listA = await NotificationTestData.ListAsync(a.ServiceProvider, pair.A.TenantId);
        var listB = await NotificationTestData.ListAsync(b.ServiceProvider, pair.B.TenantId);
        Assert.Equal(6, listA.TotalCount); Assert.Equal(6, listB.TotalCount);
        Assert.Equal(6, listA.Rows.Select(n => n.InventoryLotId).Distinct().Count());
        Assert.All(listA.Rows, n => Assert.True(n.DaysUntilExpiration is 0 or 10 or 30));
        Assert.Equal(2, listA.Rows.Select(n => n.BranchId).Distinct().Count());
        Assert.Empty(listA.Rows.Select(n => n.InventoryLotId).Intersect(listB.Rows.Select(n => n.InventoryLotId)));
        var notice = listA.Rows.Single(n => n.InventoryLotId == scenarioA.TodayLotId);
        await NotificationTestData.MarkAsync(a.ServiceProvider, pair.A.TenantId, notice.NotificationId);
        CashSessionTestData.Authenticate(a.ServiceProvider, pair.A.Identity.UserId);
        var cashier = await NotificationTestData.ListAsync(a.ServiceProvider, pair.A.TenantId, pair.A.Identity.BranchId);
        Assert.Equal(3, cashier.Rows.Count); Assert.False(cashier.Rows.Single(n => n.NotificationId == notice.NotificationId).IsRead);
        Assert.Equal(3, await NotificationTestData.UnreadAsync(a.ServiceProvider, pair.A.TenantId, pair.A.Identity.BranchId));
        await NotificationTestData.ExhaustTodayAsync(a.ServiceProvider, scenarioA);
        await first.GetRequiredService<RunExpirationAlertsJob>().HandleAsync(Token);
        var history = await NotificationTestData.ListAsync(a.ServiceProvider, pair.A.TenantId);
        var afterSale = history.Rows.Single(n => n.NotificationId == notice.NotificationId);
        Assert.False(afterSale.IsCurrentlyActionable); Assert.True(afterSale.IsRead); Assert.Equal(6, history.TotalCount);
        Assert.Equal(scenarioB.ProductId, Assert.Single(listB.Rows.Select(n => n.ProductId).Distinct()));
        var foreign = await Assert.ThrowsAsync<ApplicationErrorException>(() => NotificationTestData.MarkAsync(a.ServiceProvider, pair.A.TenantId, listB.Rows[0].NotificationId));
        Assert.Equal("notification.not_found", foreign.Error.Code);
    }

    [Fact]
    public async Task AppliedMigrationMatchesCurrentModelAndHasNoPendingChanges()
    {
        await using var context = fixture.CreateContext();
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Contains((await context.Database.GetAppliedMigrationsAsync(Token)), m => m.EndsWith("AddPersistentExpirationNotifications", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("notifications")]
    [InlineData("notification_read_states")]
    public async Task TablesForceRlsWithUsingAndWithCheckAndRestrictedRuntimePrivileges(string table)
    {
        await using var context = fixture.CreateContext();
        var protection = await context.Database.SqlQuery<Protection>($"""
            SELECT c.relrowsecurity AS "Enabled", c.relforcerowsecurity AS "Forced",
                EXISTS (SELECT 1 FROM pg_policy p WHERE p.polrelid=c.oid AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL) AS "Policy",
                has_table_privilege(current_user, c.oid, 'SELECT') AND has_table_privilege(current_user, c.oid, 'INSERT') AS "ReadWrite",
                has_table_privilege(current_user, c.oid, 'UPDATE') OR has_table_privilege(current_user, c.oid, 'DELETE') OR has_table_privilege(current_user, c.oid, 'TRUNCATE') AS "Destructive"
            FROM pg_class c WHERE c.relname={table}
            """).SingleAsync(Token);
        Assert.True(protection.Enabled); Assert.True(protection.Forced); Assert.True(protection.Policy); Assert.True(protection.ReadWrite); Assert.False(protection.Destructive);
    }

    [Theory]
    [InlineData("lot")]
    [InlineData("product")]
    [InlineData("branch")]
    [InlineData("membership")]
    [InlineData("notification")]
    public async Task CompositeForeignKeysRejectCrossTenantReferences(string reference)
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OwnerOverviewTestData.CreateServices(fixture);
        await using var a = services.CreateAsyncScope(); await using var b = services.CreateAsyncScope();
        var sa = await NotificationTestData.SeedAsync(a.ServiceProvider, pair.A); var sb = await NotificationTestData.SeedAsync(b.ServiceProvider, pair.B);
        await services.GetRequiredService<RunExpirationAlertsJob>().HandleAsync(Token);
        var na = (await NotificationTestData.ListAsync(a.ServiceProvider, pair.A.TenantId)).Rows[0];
        var nb = (await NotificationTestData.ListAsync(b.ServiceProvider, pair.B.TenantId)).Rows[0];
        await using var context = fixture.CreateConstraintContext(pair.A.TenantId);
        if (reference is "membership" or "notification")
            context.NotificationReadStates.Add(NotificationReadState.Read(pair.A.TenantId, reference == "notification" ? nb.NotificationId : na.NotificationId,
                reference == "membership" ? pair.B.Identity.MembershipId : pair.A.Identity.MembershipId, IdentityAccessTestSetup.Now));
        else
            context.Notifications.Add(Notification.ExpiringLot(pair.A.TenantId, reference == "branch" ? pair.B.Identity.BranchId : pair.A.Identity.BranchId,
                reference == "lot" ? sb.TodayLotId : sa.TodayLotId, reference == "product" ? sb.ProductId : sa.ProductId,
                NotificationTestData.Today.AddDays(1), IdentityAccessTestSetup.Now));
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Token));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
    }

    [Fact]
    public async Task RepeatedJobAndRestartPreserveOriginalNotificationIdsAndTimes()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = OwnerOverviewTestData.CreateServices(fixture); await using var scope = services.CreateAsyncScope();
        await NotificationTestData.SeedAsync(scope.ServiceProvider, pair.A); await services.GetRequiredService<RunExpirationAlertsJob>().HandleAsync(Token);
        var first = await NotificationTestData.ListAsync(scope.ServiceProvider, pair.A.TenantId);
        await services.GetRequiredService<RunExpirationAlertsJob>().HandleAsync(Token);
        await using var restarted = OwnerOverviewTestData.CreateServices(fixture); await restarted.GetRequiredService<RunExpirationAlertsJob>().HandleAsync(Token);
        var again = await NotificationTestData.ListAsync(scope.ServiceProvider, pair.A.TenantId);
        Assert.Equal(first.Rows.Select(n => (n.NotificationId, n.CreatedAt)), again.Rows.Select(n => (n.NotificationId, n.CreatedAt)));
    }

    [Fact]
    public async Task ZeroStockDoesNotGenerateNotification()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = OwnerOverviewTestData.CreateServices(fixture); await using var scope = services.CreateAsyncScope();
        var scenario = await NotificationTestData.SeedAsync(scope.ServiceProvider, pair.A);
        await NotificationTestData.ExhaustTodayAsync(scope.ServiceProvider, scenario); await services.GetRequiredService<RunExpirationAlertsJob>().HandleAsync(Token);
        var rows = await NotificationTestData.ListAsync(scope.ServiceProvider, pair.A.TenantId);
        Assert.Equal(5, rows.TotalCount); Assert.DoesNotContain(rows.Rows, n => n.InventoryLotId == scenario.TodayLotId);
    }

    [Fact]
    public async Task ExpiredAndRetailLotsRemainSeparateFromUpcomingMedicineAlerts()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OwnerOverviewTestData.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope(); var scenario = await NotificationTestData.SeedAsync(scope.ServiceProvider, pair.A);
        await services.GetRequiredService<RunExpirationAlertsJob>().HandleAsync(Token);
        var notices = await NotificationTestData.ListAsync(scope.ServiceProvider, pair.A.TenantId);
        Assert.All(notices.Rows, n => { Assert.Equal(scenario.ProductId, n.ProductId); Assert.InRange(n.DaysUntilExpiration, 0, 30); });
        var report = await scope.ServiceProvider.GetRequiredService<GetOwnerExpirationReportHandler>().HandleAsync(
            new(pair.A.TenantId, null, NotificationTestData.Today.AddDays(-1), NotificationTestData.Today.AddDays(31)), Token);
        Assert.Equal(2, report.Rows.Count(n => n.State == OwnerExpirationState.Expired));
    }

    [Theory]
    [InlineData(TenantRole.Cashier)]
    [InlineData(TenantRole.Pharmacist)]
    public async Task StaffCannotConsultOrMarkUnassignedBranch(TenantRole role)
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OwnerOverviewTestData.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider; var scenario = await NotificationTestData.SeedAsync(source, pair.A);
        await services.GetRequiredService<RunExpirationAlertsJob>().HandleAsync(Token);
        var foreignBranch = (await NotificationTestData.ListAsync(source, pair.A.TenantId, pair.A.SpareBranchId)).Rows[0];
        var staff = pair.A.Identity;
        if (role == TenantRole.Pharmacist)
        {
            var user = await IdentityAccessTestSetup.CreateUserAsync(source);
            var member = await source.GetRequiredService<CreateMembershipHandler>().HandleAsync(new(pair.A.TenantId, user.Id, role, scenario.Owner.UserId), Token);
            await source.GetRequiredService<SetMembershipBranchesHandler>().HandleAsync(new(pair.A.TenantId, member.Id, [staff.BranchId], scenario.Owner.UserId), Token);
            await source.GetRequiredService<ReplaceWorkScheduleHandler>().HandleAsync(new(pair.A.TenantId, member.Id, [new(DayOfWeek.Tuesday, new(9, 0), new(18, 0))], scenario.Owner.UserId), Token);
            staff = staff with { UserId = user.Id, MembershipId = member.Id };
        }
        CashSessionTestData.Authenticate(source, staff.UserId);
        Assert.Equal(3, (await NotificationTestData.ListAsync(source, pair.A.TenantId, staff.BranchId)).Rows.Count);
        Assert.Equal("access.branch_unassigned", (await Assert.ThrowsAsync<ApplicationErrorException>(() => NotificationTestData.ListAsync(source, pair.A.TenantId, pair.A.SpareBranchId))).Error.Code);
        Assert.Equal("access.branch_unassigned", (await Assert.ThrowsAsync<ApplicationErrorException>(() => NotificationTestData.MarkAsync(source, pair.A.TenantId, foreignBranch.NotificationId))).Error.Code);
    }

    [Fact]
    public async Task ConcurrentDoubleReadReturnsOriginalStateAndOnlyOneRow()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OwnerOverviewTestData.CreateServices(fixture);
        await using var seed = services.CreateAsyncScope(); var scenario = await NotificationTestData.SeedAsync(seed.ServiceProvider, pair.A);
        await services.GetRequiredService<RunExpirationAlertsJob>().HandleAsync(Token);
        var notice = (await NotificationTestData.ListAsync(seed.ServiceProvider, pair.A.TenantId)).Rows[0];
        await using var first = services.CreateAsyncScope(); await using var second = services.CreateAsyncScope();
        CashSessionTestData.Authenticate(first.ServiceProvider, scenario.Owner.UserId); CashSessionTestData.Authenticate(second.ServiceProvider, scenario.Owner.UserId);
        var result = await Task.WhenAll(NotificationTestData.MarkAsync(first.ServiceProvider, pair.A.TenantId, notice.NotificationId),
            NotificationTestData.MarkAsync(second.ServiceProvider, pair.A.TenantId, notice.NotificationId));
        Assert.Equal(result[0], result[1]); Assert.Equal(result[0], await NotificationTestData.MarkAsync(first.ServiceProvider, pair.A.TenantId, notice.NotificationId));
        await using var context = fixture.CreateContext(pair.A.TenantId);
        Assert.Single(await context.NotificationReadStates.Where(r => r.NotificationId == notice.NotificationId && r.MembershipId == scenario.Owner.MembershipId).ToArrayAsync(Token));
    }

    [Theory]
    [InlineData("notifications", "UPDATE")]
    [InlineData("notifications", "DELETE")]
    [InlineData("notification_read_states", "UPDATE")]
    [InlineData("notification_read_states", "DELETE")]
    public async Task RuntimeAndDatabaseTriggersRefuseDestructiveHistoryMutations(string table, string operation)
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OwnerOverviewTestData.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope(); await NotificationTestData.SeedAsync(scope.ServiceProvider, pair.A);
        await services.GetRequiredService<RunExpirationAlertsJob>().HandleAsync(Token);
        var notice = (await NotificationTestData.ListAsync(scope.ServiceProvider, pair.A.TenantId)).Rows[0];
        await NotificationTestData.MarkAsync(scope.ServiceProvider, pair.A.TenantId, notice.NotificationId);
        var sql = operation == "UPDATE" ? $"UPDATE {table} SET id=id" : $"DELETE FROM {table}";
        await using var runtime = fixture.CreateContext(pair.A.TenantId);
        var denied = await Assert.ThrowsAsync<PostgresException>(() => runtime.Database.ExecuteSqlRawAsync(sql, Token)); Assert.Equal("42501", denied.SqlState);
        await using var admin = fixture.CreateConstraintContext(pair.A.TenantId);
        var trigger = await Assert.ThrowsAsync<PostgresException>(() => admin.Database.ExecuteSqlRawAsync(sql, Token)); Assert.Equal("42501", trigger.SqlState);
    }

    [Fact]
    public async Task PoolReuseAndRawSqlCannotLeakAnotherTenantOrUnselectedScope()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture);
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { MaxPoolSize = 1, NoResetOnClose = true, ApplicationName = Guid.NewGuid().ToString("N") }.ConnectionString;
        await using var services = OwnerOverviewTestData.CreateServices(fixture, connectionString: connection);
        int backend;
        await using (var a = services.CreateAsyncScope())
        {
            await NotificationTestData.SeedAsync(a.ServiceProvider, pair.A); var context = a.ServiceProvider.GetRequiredService<MediPosDbContext>();
            backend = await context.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(Token);
        }
        await using (var b = services.CreateAsyncScope()) { await NotificationTestData.SeedAsync(b.ServiceProvider, pair.B); }
        await services.GetRequiredService<RunExpirationAlertsJob>().HandleAsync(Token);
        foreach (var tenant in new[] { pair.A, pair.B, pair.A })
        {
            await using var scope = services.CreateAsyncScope(); var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>(); context.SelectTenant(tenant.TenantId);
            Assert.Equal(backend, await context.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(Token));
            Assert.Equal(6, await context.Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM notifications").SingleAsync(Token));
            var other = tenant.TenantId == pair.A.TenantId ? pair.B.TenantId : pair.A.TenantId;
            Assert.Empty(await context.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM notifications WHERE tenant_id={other}").ToArrayAsync(Token));
        }
        await using var empty = services.CreateAsyncScope();
        Assert.Empty(await empty.ServiceProvider.GetRequiredService<MediPosDbContext>().Database.SqlQueryRaw<Guid>("SELECT id AS \"Value\" FROM notifications").ToArrayAsync(Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCancelledTenantRollsBackBatchAndRetryUsesFreshScopes(bool cancel)
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var seedServices = OwnerOverviewTestData.CreateServices(fixture);
        await using (var a = seedServices.CreateAsyncScope()) { await NotificationTestData.SeedAsync(a.ServiceProvider, pair.A); }
        await using (var b = seedServices.CreateAsyncScope()) { await NotificationTestData.SeedAsync(b.ServiceProvider, pair.B); }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var fault = new Fault(pair.A.TenantId, cancel ? cancellation : null);
        await using var failed = OwnerOverviewTestData.CreateServices(fixture, observer: fault);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => failed.GetRequiredService<RunExpirationAlertsJob>().HandleAsync(cancellation.Token));
        else
        {
            var result = await failed.GetRequiredService<RunExpirationAlertsJob>().HandleAsync(Token); Assert.Equal(1, result.FailedTenants);
            await using var b = fixture.CreateContext(pair.B.TenantId); Assert.Equal(6, await b.Notifications.LongCountAsync(Token));
        }
        await using (var a = fixture.CreateContext(pair.A.TenantId)) { Assert.Empty(await a.Notifications.ToArrayAsync(Token)); }
        await seedServices.GetRequiredService<RunExpirationAlertsJob>().HandleAsync(Token);
        await using var retried = fixture.CreateContext(pair.A.TenantId); Assert.Equal(6, await retried.Notifications.LongCountAsync(Token));
    }

    [Fact]
    public async Task PagingUsesSqlCountsAndUnreadCountsRespectMembershipAndBranch()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); var observer = new OwnerOverviewTestData.SqlObserver();
        await using var services = OwnerOverviewTestData.CreateServices(fixture, observer: observer); await using var scope = services.CreateAsyncScope();
        await NotificationTestData.SeedAsync(scope.ServiceProvider, pair.A); await services.GetRequiredService<RunExpirationAlertsJob>().HandleAsync(Token);
        var all = await NotificationTestData.ListAsync(scope.ServiceProvider, pair.A.TenantId); observer.Reset();
        var page = await NotificationTestData.ListAsync(scope.ServiceProvider, pair.A.TenantId, offset: 1, limit: 2);
        Assert.Equal(all.Rows.Skip(1).Take(2).Select(n => n.NotificationId), page.Rows.Select(n => n.NotificationId)); Assert.Equal(6, page.TotalCount);
        Assert.Contains(observer.Selects, sql => sql.Contains("LIMIT", StringComparison.Ordinal));
        Assert.Contains(observer.Selects, sql => sql.Contains("count(*)", StringComparison.Ordinal));
        await NotificationTestData.MarkAsync(scope.ServiceProvider, pair.A.TenantId, all.Rows[0].NotificationId); observer.Reset();
        Assert.Equal(5, await NotificationTestData.UnreadAsync(scope.ServiceProvider, pair.A.TenantId));
        Assert.Contains(observer.Selects, sql => sql.Contains("NOT EXISTS", StringComparison.Ordinal) && sql.Contains("count(*)", StringComparison.Ordinal));
        Assert.Equal(5, (await NotificationTestData.ListAsync(scope.ServiceProvider, pair.A.TenantId, unread: true)).TotalCount);
    }

    [Fact]
    public async Task HistoryRemainsAfterProductDeactivationAndLimaDateChanges()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); var clock = new OwnerOverviewTestData.Clock();
        await using var services = OwnerOverviewTestData.CreateServices(fixture, clock); await using var scope = services.CreateAsyncScope();
        var scenario = await NotificationTestData.SeedAsync(scope.ServiceProvider, pair.A); await services.GetRequiredService<RunExpirationAlertsJob>().HandleAsync(Token);
        await scope.ServiceProvider.GetRequiredService<SetBusinessProductStatusHandler>().HandleAsync(new(pair.A.TenantId, scenario.ProductId, false, scenario.Owner.UserId), Token);
        clock.Now = new(2026, 10, 7, 4, 59, 0, TimeSpan.Zero);
        var before = await NotificationTestData.ListAsync(scope.ServiceProvider, pair.A.TenantId); clock.Now = clock.Now.AddMinutes(1);
        var after = await NotificationTestData.ListAsync(scope.ServiceProvider, pair.A.TenantId);
        Assert.Equal(before.Rows.Select(n => n.NotificationId), after.Rows.Select(n => n.NotificationId));
        Assert.All(after.Rows, n => Assert.False(n.IsCurrentlyActionable));
        Assert.Equal(-1, after.Rows.Single(n => n.InventoryLotId == scenario.TodayLotId).DaysUntilExpiration);
    }

    private sealed record Protection(bool Enabled, bool Forced, bool Policy, bool ReadWrite, bool Destructive);
    private sealed class Fault(Guid tenant, CancellationTokenSource? cancellation) : DbCommandInterceptor
    {
        private int _inserts;
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("INSERT INTO notifications", StringComparison.Ordinal) && command.Parameters.Cast<DbParameter>().Any(p => p.Value is Guid id && id == tenant) && ++_inserts == 2)
            {
                if (cancellation is not null) { cancellation.Cancel(); cancellationToken.ThrowIfCancellationRequested(); }
                throw new InvalidOperationException("Injected tenant batch failure");
            }
            return ValueTask.FromResult(result);
        }
    }
}
