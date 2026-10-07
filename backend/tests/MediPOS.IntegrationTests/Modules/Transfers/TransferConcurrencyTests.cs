using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.Transfers;
using MediPOS.Application.Modules.Transfers.ApproveTransfer;
using MediPOS.Application.Modules.Transfers.DispatchTransfer;
using MediPOS.Application.Modules.Transfers.ReceiveTransfer;
using MediPOS.Application.Modules.Transfers.RequestTransfer;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.Transfers;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.Transfers;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class TransferConcurrencyTests(PostgreSqlFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentDoubleDispatchOrReceiptHasOneWinnerAndNoDuplicateEffects(bool receipt)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        TransferTestData.Rows rows;
        TransferDetails? dispatched = null;
        await using (var scope = services.CreateAsyncScope())
        {
            rows = await TransferTestData.CreateAsync(scope.ServiceProvider, tenant);
            await TransferTestData.ApproveAsync(scope.ServiceProvider, rows);
            if (receipt) dispatched = await TransferTestData.DispatchAsync(scope.ServiceProvider, rows);
        }
        var barrier = new TwoPartyBarrier();
        var input = new[] { new TransferDispatchSelection(rows.Requested.Lines[0].Id, rows.SourceLotIds[0], 2m),
            new TransferDispatchSelection(rows.Requested.Lines[0].Id, rows.SourceLotIds[1], 3m) };
        var outcomes = await Task.WhenAll(AttemptAsync(services, rows, receipt, input, dispatched, barrier),
            AttemptAsync(services, rows, receipt, input, dispatched, barrier));
        Assert.Single(outcomes, o => o.Result is not null);
        Assert.Equal(receipt ? TransferErrors.AlreadyReceived : TransferErrors.AlreadyDispatched, Assert.Single(outcomes, o => o.Error is not null).Error);
        await using var verify = fixture.CreateContext(tenant.TenantId);
        Assert.Equal(2, await verify.TransferLotAllocations.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, await verify.StockMovements.CountAsync(m => m.MovementType == StockMovementType.TransferDispatch, TestContext.Current.CancellationToken));
        Assert.Equal(receipt ? 2 : 0, await verify.StockMovements.CountAsync(m => m.MovementType == StockMovementType.TransferReceipt, TestContext.Current.CancellationToken));
        Assert.Equal(receipt ? 4 : 3, await verify.TransferEvents.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(receipt ? 4 : 3, await verify.AuditLogs.CountAsync(a => a.EntityType == MediPOS.Domain.Modules.AuditSupport.AuditEntityType.Transfer, TestContext.Current.CancellationToken));
        Assert.Equal(receipt ? 5m : 0m, await verify.InventoryLots.SumAsync(l => l.QuantityAvailableBase, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DifferentTransfersCompetingForLastStockCannotBothDispatchOrMakeTheSourceNegative()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        TransferTestData.Rows first;
        TransferTestData.Rows second;
        await using (var scope = services.CreateAsyncScope())
        {
            var source = scope.ServiceProvider;
            first = await TransferTestData.CreateAsync(source, tenant, 1m, [(1m, new(2027, 1, 1))]);
            await TransferTestData.ApproveAsync(source, first);
            var request = await source.GetRequiredService<RequestTransferHandler>().HandleAsync(new(tenant.TenantId,
                tenant.Identity.BranchId, tenant.SpareBranchId, [new(tenant.BusinessProductId, first.UnitId, 1m)]), TestContext.Current.CancellationToken);
            second = first with { Requested = request };
            await source.GetRequiredService<ApproveTransferHandler>().HandleAsync(new(tenant.TenantId, request.Id), TestContext.Current.CancellationToken);
        }
        var gate = new TwoPartyBarrier();
        var outcomes = await Task.WhenAll(
            AttemptAsync(services, first, false, [new(first.Requested.Lines[0].Id, first.SourceLotIds[0], 1m)], null, gate),
            AttemptAsync(services, second, false, [new(second.Requested.Lines[0].Id, second.SourceLotIds[0], 1m)], null, gate));
        Assert.Single(outcomes, o => o.Result is not null);
        Assert.Equal(TransferErrors.InsufficientStock, Assert.Single(outcomes, o => o.Error is not null).Error);
        await using var verify = fixture.CreateContext(tenant.TenantId);
        Assert.Equal(0m, await verify.InventoryLots.Select(l => l.QuantityAvailableBase).SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await verify.TransferLotAllocations.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await verify.StockMovements.CountAsync(m => m.MovementType == StockMovementType.TransferDispatch, TestContext.Current.CancellationToken));
        Assert.Equal(1, await verify.Transfers.CountAsync(t => t.Status == TransferStatus.InTransit, TestContext.Current.CancellationToken));
        Assert.Equal(1, await verify.Transfers.CountAsync(t => t.Status == TransferStatus.Approved, TestContext.Current.CancellationToken));
    }

    private static async Task<Outcome> AttemptAsync(ServiceProvider services, TransferTestData.Rows rows, bool receipt,
        IReadOnlyList<TransferDispatchSelection> selections, TransferDetails? dispatched, TwoPartyBarrier gate)
    {
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        CashSessionTestData.Authenticate(source, rows.OwnerId);
        var transaction = new GatedTransaction(source.GetRequiredService<ITransferTransaction>(), gate);
        try
        {
            var resolver = source.GetRequiredService<ResolveAccessContextHandler>();
            var reader = source.GetRequiredService<ITransferReader>();
            var clock = source.GetRequiredService<TimeProvider>();
            var result = receipt
                ? await new ReceiveTransferHandler(resolver, reader, transaction, clock).HandleAsync(new(rows.Tenant.TenantId, rows.Requested.Id,
                    dispatched!.Allocations.Select(a => new TransferReceiptSelection(a.Id, a.DispatchedQuantityBase)).ToArray()), TestContext.Current.CancellationToken)
                : await new DispatchTransferHandler(resolver, reader, transaction, clock).HandleAsync(new(rows.Tenant.TenantId, rows.Requested.Id, selections), TestContext.Current.CancellationToken);
            return new(result, null);
        }
        catch (ApplicationErrorException error) { return new(null, error.Error); }
    }
    private sealed record Outcome(TransferDetails? Result, ApplicationError? Error);
    private sealed class TwoPartyBarrier
    {
        private int _arrivals;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task WaitAsync(CancellationToken token)
        {
            if (Interlocked.Increment(ref _arrivals) == 2) _ready.TrySetResult();
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
        }
    }
    private sealed class GatedTransaction(ITransferTransaction inner, TwoPartyBarrier gate) : ITransferTransaction
    {
        public async Task<ITransferScope> BeginAsync(Guid tenantId, Guid transferId, CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);
            return await inner.BeginAsync(tenantId, transferId, cancellationToken);
        }
    }
}
