using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.Cash.CloseCashSession;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.ConfirmSale;
using MediPOS.Application.Modules.SalesPos.VoidSale;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.SalesPos;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.Cash;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class CashCloseConcurrencyTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task ConcurrentDoubleCloseHasOneWinnerAndNeverOverwritesItsCountedAmountOrAudit()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        IdentityAccessTestSetup.Setup setup;
        Guid session;
        await using (var scope = services.CreateAsyncScope())
        {
            setup = await CashSessionTestData.CreateAsync(scope.ServiceProvider);
            session = (await CashSessionTestData.OpenAsync(scope.ServiceProvider, setup)).CashSessionId;
        }
        var gate = new ArrivalGate();
        async Task<(CashSessionReconciliationDetails? Result, ApplicationError? Error)> AttemptAsync(decimal counted)
        {
            await using var scope = services.CreateAsyncScope();
            var source = scope.ServiceProvider;
            CashSessionTestData.Authenticate(source, setup.UserId);
            var handler = new CloseCashSessionHandler(source.GetRequiredService<ResolveAccessContextHandler>(),
                new CloseTransaction(source.GetRequiredService<ICashCloseTransaction>(), gate.WaitAsync), source.GetRequiredService<TimeProvider>());
            try { return (await handler.HandleAsync(CashCloseTestData.Command(setup.TenantId, setup.BranchId, session, counted), TestContext.Current.CancellationToken), null); }
            catch (ApplicationErrorException error) { return (null, error.Error); }
        }
        var results = await Task.WhenAll(AttemptAsync(98.1234m), AttemptAsync(123.4567m));
        var winner = Assert.Single(results, r => r.Result is not null).Result!;
        Assert.Equal(CashSessionErrors.AlreadyClosed, Assert.Single(results, r => r.Error is not null).Error);
        await using var verify = fixture.CreateContext(setup.TenantId);
        var cash = await verify.CashSessions.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(winner.CountedCashAmount, cash.CountedCashAmount);
        Assert.Equal(winner.ExpectedCashAmount, cash.ExpectedCashAmount);
        Assert.Equal(winner.CashDifference, cash.CashDifference);
        var audit = Assert.Single(await verify.AuditLogs.Where(a => a.Action == AuditAction.CashSessionClosed).ToListAsync(TestContext.Current.CancellationToken));
        using var after = System.Text.Json.JsonDocument.Parse(audit.AfterJson!);
        Assert.Equal(winner.CountedCashAmount, after.RootElement.GetProperty("countedCashAmount").GetDecimal());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RealCashRowLockSerializesBothOrdersOfConfirmationAndVoidAgainstClose(bool voidSale, bool closeFirst)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var setupServices = IdentityAccessTestSetup.CreateServices(fixture);
        SaleCheckoutTestData.Rows rows;
        SaleVoidTestData.Rows? voidRows = null;
        await using (var scope = setupServices.CreateAsyncScope())
        {
            if (voidSale)
            {
                voidRows = await SaleVoidTestData.CreateAsync(scope.ServiceProvider, tenant);
                rows = voidRows.Checkout;
            }
            else rows = await SaleCheckoutTestData.CreateAsync(scope.ServiceProvider, tenant);
        }
        var marker = "cash-race-" + Guid.NewGuid().ToString("N");
        await using var loserServices = SaleCheckoutTestData.CreateServices(fixture, marker);
        await using var winnerScope = setupServices.CreateAsyncScope();
        await using var loserScope = loserServices.CreateAsyncScope();
        CashSessionTestData.Authenticate(winnerScope.ServiceProvider, tenant.Identity.UserId);
        CashSessionTestData.Authenticate(loserScope.ServiceProvider, tenant.Identity.UserId);
        var gate = new HeldGate();
        var closeSource = closeFirst ? winnerScope.ServiceProvider : loserScope.ServiceProvider;
        var childSource = closeFirst ? loserScope.ServiceProvider : winnerScope.ServiceProvider;
        var closePort = closeSource.GetRequiredService<ICashCloseTransaction>();
        var close = new CloseCashSessionHandler(closeSource.GetRequiredService<ResolveAccessContextHandler>(),
            closeFirst ? new CloseTransaction(closePort, null, gate.HoldAsync) : closePort, closeSource.GetRequiredService<TimeProvider>());
        async Task ChildAsync()
        {
            if (voidSale)
            {
                var port = childSource.GetRequiredService<ISaleVoidTransaction>();
                var handler = new VoidSaleHandler(childSource.GetRequiredService<ResolveAccessContextHandler>(),
                    closeFirst ? port : new VoidTransaction(port, gate), childSource.GetRequiredService<TimeProvider>());
                await handler.HandleAsync(SaleVoidTestData.Command(voidRows!), TestContext.Current.CancellationToken);
            }
            else
            {
                var port = childSource.GetRequiredService<ISaleCheckoutTransaction>();
                var handler = new ConfirmSaleHandler(childSource.GetRequiredService<ResolveAccessContextHandler>(),
                    childSource.GetRequiredService<ISaleDraftStore>(), childSource.GetRequiredService<IFindOpenCashSession>(),
                    closeFirst ? port : new CheckoutTransaction(port, gate), childSource.GetRequiredService<IBusinessProductStore>(),
                    childSource.GetRequiredService<TimeProvider>());
                await handler.HandleAsync(SaleCheckoutTestData.Command(rows), TestContext.Current.CancellationToken);
            }
        }
        Task child;
        Task<CashSessionReconciliationDetails> closing;
        var command = CashCloseTestData.Command(tenant.TenantId, tenant.Identity.BranchId, rows.Cash.CashSessionId);
        if (closeFirst)
        {
            closing = close.HandleAsync(command, TestContext.Current.CancellationToken);
            await gate.Acquired.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            child = ChildAsync();
        }
        else
        {
            child = ChildAsync();
            await gate.Acquired.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            closing = close.HandleAsync(command, TestContext.Current.CancellationToken);
        }
        try
        {
            await WaitForLockAsync(marker);
            Assert.False((closeFirst ? child : closing).IsCompleted);
        }
        finally { gate.Release.TrySetResult(); }
        var result = await closing;
        if (closeFirst)
        {
            var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => child);
            Assert.Equal(voidSale ? SalesPosErrors.CashSessionClosed : SalesPosErrors.CashSessionRequired, error.Error);
        }
        else await child;
        var cashNet = voidSale ? (closeFirst ? 2.125m : 0m) : (closeFirst ? 0m : 2.125m);
        Assert.Equal(new(cashNet, 0m, 0m, 0m, 0m), result.PaymentTotals);
        Assert.Equal(100m + cashNet, result.ExpectedCashAmount);
        await using var verify = fixture.CreateContext(tenant.TenantId);
        var sale = await verify.Sales.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(voidSale ? (closeFirst ? SaleStatus.Confirmed : SaleStatus.Voided) :
            (closeFirst ? SaleStatus.Draft : SaleStatus.Confirmed), sale.Status);
        Assert.Equal(voidSale && !closeFirst ? 1 : 0, await verify.SalePaymentReversals.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(!voidSale && closeFirst ? 0 : 1, await verify.SalePayments.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(!voidSale && closeFirst ? 0 : 1,
            await verify.StockMovements.CountAsync(m => m.MovementType == StockMovementType.Sale, TestContext.Current.CancellationToken));
        Assert.Equal(voidSale && !closeFirst ? 1 : 0,
            await verify.StockMovements.CountAsync(m => m.MovementType == StockMovementType.SaleReversal, TestContext.Current.CancellationToken));
        Assert.Equal(1, await verify.AuditLogs.CountAsync(a => a.Action == AuditAction.CashSessionClosed, TestContext.Current.CancellationToken));
    }

    private async Task WaitForLockAsync(string marker)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await using var monitor = fixture.CreateContext();
        while (await monitor.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE application_name = {marker} AND wait_event_type = 'Lock'")
            .SingleAsync(timeout.Token) == 0) await Task.Delay(10, timeout.Token);
    }

    private sealed class ArrivalGate
    {
        private int _arrivals;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task WaitAsync(CancellationToken token)
        {
            if (Interlocked.Increment(ref _arrivals) == 2) _ready.TrySetResult();
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
        }
    }

    private sealed class HeldGate
    {
        public TaskCompletionSource Acquired { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task HoldAsync(CancellationToken token)
        {
            Acquired.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
        }
    }

    // Decorators control when a real acquired scope is released to the handler. No financial behavior is substituted.
    private sealed class CloseTransaction(ICashCloseTransaction inner, Func<CancellationToken, Task>? before = null,
        Func<CancellationToken, Task>? after = null) : ICashCloseTransaction
    {
        public async Task<ICashCloseScope> BeginAsync(Guid tenantId, Guid branchId, Guid sessionId, CancellationToken cancellationToken)
        {
            if (before is not null) await before(cancellationToken);
            var scope = await inner.BeginAsync(tenantId, branchId, sessionId, cancellationToken);
            try { if (after is not null) await after(cancellationToken); return scope; }
            catch { await scope.DisposeAsync(); throw; }
        }
    }

    private sealed class CheckoutTransaction(ISaleCheckoutTransaction inner, HeldGate gate) : ISaleCheckoutTransaction
    {
        public async Task<ISaleCheckoutScope> BeginAsync(Guid tenantId, Guid branchId, Guid membershipId, Guid cashSessionId, Guid saleId, CancellationToken cancellationToken)
        {
            var scope = await inner.BeginAsync(tenantId, branchId, membershipId, cashSessionId, saleId, cancellationToken);
            try { await gate.HoldAsync(cancellationToken); return scope; }
            catch { await scope.DisposeAsync(); throw; }
        }
    }

    private sealed class VoidTransaction(ISaleVoidTransaction inner, HeldGate gate) : ISaleVoidTransaction
    {
        public Task<SaleVoidSnapshot?> FindAsync(Guid tenantId, Guid branchId, Guid saleId, CancellationToken cancellationToken) =>
            inner.FindAsync(tenantId, branchId, saleId, cancellationToken);
        public async Task<ISaleVoidScope> BeginAsync(Guid tenantId, Guid branchId, Guid cashSessionId, Guid saleId, CancellationToken cancellationToken)
        {
            var scope = await inner.BeginAsync(tenantId, branchId, cashSessionId, saleId, cancellationToken);
            try { await gate.HoldAsync(cancellationToken); return scope; }
            catch { await scope.DisposeAsync(); throw; }
        }
    }
}
