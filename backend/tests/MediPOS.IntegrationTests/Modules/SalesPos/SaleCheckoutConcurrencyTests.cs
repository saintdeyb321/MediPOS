using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.Inventory;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.ConfirmSale;
using MediPOS.Application.Modules.SalesPos.CreateSaleDraft;
using MediPOS.Application.Modules.SalesPos.ReplaceSaleLines;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.SalesPos;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class SaleCheckoutConcurrencyTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task TwoConfirmationsOfOneDraftCommitExactlyOnePaymentMovementAndAudit()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        SaleCheckoutTestData.Rows rows;
        await using (var scope = services.CreateAsyncScope())
            rows = await SaleCheckoutTestData.CreateAsync(scope.ServiceProvider, tenant);
        var barrier = new TwoPartyBarrier();
        var outcomes = await Task.WhenAll(ConfirmAsync(services, rows, barrier, beforeLots: false), ConfirmAsync(services, rows, barrier, beforeLots: false));
        Assert.Single(outcomes, outcome => outcome.Result is not null);
        Assert.Equal(SalesPosErrors.NotDraft, Assert.Single(outcomes, outcome => outcome.Error is not null).Error!.Error);
        await VerifySingleCommitAsync(rows, rows.Draft.SaleId);
        await using var verify = fixture.CreateContext(tenant.TenantId);
        Assert.Equal(2m, await verify.InventoryLots.Where(lot => lot.Id == rows.LotIds[0]).Select(lot => lot.QuantityAvailableBase).SingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LastUnitAcrossDifferentSellersAndCashSessionsHasExactlyOneWinner()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        SaleCheckoutTestData.Rows first;
        SaleCheckoutTestData.Rows second;
        await using (var scope = services.CreateAsyncScope())
        {
            var source = scope.ServiceProvider;
            first = await SaleCheckoutTestData.CreateAsync(source, tenant, receipts: [(1m, null)]);
            var owner = await CashSessionTestData.AddOwnerAsync(source, tenant.Identity);
            var membership = await source.GetRequiredService<MediPosDbContext>().Memberships.Where(value => value.UserId == owner)
                .Select(value => value.Id).SingleAsync(TestContext.Current.CancellationToken);
            var identity = tenant.Identity with { UserId = owner, MembershipId = membership };
            var cash = await CashSessionTestData.OpenAsync(source, identity);
            var draft = await source.GetRequiredService<CreateSaleDraftHandler>().HandleAsync(new(tenant.TenantId, identity.BranchId), TestContext.Current.CancellationToken);
            draft = await source.GetRequiredService<ReplaceSaleLinesHandler>().HandleAsync(new(tenant.TenantId, identity.BranchId, draft.SaleId, draft.Version,
                [new(first.ProductId, first.UnitId, 1m, PriceKind.Retail)]), TestContext.Current.CancellationToken);
            second = first with { Tenant = tenant with { Identity = identity }, Cash = cash, Draft = draft };
        }
        Assert.NotEqual(first.Cash.CashSessionId, second.Cash.CashSessionId);
        var barrier = new TwoPartyBarrier(); // Both own header locks before competing for the same physical lot.
        var outcomes = await Task.WhenAll(ConfirmAsync(services, first, barrier, beforeLots: true), ConfirmAsync(services, second, barrier, beforeLots: true));
        var winner = Assert.Single(outcomes, outcome => outcome.Result is not null).Result!;
        Assert.Equal(InventoryErrors.InsufficientStock, Assert.Single(outcomes, outcome => outcome.Error is not null).Error!.Error);
        await VerifySingleCommitAsync(first, winner.SaleId);
        await using var verify = fixture.CreateContext(tenant.TenantId);
        var loserRows = winner.SaleId == first.Draft.SaleId ? second : first;
        var loser = await verify.Sales.SingleAsync(sale => sale.Id == loserRows.Draft.SaleId, TestContext.Current.CancellationToken);
        Assert.Equal(SaleStatus.Draft, loser.Status);
        Assert.Null(loser.ConfirmedAt);
        Assert.Equal(loserRows.Draft.Version, verify.Entry(loser).Property<uint>("Version").CurrentValue);
        Assert.Equal(0m, await verify.InventoryLots.Where(lot => lot.Id == first.LotIds[0]).Select(lot => lot.QuantityAvailableBase).SingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CashRowLockWaitRechecksClosedStateBeforeAnyConsumption()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var setupServices = IdentityAccessTestSetup.CreateServices(fixture);
        SaleCheckoutTestData.Rows rows;
        await using (var scope = setupServices.CreateAsyncScope())
            rows = await SaleCheckoutTestData.CreateAsync(scope.ServiceProvider, tenant);
        var marker = "checkout-cash-" + Guid.NewGuid().ToString("N");
        await using var services = SaleCheckoutTestData.CreateServices(fixture, marker);
        await using var blocker = setupServices.CreateAsyncScope();
        await using var held = await blocker.ServiceProvider.GetRequiredService<ICashCloseTransaction>().BeginAsync(
            tenant.TenantId, tenant.Identity.BranchId, rows.Cash.CashSessionId, TestContext.Current.CancellationToken);
        await using var scopeToConfirm = services.CreateAsyncScope();
        var pending = SaleCheckoutTestData.ConfirmAsync(scopeToConfirm.ServiceProvider, rows);
        try
        {
            await WaitForDatabaseLockAsync(marker);
            Assert.False(pending.IsCompleted);
            await CashCloseTestData.CompleteHeldAsync(held, tenant.Identity.UserId);
        }
        finally { await held.DisposeAsync(); }
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => pending);
        Assert.Equal(SalesPosErrors.CashSessionRequired, error.Error);
        await using var verify = fixture.CreateContext(tenant.TenantId);
        Assert.Equal(SaleStatus.Draft, await verify.Sales.Where(sale => sale.Id == rows.Draft.SaleId).Select(sale => sale.Status).SingleAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await verify.SalePayments.ToListAsync(TestContext.Current.CancellationToken));
        Assert.False(await verify.StockMovements.AnyAsync(movement => movement.MovementType == StockMovementType.Sale, TestContext.Current.CancellationToken));
        Assert.False(await verify.AuditLogs.AnyAsync(audit => audit.Action == AuditAction.SaleConfirmed, TestContext.Current.CancellationToken));
        Assert.Equal(3m, await verify.InventoryLots.Where(lot => lot.Id == rows.LotIds[0]).Select(lot => lot.QuantityAvailableBase).SingleAsync(TestContext.Current.CancellationToken));
    }

    private async Task WaitForDatabaseLockAsync(string marker)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await using var monitor = fixture.CreateContext();
        while (await monitor.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE application_name = {marker} AND wait_event_type = 'Lock'")
            .SingleAsync(timeout.Token) == 0)
            await Task.Delay(10, timeout.Token);
    }

    [Fact]
    public async Task CheckoutDoesNotLockLicenseTenantOrUnusedLaterLot()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var rows = await SaleCheckoutTestData.CreateAsync(scope.ServiceProvider, tenant, receipts: [(1m, null), (10m, null)]);
        await using var blocker = fixture.CreateConstraintContext(tenant.TenantId);
        var unused = await blocker.InventoryLots.Where(lot => lot.BusinessProductId == rows.ProductId)
            .OrderBy(lot => lot.CreatedAt).ThenBy(lot => lot.Id).Select(lot => lot.Id).LastAsync(TestContext.Current.CancellationToken);
        await using var held = await blocker.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        // NO KEY UPDATE allows the audit FK's KEY SHARE check, but conflicts with a tenant/license mutation mutex.
        await blocker.Database.SqlQuery<Guid>($"SELECT tenant_id AS \"Value\" FROM tenants WHERE tenant_id = {tenant.TenantId} FOR NO KEY UPDATE").SingleAsync(TestContext.Current.CancellationToken);
        await blocker.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM licenses WHERE id = {tenant.Identity.LicenseId} FOR NO KEY UPDATE").SingleAsync(TestContext.Current.CancellationToken);
        await blocker.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM inventory_lots WHERE id = {unused} FOR UPDATE").SingleAsync(TestContext.Current.CancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        CashSessionTestData.Authenticate(scope.ServiceProvider, tenant.Identity.UserId);
        var result = await scope.ServiceProvider.GetRequiredService<ConfirmSaleHandler>().HandleAsync(SaleCheckoutTestData.Command(rows), timeout.Token);
        Assert.Equal(rows.Draft.SaleId, result.SaleId);
        await using var verify = fixture.CreateContext(tenant.TenantId);
        Assert.False(await verify.StockMovements.AnyAsync(movement => movement.InventoryLotId == unused && movement.MovementType == StockMovementType.Sale, TestContext.Current.CancellationToken));
    }

    private async Task VerifySingleCommitAsync(SaleCheckoutTestData.Rows rows, Guid winnerId)
    {
        await using var verify = fixture.CreateContext(rows.Tenant.TenantId);
        var sale = Assert.Single(await verify.Sales.Where(sale => sale.Status == SaleStatus.Confirmed).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(winnerId, sale.Id);
        Assert.NotNull(sale.ConfirmedAt);
        Assert.Equal(winnerId, Assert.Single(await verify.SalePayments.ToListAsync(TestContext.Current.CancellationToken)).SaleId);
        Assert.Single(await verify.StockMovements.Where(movement => movement.MovementType == StockMovementType.Sale).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(winnerId, Assert.Single(await verify.AuditLogs.Where(audit => audit.Action == AuditAction.SaleConfirmed).ToListAsync(TestContext.Current.CancellationToken)).EntityId);
        var lot = await verify.InventoryLots.SingleAsync(lot => lot.Id == rows.LotIds[0], TestContext.Current.CancellationToken);
        Assert.Equal(lot.QuantityAvailableBase, await verify.StockMovements.Where(movement => movement.InventoryLotId == lot.Id).SumAsync(movement => movement.QuantityDeltaBase, TestContext.Current.CancellationToken));
    }

    private static async Task<Outcome> ConfirmAsync(ServiceProvider services, SaleCheckoutTestData.Rows rows, TwoPartyBarrier barrier, bool beforeLots)
    {
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        CashSessionTestData.Authenticate(source, rows.Tenant.Identity.UserId);
        var handler = new ConfirmSaleHandler(source.GetRequiredService<ResolveAccessContextHandler>(), source.GetRequiredService<ISaleDraftStore>(),
            source.GetRequiredService<IFindOpenCashSession>(), new GatedTransaction(source.GetRequiredService<ISaleCheckoutTransaction>(), barrier, beforeLots),
            source.GetRequiredService<IBusinessProductStore>(), source.GetRequiredService<TimeProvider>());
        try { return new(await handler.HandleAsync(SaleCheckoutTestData.Command(rows), TestContext.Current.CancellationToken), null); }
        catch (ApplicationErrorException error) { return new(null, error); }
    }

    private sealed record Outcome(ConfirmSaleResult? Result, ApplicationErrorException? Error);

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

    // Only scheduling is controlled; transactions, locks and persistence always delegate to real PostgreSQL.
    private sealed class GatedTransaction(ISaleCheckoutTransaction inner, TwoPartyBarrier barrier, bool beforeLots) : ISaleCheckoutTransaction
    {
        public async Task<ISaleCheckoutScope> BeginAsync(Guid tenantId, Guid branchId, Guid membershipId, Guid cashSessionId, Guid saleId, CancellationToken cancellationToken)
        {
            if (!beforeLots) await barrier.ArriveAsync(cancellationToken);
            return new GatedScope(await inner.BeginAsync(tenantId, branchId, membershipId, cashSessionId, saleId, cancellationToken), barrier, beforeLots);
        }
    }

    private sealed class GatedScope(ISaleCheckoutScope inner, TwoPartyBarrier barrier, bool beforeLots) : ISaleCheckoutScope
    {
        public CashSession CashSession => inner.CashSession;
        public Sale Sale => inner.Sale;
        public uint Version => inner.Version;
        public async Task<IReadOnlyList<InventoryLot>> LockLotsAsync(Guid productId, ProductType productType, decimal requested, DateOnly today, CancellationToken cancellationToken)
        {
            if (beforeLots) await barrier.ArriveAsync(cancellationToken);
            return await inner.LockLotsAsync(productId, productType, requested, today, cancellationToken);
        }
        public Task<uint> CompleteAsync(IReadOnlyList<SalePayment> payments, IReadOnlyList<StockMovement> movements, AuditLog audit, CancellationToken cancellationToken) =>
            inner.CompleteAsync(payments, movements, audit, cancellationToken);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
