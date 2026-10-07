using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.VoidSale;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.SalesPos;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class SaleVoidConcurrencyTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task ConcurrentDoubleVoidHasOneWinnerAndExactlyOneCounterpartPerEffect()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        SaleVoidTestData.Rows rows;
        await using (var scope = services.CreateAsyncScope()) rows = await SaleVoidTestData.CreateAsync(scope.ServiceProvider, tenant, highRisk: true);
        var gate = new TwoPartyBarrier();
        var outcomes = await Task.WhenAll(VoidAsync(services, rows, gate), VoidAsync(services, rows, gate));
        Assert.Single(outcomes, outcome => outcome.Result is not null);
        Assert.Equal(SalesPosErrors.AlreadyVoided, Assert.Single(outcomes, outcome => outcome.Error is not null).Error!.Error);
        await using var verify = fixture.CreateContext(tenant.TenantId);
        Assert.Equal(3, await verify.SalePaymentReversals.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(3, await verify.StockMovements.CountAsync(movement => movement.MovementType == StockMovementType.SaleReversal, TestContext.Current.CancellationToken));
        Assert.Equal(1, await verify.AuditLogs.CountAsync(audit => audit.Action == AuditAction.SaleVoided, TestContext.Current.CancellationToken));
        Assert.Equal(1, await verify.AuditLogs.CountAsync(audit => audit.Action == AuditAction.SaleConfirmed, TestContext.Current.CancellationToken));
        foreach (var lot in await verify.InventoryLots.ToListAsync(TestContext.Current.CancellationToken)) Assert.Equal(rows.BalancesBeforeSale[lot.Id], lot.QuantityAvailableBase);
    }

    [Fact]
    public async Task BusyLaterOriginalLotFailsWithoutWaitCycleRollsBackAndReleasesEarlierLot()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var rows = await SaleVoidTestData.CreateAsync(scope.ServiceProvider, tenant, highRisk: true);
        var ids = rows.Movements.Select(movement => movement.InventoryLotId).Distinct().Order().ToArray();
        await using var blocker = fixture.CreateContext(tenant.TenantId);
        await using (var held = await blocker.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await blocker.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM inventory_lots WHERE id = {ids[^1]} FOR UPDATE").SingleAsync(TestContext.Current.CancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            CashSessionTestData.Authenticate(scope.ServiceProvider, tenant.Identity.UserId);
            var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => scope.ServiceProvider.GetRequiredService<VoidSaleHandler>().HandleAsync(SaleVoidTestData.Command(rows), timeout.Token));
            Assert.Equal(SalesPosErrors.ConcurrentEdit, error.Error);
            await SaleVoidTestData.AssertStillConfirmedAsync(fixture, rows);
            await using var probe = fixture.CreateContext(tenant.TenantId);
            await using var probeTransaction = await probe.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            Assert.Equal(ids[0], await probe.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM inventory_lots WHERE id = {ids[0]} FOR UPDATE NOWAIT").SingleAsync(TestContext.Current.CancellationToken));
        }
        await SaleVoidTestData.VoidAsync(scope.ServiceProvider, rows);
    }

    [Fact]
    public async Task ClosedOriginalCashIsRecheckedAfterItsLockWaitEvenForOwner()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var setup = IdentityAccessTestSetup.CreateServices(fixture);
        SaleVoidTestData.Rows rows;
        Guid owner;
        await using (var scope = setup.CreateAsyncScope())
        {
            rows = await SaleVoidTestData.CreateAsync(scope.ServiceProvider, tenant);
            owner = await CashSessionTestData.AddOwnerAsync(scope.ServiceProvider, tenant.Identity);
        }
        var marker = "void-cash-" + Guid.NewGuid().ToString("N");
        await using var services = SaleCheckoutTestData.CreateServices(fixture, marker);
        await using var scopeToVoid = services.CreateAsyncScope();
        CashSessionTestData.Authenticate(scopeToVoid.ServiceProvider, owner);
        await using var blocker = fixture.CreateContext(tenant.TenantId);
        await using var held = await blocker.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await blocker.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM cash_sessions WHERE id = {rows.Checkout.Cash.CashSessionId} FOR UPDATE").SingleAsync(TestContext.Current.CancellationToken);
        var pending = scopeToVoid.ServiceProvider.GetRequiredService<VoidSaleHandler>().HandleAsync(SaleVoidTestData.Command(rows), TestContext.Current.CancellationToken);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await using var monitor = fixture.CreateContext();
            while (await monitor.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE application_name = {marker} AND wait_event_type = 'Lock'").SingleAsync(timeout.Token) == 0)
                await Task.Delay(10, timeout.Token);
            Assert.False(pending.IsCompleted);
            // Test state represents a future close operation owning this same cash row.
            await blocker.Database.ExecuteSqlInterpolatedAsync($"UPDATE cash_sessions SET status = 'closed' WHERE id = {rows.Checkout.Cash.CashSessionId}", TestContext.Current.CancellationToken);
        }
        finally { await held.CommitAsync(TestContext.Current.CancellationToken); }
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => pending);
        Assert.Equal(SalesPosErrors.CashSessionClosed, error.Error);
        await SaleVoidTestData.AssertStillConfirmedAsync(fixture, rows);
    }

    private static async Task<Outcome> VoidAsync(ServiceProvider services, SaleVoidTestData.Rows rows, TwoPartyBarrier gate)
    {
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        CashSessionTestData.Authenticate(source, rows.Checkout.Tenant.Identity.UserId);
        var handler = new VoidSaleHandler(source.GetRequiredService<ResolveAccessContextHandler>(), new GatedTransaction(source.GetRequiredService<ISaleVoidTransaction>(), gate), source.GetRequiredService<TimeProvider>());
        try { return new(await handler.HandleAsync(SaleVoidTestData.Command(rows), TestContext.Current.CancellationToken), null); }
        catch (ApplicationErrorException error) { return new(null, error); }
    }
    private sealed record Outcome(VoidSaleResult? Result, ApplicationErrorException? Error);
    private sealed class TwoPartyBarrier
    {
        private int _arrivals;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task ArriveAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrivals) == 2) _ready.TrySetResult();
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
    }
    // Only the schedule is controlled; all transaction and lock operations use the real implementation.
    private sealed class GatedTransaction(ISaleVoidTransaction inner, TwoPartyBarrier gate) : ISaleVoidTransaction
    {
        public Task<SaleVoidSnapshot?> FindAsync(Guid tenantId, Guid branchId, Guid saleId, CancellationToken cancellationToken) => inner.FindAsync(tenantId, branchId, saleId, cancellationToken);
        public async Task<ISaleVoidScope> BeginAsync(Guid tenantId, Guid branchId, Guid cashSessionId, Guid saleId, CancellationToken cancellationToken)
        {
            await gate.ArriveAsync(cancellationToken);
            return await inner.BeginAsync(tenantId, branchId, cashSessionId, saleId, cancellationToken);
        }
    }
}
