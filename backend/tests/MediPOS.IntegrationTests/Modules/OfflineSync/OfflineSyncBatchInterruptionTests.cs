using System.Data.Common;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.OfflineSync;
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
using static MediPOS.IntegrationTests.Modules.OfflineSync.OfflineSyncBatchTestData;

namespace MediPOS.IntegrationTests.Modules.OfflineSync;

[Collection("PostgreSQL")]
public sealed class OfflineSyncBatchInterruptionTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task ConcurrentRevocationBetweenEventsStopsFourthAndRetainsFirstThreeDurableReceipts()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); var observer = new CommitObserver();
        await using var services = CreateServices(fixture, observer); await using var scope = services.CreateAsyncScope();
        var device = await Register(scope.ServiceProvider, pair.A); var events = Enumerable.Range(0, 5).Select(_ => Envelope(device)).ToArray();
        var third = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        observer.Armed = true; observer.AfterCommit = async count => { if (count == 3) { third.TrySetResult(); await resume.Task.WaitAsync(Token); } };
        var batch = Run(scope.ServiceProvider, pair.A, device, events);
        try
        {
            await third.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            // COMMIT has released Device/Event locks; a different context can revoke before event four begins.
            await using var revokers = CreateServices(fixture); await using var revokeScope = revokers.CreateAsyncScope();
            CashSessionTestData.Authenticate(revokeScope.ServiceProvider, pair.A.Identity.UserId);
            await revokeScope.ServiceProvider.GetRequiredService<RevokeSyncDeviceHandler>().HandleAsync(new(pair.A.TenantId, device), Token);
        }
        finally { resume.TrySetResult(); }
        Assert.Equal("sync.device_revoked", (await Assert.ThrowsAsync<ApplicationErrorException>(() => batch)).Error.Code);
        await using var context = fixture.CreateContext(pair.A.TenantId); var durable = await context.SyncEvents.Select(receipt => receipt.EventId).ToArrayAsync(Token);
        Assert.Equal(events.Take(3).Select(envelope => envelope.EventId).Order(), durable.Order()); Assert.Empty(await context.Sales.ToArrayAsync(Token));
        Assert.Equal("sync.device_revoked", (await Assert.ThrowsAsync<ApplicationErrorException>(() => Run(scope.ServiceProvider, pair.A, device, events))).Error.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAfterCommitsOrDuringNextReservationKeepsCommittedPrefixRecoverable(bool duringReservation)
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var commits = new CommitObserver(); var reserve = new InsertObserver();
        await using var services = CreateServices(fixture, duringReservation ? reserve : commits); await using var scope = services.CreateAsyncScope();
        var device = await Register(scope.ServiceProvider, pair.A); var events = Enumerable.Range(0, 4).Select(_ => Envelope(device)).ToArray();
        commits.Armed = true; commits.AfterCommit = count => { if (count == 2) cancellation.Cancel(); return Task.CompletedTask; };
        reserve.OnInsert = count => { if (count == 3) { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); } return Task.CompletedTask; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(scope.ServiceProvider, pair.A, device, events, cancellation.Token));
        await using (var context = fixture.CreateContext(pair.A.TenantId)) Assert.Equal(2, await context.SyncEvents.CountAsync(Token));
        await using var restarted = CreateServices(fixture); await using var retryScope = restarted.CreateAsyncScope();
        var retry = await Run(retryScope.ServiceProvider, pair.A, device, events);
        Assert.All(retry.Results.Take(2), result => Assert.True(result.IsReplay)); Assert.All(retry.Results.Skip(2), result => Assert.False(result.IsReplay));
        Assert.Equal(4, retry.PendingCount);
    }

    [Fact]
    public async Task LostServerSessionAfterSecondCommitAbortsBeforeThirdAndCannotReadCachedResults()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); var observer = new CommitObserver();
        await using var services = CreateServices(fixture, observer); await using var scope = services.CreateAsyncScope();
        var device = await Register(scope.ServiceProvider, pair.A); var events = Enumerable.Range(0, 4).Select(_ => Envelope(device)).ToArray();
        observer.Armed = true; observer.AfterCommit = count => { if (count == 2) ((IdentityAccessTestSetup.TestServerSession)scope.ServiceProvider.GetRequiredService<IAuthenticatedMediPosUser>()).UserId = null; return Task.CompletedTask; };
        Assert.Equal("access.unauthenticated", (await Assert.ThrowsAsync<ApplicationErrorException>(() => Run(scope.ServiceProvider, pair.A, device, events))).Error.Code);
        Assert.Equal("access.unauthenticated", (await Assert.ThrowsAsync<ApplicationErrorException>(() => scope.ServiceProvider.GetRequiredService<ProcessOfflineSyncBatchHandler>()
            .HandleAsync(new(pair.A.TenantId, device, 1, events), Token))).Error.Code);
        await using var context = fixture.CreateContext(pair.A.TenantId); Assert.Equal(2, await context.SyncEvents.CountAsync(Token));
        Assert.Empty(await context.SalePayments.ToArrayAsync(Token)); Assert.Empty(await context.StockMovements.ToArrayAsync(Token));
    }

    [Fact]
    public async Task PostgreSqlStatementTimeoutOnAnEventLockDoesNotFabricateAcknowledgmentsOrLosePriorCommit()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); Guid device; SyncEventEnvelope existing;
        await using (var seed = CreateServices(fixture))
        {
            await using var scope = seed.CreateAsyncScope(); device = await Register(scope.ServiceProvider, pair.A); existing = Envelope(device);
            await Run(scope.ServiceProvider, pair.A, device, [existing]);
        }
        await using var blocker = fixture.CreateContext(pair.A.TenantId); await using var held = await blocker.Database.BeginTransactionAsync(Token);
        await blocker.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM sync_events WHERE tenant_id={pair.A.TenantId} AND event_id={existing.EventId} FOR UPDATE", Token);
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Options = "-c statement_timeout=1000", ApplicationName = Guid.NewGuid().ToString("N") }.ConnectionString;
        await using var limited = CreateServices(fixture, connectionString: connection); await using var batchScope = limited.CreateAsyncScope();
        var first = Envelope(device); var third = Envelope(device);
        var error = await Assert.ThrowsAsync<PostgresException>(() => Run(batchScope.ServiceProvider, pair.A, device, [first, existing, third]));
        Assert.Equal(PostgresErrorCodes.QueryCanceled, error.SqlState); await held.RollbackAsync(Token);
        await using var context = fixture.CreateContext(pair.A.TenantId); Assert.Equal(2, await context.SyncEvents.CountAsync(Token));
        Assert.False(await context.SyncEvents.AnyAsync(receipt => receipt.EventId == third.EventId, Token));
        await using var recovered = CreateServices(fixture); await using var retryScope = recovered.CreateAsyncScope();
        var retry = await Run(retryScope.ServiceProvider, pair.A, device, [first, existing, third]);
        Assert.True(retry.Results[0].IsReplay); Assert.True(retry.Results[1].IsReplay); Assert.False(retry.Results[2].IsReplay);
    }

    [Fact]
    public async Task ActualConnectionTerminationDuringSecondReservationKeepsOnlyCommittedPrefixAndRetryRecovers()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); var observer = new DisconnectObserver(fixture);
        await using var services = CreateServices(fixture, observer); await using var scope = services.CreateAsyncScope();
        var device = await Register(scope.ServiceProvider, pair.A); var events = Enumerable.Range(0, 3).Select(_ => Envelope(device)).ToArray();
        await Assert.ThrowsAnyAsync<NpgsqlException>(() => Run(scope.ServiceProvider, pair.A, device, events));
        await using (var context = fixture.CreateContext(pair.A.TenantId)) Assert.Equal(1, await context.SyncEvents.CountAsync(Token));
        await using var restarted = CreateServices(fixture); await using var recovered = restarted.CreateAsyncScope();
        var retry = await Run(recovered.ServiceProvider, pair.A, device, events);
        Assert.True(retry.Results[0].IsReplay); Assert.False(retry.Results[1].IsReplay); Assert.False(retry.Results[2].IsReplay);
        Assert.Equal(3, retry.PendingCount);
    }

    [Fact]
    public async Task HighRiskTwoDeviceRaceWithInvalidAndDuplicateEventsRevocationAndWholeBatchRetryKeepsHistory()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); var observer = new CommitObserver();
        await using var firstServices = CreateServices(fixture, observer); await using var secondServices = CreateServices(fixture);
        await using var firstScope = firstServices.CreateAsyncScope(); await using var secondScope = secondServices.CreateAsyncScope();
        var firstDevice = await Register(firstScope.ServiceProvider, pair.A); var secondDevice = await Register(secondScope.ServiceProvider, pair.A);
        var shared = Envelope(firstDevice); var later = Envelope(firstDevice); var invalid = Envelope(firstDevice) with { Payload = "{bad" };
        SyncEventEnvelope[] firstEvents = [shared, shared with { Payload = " { \"quantity\":1.0 }" }, invalid, Envelope(firstDevice, shared.EventId, "{\"quantity\":2}"), later];
        var secondValid = Envelope(secondDevice); var secondLast = Envelope(secondDevice);
        SyncEventEnvelope[] secondEvents = [Envelope(secondDevice, shared.EventId), secondValid, secondValid, Envelope(secondDevice) with { SchemaVersion = 2 }, secondLast];
        var third = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        observer.Armed = true; observer.AfterCommit = async count => { if (count == 3) { third.TrySetResult(); await resume.Task.WaitAsync(Token); } };
        // Different requests/providers/contexts. The winning device for the shared EventId is deliberately unspecified.
        var firstBatch = Run(firstScope.ServiceProvider, pair.A, firstDevice, firstEvents);
        var secondBatch = Run(secondScope.ServiceProvider, pair.A, secondDevice, secondEvents);
        OfflineSyncBatchResult secondResult;
        try
        {
            await third.Task.WaitAsync(TimeSpan.FromSeconds(30), Token); secondResult = await secondBatch;
            CashSessionTestData.Authenticate(secondScope.ServiceProvider, pair.A.Identity.UserId);
            await secondScope.ServiceProvider.GetRequiredService<RevokeSyncDeviceHandler>().HandleAsync(new(pair.A.TenantId, firstDevice), Token);
        }
        finally { resume.TrySetResult(); }
        Assert.Equal("sync.device_revoked", (await Assert.ThrowsAsync<ApplicationErrorException>(() => firstBatch)).Error.Code);
        Assert.Equal(1, secondResult.RejectedCount); Assert.All(secondResult.Results.Where(result => result.Status == "conflict"), result =>
        { Assert.Null(result.ReceiptId); Assert.Null(result.ResultReferenceId); Assert.Null(result.ReceivedAtUtc); });
        var replay = await Run(secondScope.ServiceProvider, pair.A, secondDevice, secondEvents);
        Assert.Equal(secondResult.Results.Select(result => result.Status), replay.Results.Select(result => result.Status));
        Assert.Equal(secondResult.Results.Select(result => result.ReceiptId), replay.Results.Select(result => result.ReceiptId));
        Assert.Equal("sync.device_revoked", (await Assert.ThrowsAsync<ApplicationErrorException>(() => Run(firstScope.ServiceProvider, pair.A, firstDevice, firstEvents))).Error.Code);
        await using var context = fixture.CreateContext(pair.A.TenantId); Assert.Equal(3, await context.SyncEvents.CountAsync(Token));
        Assert.Equal(1, await context.SyncEvents.CountAsync(receipt => receipt.EventId == shared.EventId, Token));
        Assert.False(await context.SyncEvents.AnyAsync(receipt => receipt.EventId == later.EventId || receipt.EventId == invalid.EventId, Token));
        Assert.Empty(await context.Sales.ToArrayAsync(Token)); Assert.Empty(await context.SalePayments.ToArrayAsync(Token)); Assert.Empty(await context.StockMovements.ToArrayAsync(Token));
    }

    private sealed class CommitObserver : DbTransactionInterceptor
    {
        private int _commits;
        internal bool Armed { get; set; }
        internal Func<int, Task>? AfterCommit { get; set; }
        public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Armed && AfterCommit is not null) await AfterCommit(++_commits);
        }
    }
    private sealed class InsertObserver : DbCommandInterceptor
    {
        private int _inserts;
        internal Func<int, Task>? OnInsert { get; set; }
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("INSERT INTO sync_events", StringComparison.Ordinal) && OnInsert is not null) await OnInsert(++_inserts);
            return result;
        }
    }
    private sealed class DisconnectObserver(PostgreSqlFixture fixture) : DbCommandInterceptor
    {
        private int _inserts;
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("INSERT INTO sync_events", StringComparison.Ordinal) && ++_inserts == 2)
            {
                // Terminate only this controlled batch's own backend, never an unrelated PostgreSQL session.
                var pid = ((NpgsqlConnection)command.Connection!).ProcessID;
                await using var admin = fixture.CreateConstraintContext();
                Assert.True(await admin.Database.SqlQuery<bool>($"SELECT pg_terminate_backend({pid}) AS \"Value\"").SingleAsync(Token));
            }
            return result;
        }
    }
}
