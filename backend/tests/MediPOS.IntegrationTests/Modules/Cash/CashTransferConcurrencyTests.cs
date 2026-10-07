using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.Cash.CloseCashSession;
using MediPOS.Application.Modules.Cash.DispatchCashTransfer;
using MediPOS.Application.Modules.Cash.ReceiveCashTransfer;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.VoidSale;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.SalesPos;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.Cash;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class CashTransferConcurrencyTests(PostgreSqlFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DispatchAndCashRefundCannotBothSpendTheSameExpectedCash(bool voidFirst)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        CashTransferTestData.Rows rows; SaleVoidTestData.Rows sale;
        await using (var scope = services.CreateAsyncScope())
        {
            sale = await SaleVoidTestData.CreateAsync(scope.ServiceProvider, tenant);
            rows = await CashTransferTestData.CreateAsync(scope.ServiceProvider, tenant, existingSource: sale.Checkout.Cash.CashSessionId);
        }
        var marker = "cash-refund-race-" + Guid.NewGuid().ToString("N");
        await using var loserServices = SaleCheckoutTestData.CreateServices(fixture, marker);
        await using var winnerScope = services.CreateAsyncScope(); await using var loserScope = loserServices.CreateAsyncScope();
        var voidSource = voidFirst ? winnerScope.ServiceProvider : loserScope.ServiceProvider;
        var dispatchSource = voidFirst ? loserScope.ServiceProvider : winnerScope.ServiceProvider;
        CashSessionTestData.Authenticate(voidSource, rows.Source.UserId); CashSessionTestData.Authenticate(dispatchSource, rows.Source.UserId);
        var held = new HeldGate();
        var voidPort = voidSource.GetRequiredService<ISaleVoidTransaction>();
        var voidHandler = new VoidSaleHandler(voidSource.GetRequiredService<ResolveAccessContextHandler>(), voidFirst ? new VoidTransaction(voidPort, held) : voidPort,
            voidSource.GetRequiredService<TimeProvider>());
        var dispatchPort = dispatchSource.GetRequiredService<ICashTransferTransaction>();
        var dispatchHandler = new DispatchCashTransferHandler(dispatchSource.GetRequiredService<ResolveAccessContextHandler>(),
            voidFirst ? dispatchPort : new TransferTransaction(dispatchPort, afterCashLock: held.HoldAsync), dispatchSource.GetRequiredService<TimeProvider>());
        Task<VoidSaleResult> voiding; Task<CashTransferDetails> dispatching;
        var amount = 100m + sale.Checkout.Draft.TotalAmount;
        if (voidFirst)
        {
            voiding = voidHandler.HandleAsync(SaleVoidTestData.Command(sale), TestContext.Current.CancellationToken);
            await held.Acquired.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            dispatching = dispatchHandler.HandleAsync(CashTransferTestData.Command(rows, amount), TestContext.Current.CancellationToken);
        }
        else
        {
            dispatching = dispatchHandler.HandleAsync(CashTransferTestData.Command(rows, amount), TestContext.Current.CancellationToken);
            await held.Acquired.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            voiding = voidHandler.HandleAsync(SaleVoidTestData.Command(sale), TestContext.Current.CancellationToken);
        }
        try { await WaitForLockAsync(marker); } finally { held.Release.TrySetResult(); }
        if (voidFirst)
        {
            await voiding;
            Assert.Equal(CashTransferErrors.InsufficientCash, (await Assert.ThrowsAsync<ApplicationErrorException>(() => dispatching)).Error);
        }
        else
        {
            await dispatching;
            Assert.Equal(SalesPosErrors.InsufficientCash, (await Assert.ThrowsAsync<ApplicationErrorException>(() => voiding)).Error);
        }
        await using var verify = fixture.CreateContext(tenant.TenantId);
        Assert.Equal(voidFirst ? 0 : 1, await verify.CashTransfers.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(voidFirst ? 1 : 0, await verify.SalePaymentReversals.CountAsync(TestContext.Current.CancellationToken));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentDispatchesCannotOverspendAndDoubleReceiptHasOneWinner(bool receipt)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        CashTransferTestData.Rows rows; CashTransferDetails? sent = null;
        await using (var scope = services.CreateAsyncScope())
        {
            rows = await CashTransferTestData.CreateAsync(scope.ServiceProvider, tenant);
            if (receipt) sent = await CashTransferTestData.DispatchAsync(scope.ServiceProvider, rows, 80m);
        }
        var gate = new ArrivalGate();
        async Task<(CashTransferDetails? Result, ApplicationError? Error)> AttemptAsync()
        {
            await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
            CashSessionTestData.Authenticate(source, receipt ? rows.Destination.UserId : rows.Source.UserId);
            var port = new TransferTransaction(source.GetRequiredService<ICashTransferTransaction>(), before: gate.WaitAsync);
            try
            {
                var resolver = source.GetRequiredService<ResolveAccessContextHandler>(); var clock = source.GetRequiredService<TimeProvider>();
                return (receipt ? await new ReceiveCashTransferHandler(resolver, port, clock).HandleAsync(new(tenant.TenantId, sent!.Id, rows.DestinationSessionId), TestContext.Current.CancellationToken)
                    : await new DispatchCashTransferHandler(resolver, port, clock).HandleAsync(CashTransferTestData.Command(rows, 80m), TestContext.Current.CancellationToken), null);
            }
            catch (ApplicationErrorException error) { return (null, error.Error); }
        }
        var results = await Task.WhenAll(AttemptAsync(), AttemptAsync());
        Assert.Single(results, r => r.Result is not null);
        Assert.Equal(receipt ? CashTransferErrors.AlreadyReceived : CashTransferErrors.InsufficientCash, Assert.Single(results, r => r.Error is not null).Error);
        await using var verify = fixture.CreateContext(tenant.TenantId);
        var transfer = await verify.CashTransfers.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(receipt ? CashTransferStatus.Received : CashTransferStatus.InTransit, transfer.Status);
        Assert.Equal(80m, transfer.Amount);
        Assert.Equal(receipt ? 2 : 1, await verify.AuditLogs.CountAsync(a => a.EntityType == AuditEntityType.CashTransfer, TestContext.Current.CancellationToken));
        await using var closing = services.CreateAsyncScope();
        CashSessionTestData.Authenticate(closing.ServiceProvider, rows.Source.UserId);
        var result = await closing.ServiceProvider.GetRequiredService<CloseCashSessionHandler>().HandleAsync(new(tenant.TenantId, rows.Source.BranchId, rows.SourceSessionId, 20m), TestContext.Current.CancellationToken);
        Assert.Equal(20m, result.ExpectedCashAmount);
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CashRowLockProvesBothOrdersOfCloseAgainstDispatchAndReceipt(bool receipt, bool closeFirst)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var winnerServices = IdentityAccessTestSetup.CreateServices(fixture);
        CashTransferTestData.Rows rows; CashTransferDetails? sent = null;
        await using (var scope = winnerServices.CreateAsyncScope())
        {
            rows = await CashTransferTestData.CreateAsync(scope.ServiceProvider, tenant);
            if (receipt) sent = await CashTransferTestData.DispatchAsync(scope.ServiceProvider, rows);
        }
        var marker = "cash-transfer-race-" + Guid.NewGuid().ToString("N");
        await using var loserServices = SaleCheckoutTestData.CreateServices(fixture, marker);
        await using var winnerScope = winnerServices.CreateAsyncScope(); await using var loserScope = loserServices.CreateAsyncScope();
        var actor = receipt ? rows.Destination : rows.Source;
        CashSessionTestData.Authenticate(winnerScope.ServiceProvider, actor.UserId); CashSessionTestData.Authenticate(loserScope.ServiceProvider, actor.UserId);
        var held = new HeldGate();
        var closeSource = closeFirst ? winnerScope.ServiceProvider : loserScope.ServiceProvider;
        var transferSource = closeFirst ? loserScope.ServiceProvider : winnerScope.ServiceProvider;
        var closePort = closeSource.GetRequiredService<ICashCloseTransaction>();
        var close = new CloseCashSessionHandler(closeSource.GetRequiredService<ResolveAccessContextHandler>(),
            closeFirst ? new CloseTransaction(closePort, held) : closePort, closeSource.GetRequiredService<TimeProvider>());
        var transferPort = transferSource.GetRequiredService<ICashTransferTransaction>();
        var port = closeFirst ? transferPort : new TransferTransaction(transferPort, afterCashLock: held.HoldAsync);
        var resolver = transferSource.GetRequiredService<ResolveAccessContextHandler>(); var clock = transferSource.GetRequiredService<TimeProvider>();
        Task<CashTransferDetails> TransferAsync() => receipt
            ? new ReceiveCashTransferHandler(resolver, port, clock).HandleAsync(new(tenant.TenantId, sent!.Id, rows.DestinationSessionId), TestContext.Current.CancellationToken)
            : new DispatchCashTransferHandler(resolver, port, clock).HandleAsync(CashTransferTestData.Command(rows), TestContext.Current.CancellationToken);
        var sessionId = receipt ? rows.DestinationSessionId : rows.SourceSessionId;
        Task<CashSessionReconciliationDetails> CloseAsync() => close.HandleAsync(new(tenant.TenantId, actor.BranchId, sessionId, 0m), TestContext.Current.CancellationToken);
        Task<CashTransferDetails> transferring; Task<CashSessionReconciliationDetails> closing;
        if (closeFirst)
        {
            closing = CloseAsync(); await held.Acquired.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken); transferring = TransferAsync();
        }
        else
        {
            transferring = TransferAsync(); await held.Acquired.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken); closing = CloseAsync();
        }
        try { await WaitForLockAsync(marker); Assert.False((closeFirst ? (Task)transferring : closing).IsCompleted); }
        finally { held.Release.TrySetResult(); }
        var closed = await closing;
        if (closeFirst)
            Assert.Equal(receipt ? CashTransferErrors.DestinationClosed : CashTransferErrors.SourceClosed,
                (await Assert.ThrowsAsync<ApplicationErrorException>(() => transferring)).Error);
        else await transferring;
        Assert.Equal(receipt ? (closeFirst ? 20m : 70m) : (closeFirst ? 100m : 50m), closed.ExpectedCashAmount);
        await using var verify = fixture.CreateContext(tenant.TenantId);
        Assert.Equal(receipt || !closeFirst ? 1 : 0, await verify.CashTransfers.CountAsync(TestContext.Current.CancellationToken));
        if (receipt) Assert.Equal(closeFirst ? CashTransferStatus.InTransit : CashTransferStatus.Received,
            await verify.CashTransfers.Select(t => t.Status).SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await verify.AuditLogs.CountAsync(a => a.Action == AuditAction.CashSessionClosed, TestContext.Current.CancellationToken));
    }
    private async Task WaitForLockAsync(string marker)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await using var monitor = fixture.CreateContext();
        while (await monitor.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE application_name = {marker} AND wait_event_type = 'Lock'")
            .SingleAsync(timeout.Token) == 0) await Task.Delay(10, timeout.Token);
    }
    private sealed class ArrivalGate
    {
        private int _arrivals; private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task WaitAsync(CancellationToken token) { if (Interlocked.Increment(ref _arrivals) == 2) _ready.TrySetResult(); await _ready.Task.WaitAsync(TimeSpan.FromSeconds(30), token); }
    }
    private sealed class HeldGate
    {
        public TaskCompletionSource Acquired { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task HoldAsync(CancellationToken token) { Acquired.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), token); }
    }
    // Only timing is controlled; every lock, ledger read and commit uses the real PostgreSQL adapter.
    private sealed class TransferTransaction(ICashTransferTransaction inner, Func<CancellationToken, Task>? before = null, Func<CancellationToken, Task>? afterCashLock = null) : ICashTransferTransaction
    {
        public async Task<ICashTransferDispatchScope> BeginDispatchAsync(Guid tenant, Guid branch, Guid session, Guid destination, CancellationToken token)
        {
            if (before is not null) await before(token);
            var scope = await inner.BeginDispatchAsync(tenant, branch, session, destination, token);
            try { if (afterCashLock is not null) await afterCashLock(token); return scope; } catch { await scope.DisposeAsync(); throw; }
        }
        public async Task<ICashTransferReceiveScope> BeginReceiveAsync(Guid tenant, Guid transfer, CancellationToken token)
        {
            if (before is not null) await before(token);
            var scope = await inner.BeginReceiveAsync(tenant, transfer, token); return afterCashLock is null ? scope : new ReceiveScope(scope, afterCashLock);
        }
    }
    private sealed class ReceiveScope(ICashTransferReceiveScope inner, Func<CancellationToken, Task> hold) : ICashTransferReceiveScope
    {
        public CashTransfer Transfer => inner.Transfer;
        public async Task<CashSession> LockDestinationAsync(Guid session, CancellationToken token) { var result = await inner.LockDestinationAsync(session, token); await hold(token); return result; }
        public Task CompleteAsync(AuditLog audit, CancellationToken token) => inner.CompleteAsync(audit, token);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    private sealed class CloseTransaction(ICashCloseTransaction inner, HeldGate gate) : ICashCloseTransaction
    {
        public async Task<ICashCloseScope> BeginAsync(Guid tenant, Guid branch, Guid session, CancellationToken token)
        {
            var scope = await inner.BeginAsync(tenant, branch, session, token);
            try { await gate.HoldAsync(token); return scope; } catch { await scope.DisposeAsync(); throw; }
        }
    }
    private sealed class VoidTransaction(ISaleVoidTransaction inner, HeldGate gate) : ISaleVoidTransaction
    {
        public Task<SaleVoidSnapshot?> FindAsync(Guid tenant, Guid branch, Guid id, CancellationToken token) => inner.FindAsync(tenant, branch, id, token);
        public async Task<ISaleVoidScope> BeginAsync(Guid tenant, Guid branch, Guid cash, Guid sale, CancellationToken token)
        {
            var scope = await inner.BeginAsync(tenant, branch, cash, sale, token);
            try { await gate.HoldAsync(token); return scope; } catch { await scope.DisposeAsync(); throw; }
        }
    }
}
