using System.Data.Common;
using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Cash.CloseCashSession;
using MediPOS.Application.Modules.OfflineSync;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.Reporting;
using MediPOS.IntegrationTests.Modules.SalesPos;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static MediPOS.IntegrationTests.Modules.OfflineSync.OfflineSalePostingTestData;

namespace MediPOS.IntegrationTests.Modules.OfflineSync;

// Prepared for an external PostgreSQL 18 environment. Never run on the local machine.
[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class OfflineSalePostingTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task CompleteSaleMixedPaymentsExactCommissionFefoDatesAndLostResponseReplay()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture);
        var clock = new OwnerOverviewTestData.Clock();
        await using var services = OfflineSyncBatchTestData.CreateServices(fixture, clock: clock, processSales: true);
        Rows rows; SyncEventEnvelope envelope;
        await using (var seed = services.CreateAsyncScope())
        {
            rows = await CreateAsync(seed.ServiceProvider, pair.A, lots: [(1m, SaleCheckoutTestData.Today.AddDays(3)), (1m, SaleCheckoutTestData.Today)], commission: true);
            envelope = Envelope(rows, payload: Payload(rows) with { Payments = [new("cash", 1m), new("yape", 1.125m)] });
            clock.Now = clock.Now.AddHours(1);
            _ = await Run(seed.ServiceProvider, rows, envelope); // Discard committed response.
        }
        await using var restart = services.CreateAsyncScope(); var replay = Assert.Single((await Run(restart.ServiceProvider, rows, envelope)).Results);
        Assert.True(replay.IsReplay); Assert.Equal("accepted", replay.Status);
        await using var context = fixture.CreateContext(rows.Tenant.TenantId);
        var receipt = await context.SyncEvents.SingleAsync(Token); var sale = await context.Sales.Include(value => value.Lines).SingleAsync(value => value.OfflineSyncEventId == receipt.Id, Token);
        Assert.Equal(sale.Id, receipt.ResultReferenceId); Assert.Equal(SaleStatus.Confirmed, sale.Status); Assert.Equal(2.125m, sale.TotalAmount);
        Assert.Equal(clock.Now, sale.ConfirmedAt); Assert.Equal(envelope.ClientOccurredAtUtc, receipt.ClientOccurredAtUtc); Assert.Equal(clock.Now, receipt.ProcessedAtUtc);
        Assert.Single(sale.Lines); Assert.Equal(2.125m, await context.SalePayments.Where(value => value.SaleId == sale.Id).SumAsync(value => value.Amount, Token));
        Assert.Equal(2, await context.SalePayments.CountAsync(value => value.SaleId == sale.Id, Token));
        var movement = await context.StockMovements.SingleAsync(value => value.MovementType == StockMovementType.Sale, Token);
        Assert.Equal(rows.Checkout.LotIds[0], movement.InventoryLotId); Assert.Equal(-1m, movement.QuantityDeltaBase);
        Assert.Equal(.2125m, (await context.CommissionEntries.SingleAsync(value => value.SaleId == sale.Id, Token)).Amount);
        var audit = await context.AuditLogs.SingleAsync(value => value.EntityId == sale.Id && value.Action == AuditAction.SaleConfirmed, Token);
        using var json = JsonDocument.Parse(audit.AfterJson!); Assert.Equal(receipt.Id, json.RootElement.GetProperty("offlineOrigin").GetProperty("SyncEventId").GetGuid());
        Assert.Empty(await context.OfflineSaleConflicts.ToArrayAsync(Token)); Assert.False(context.Database.HasPendingModelChanges());
        Assert.All(await context.InventoryLots.ToArrayAsync(Token), lot => Assert.True(lot.QuantityAvailableBase >= 0));
    }

    [Fact]
    public async Task TwoDevicesContendForFinalMedicineThenOwnerReviewsWithoutDuplicateSalesOrConflicts()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = OfflineSyncBatchTestData.CreateServices(fixture, processSales: true); Rows rows;
        await using (var seed = services.CreateAsyncScope()) rows = await CreateAsync(seed.ServiceProvider, pair.A);
        var first = Envelope(rows); var second = Envelope(rows, rows.SecondDevice);
        var results = await Task.WhenAll(Submit(first), Submit(second));
        var accepted = Assert.Single(results, result => result.Status == "accepted"); var conflict = Assert.Single(results, result => result.Status == "conflict");
        Assert.Equal("sync.sale_insufficient_stock", conflict.ResultCode);
        await Submit(first); await Submit(second);
        await using var verify = fixture.CreateContext(pair.A.TenantId);
        Assert.Equal(1, await verify.Sales.CountAsync(value => value.OfflineSyncEventId != null, Token)); Assert.Equal(1, await verify.SalePayments.CountAsync(Token));
        Assert.Equal(1, await verify.StockMovements.CountAsync(value => value.MovementType == StockMovementType.Sale, Token));
        Assert.Equal(0m, await verify.InventoryLots.SumAsync(value => value.QuantityAvailableBase, Token));
        Assert.Equal(accepted.ResultReferenceId, (await verify.SyncEvents.SingleAsync(value => value.ProcessingStatus == "accepted", Token)).ResultReferenceId);
        await using var owner = services.CreateAsyncScope(); CashSessionTestData.Authenticate(owner.ServiceProvider, rows.Owner);
        var page = await owner.ServiceProvider.GetRequiredService<GetOfflineSaleConflictsHandler>().HandleAsync(new(pair.A.TenantId, new()), Token);
        var caseDetails = Assert.Single(page.Items); Assert.Equal(conflict.ResultReferenceId, caseDetails.Id); Assert.Single(caseDetails.Lines);
        var resolve = owner.ServiceProvider.GetRequiredService<ResolveOfflineSaleConflictHandler>(); var command = new ResolveOfflineSaleConflictCommand(pair.A.TenantId, caseDetails.Id, "reviewed_no_posting", "Revisado sin contabilizar");
        await resolve.HandleAsync(command, Token); await resolve.HandleAsync(command, Token);
        Assert.Equal(1, await verify.AuditLogs.CountAsync(value => value.EntityId == caseDetails.Id && value.Action == AuditAction.OfflineSaleConflictResolved, Token));
        Assert.Equal("conflict", (await verify.SyncEvents.SingleAsync(value => value.ResultReferenceId == caseDetails.Id, Token)).ProcessingStatus);
        await using var cashier = services.CreateAsyncScope(); CashSessionTestData.Authenticate(cashier.ServiceProvider, pair.A.Identity.UserId);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => cashier.ServiceProvider.GetRequiredService<GetOfflineSaleConflictsHandler>()
            .HandleAsync(new(pair.A.TenantId, new(BranchId: pair.A.SpareBranchId)), Token));
        await using var other = services.CreateAsyncScope(); CashSessionTestData.Authenticate(other.ServiceProvider, pair.B.Identity.UserId);
        Assert.Empty((await other.ServiceProvider.GetRequiredService<GetOfflineSaleConflictsHandler>().HandleAsync(new(pair.B.TenantId, new(pair.B.Identity.BranchId)), Token)).Items);
        async Task<OfflineSyncEventResult> Submit(SyncEventEnvelope input)
        { await using var scope = services.CreateAsyncScope(); return Assert.Single((await Run(scope.ServiceProvider, rows, input)).Results); }
    }

    [Fact]
    public async Task OnlineCheckoutAndOfflineSaleUseTheSameStockLockPolicy()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OfflineSyncBatchTestData.CreateServices(fixture, processSales: true);
        Rows rows; await using (var seed = services.CreateAsyncScope()) rows = await CreateAsync(seed.ServiceProvider, pair.A);
        await using var online = services.CreateAsyncScope(); await using var offline = services.CreateAsyncScope();
        var onlineTask = ConfirmOnline(); var offlineTask = Run(offline.ServiceProvider, rows, Envelope(rows));
        var onlineAccepted = await onlineTask; var offlineAccepted = (await offlineTask).AcceptedCount == 1;
        Assert.NotEqual(onlineAccepted, offlineAccepted);
        await using var verify = fixture.CreateContext(pair.A.TenantId);
        Assert.Equal(1, await verify.Sales.CountAsync(value => value.Status == SaleStatus.Confirmed, Token));
        Assert.Equal(1, await verify.SalePayments.CountAsync(Token)); Assert.Equal(0m, await verify.InventoryLots.SumAsync(value => value.QuantityAvailableBase, Token));
        async Task<bool> ConfirmOnline()
        {
            try { await SaleCheckoutTestData.ConfirmAsync(online.ServiceProvider, rows.Checkout); return true; }
            catch (ApplicationErrorException error) when (error.Error.Code == "inventory.insufficient_stock") { return false; }
        }
    }

    [Theory]
    [InlineData("stock_movements")]
    [InlineData("commission_entries")]
    [InlineData("audit_logs")]
    [InlineData("sync_events")]
    public async Task FailureOfAnyFinancialWriteOrTerminalTransitionRollsBackEverythingAndPendingCanRecover(string table)
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); var failure = new FailWrite(table);
        await using var services = OfflineSyncBatchTestData.CreateServices(fixture, failure, processSales: true);
        Rows rows; SyncEventEnvelope envelope; Guid receiptId;
        await using (var seed = services.CreateAsyncScope())
        {
            rows = await CreateAsync(seed.ServiceProvider, pair.A, commission: true); envelope = Envelope(rows);
            CashSessionTestData.Authenticate(seed.ServiceProvider, pair.A.Identity.UserId);
            await using var reserved = await seed.ServiceProvider.GetRequiredService<ReserveSyncEventReceiptHandler>().HandleAsync(new(pair.A.TenantId, envelope), Token);
            receiptId = reserved.Receipt.Id; await reserved.CompleteAsync(null, null, null, Token);
        }
        failure.Enabled = true;
        await using (var attempt = services.CreateAsyncScope()) await Assert.ThrowsAsync<DbUpdateException>(() => Run(attempt.ServiceProvider, rows, envelope));
        await using (var verify = fixture.CreateContext(pair.A.TenantId))
        {
            Assert.Equal("pending", (await verify.SyncEvents.SingleAsync(Token)).ProcessingStatus);
            Assert.Equal(0, await verify.Sales.CountAsync(value => value.OfflineSyncEventId != null, Token)); Assert.Equal(0, await verify.SalePayments.CountAsync(Token));
            Assert.Equal(0, await verify.StockMovements.CountAsync(value => value.MovementType == StockMovementType.Sale, Token));
            Assert.Equal(0, await verify.CommissionEntries.CountAsync(Token)); Assert.Equal(0, await verify.AuditLogs.CountAsync(value => value.Action == AuditAction.SaleConfirmed, Token));
            Assert.Equal(1m, await verify.InventoryLots.SumAsync(value => value.QuantityAvailableBase, Token));
        }
        failure.Enabled = false; await using var recovery = services.CreateAsyncScope(); var result = Assert.Single((await Run(recovery.ServiceProvider, rows, envelope)).Results);
        Assert.Equal(receiptId, result.ReceiptId); Assert.Equal("accepted", result.Status); Assert.True(result.IsReplay);
    }

    [Fact]
    public async Task ClosedOriginalCashCreatesConflictEvenWhenAnotherCashIsOpen()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OfflineSyncBatchTestData.CreateServices(fixture, processSales: true); await using var seed = services.CreateAsyncScope();
        var rows = await CreateAsync(seed.ServiceProvider, pair.A); var envelope = Envelope(rows);
        CashSessionTestData.Authenticate(seed.ServiceProvider, pair.A.Identity.UserId);
        await seed.ServiceProvider.GetRequiredService<CloseCashSessionHandler>().HandleAsync(new(pair.A.TenantId, pair.A.Identity.BranchId, rows.Checkout.Cash.CashSessionId, rows.Checkout.Cash.OpeningAmount), Token);
        var newCash = await CashSessionTestData.OpenAsync(seed.ServiceProvider, pair.A.Identity);
        Assert.NotEqual(newCash.CashSessionId, rows.Checkout.Cash.CashSessionId);
        Assert.Equal("sync.sale_cash_session_unavailable", Assert.Single((await Run(seed.ServiceProvider, rows, envelope)).Results).ResultCode);
        await using var verify = fixture.CreateContext(pair.A.TenantId); Assert.Equal(0, await verify.SalePayments.CountAsync(Token)); Assert.Single(await verify.OfflineSaleConflicts.ToArrayAsync(Token));
    }

    [Fact]
    public async Task ExpiredMedicationProducesPersistentConflictWithoutTouchingStock()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); var clock = new OwnerOverviewTestData.Clock(); await using var services = OfflineSyncBatchTestData.CreateServices(fixture, clock: clock, processSales: true); await using var seed = services.CreateAsyncScope();
        var rows = await CreateAsync(seed.ServiceProvider, pair.A); var envelope = Envelope(rows);
        // Wednesday expires the medicine; use Owner's own device/cash to keep current authorization valid on that date.
        var ownerRows = pair.A with { Identity = pair.A.Identity with { UserId = rows.Owner } };
        CashSessionTestData.Authenticate(seed.ServiceProvider, rows.Owner);
        var device = (await seed.ServiceProvider.GetRequiredService<RegisterSyncDeviceHandler>().HandleAsync(new(pair.A.TenantId, pair.A.Identity.BranchId, "OWNER"), Token)).DeviceId;
        var cash = await CashSessionTestData.OpenAsync(seed.ServiceProvider, ownerRows.Identity);
        clock.Now = clock.Now.AddDays(1);
        var ownerEnvelope = Envelope(rows, device, payload: Payload(rows) with { CashSessionId = cash.CashSessionId });
        var result = await OfflineSyncBatchTestData.Run(seed.ServiceProvider, ownerRows, device, [ownerEnvelope]);
        Assert.Equal("sync.sale_expired_stock", Assert.Single(result.Results).ResultCode);
        await using var verify = fixture.CreateContext(pair.A.TenantId); Assert.Equal(1m, await verify.InventoryLots.SumAsync(value => value.QuantityAvailableBase, Token)); Assert.Empty(await verify.SalePayments.ToArrayAsync(Token));
    }

    [Fact]
    public async Task LostAcknowledgmentAfterActualCommitReplaysEveryFinancialRecordWithoutPostingAgain()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); var loss = new LostAcknowledgment();
        await using var services = OfflineSyncBatchTestData.CreateServices(fixture, loss, processSales: true);
        Rows rows; SyncEventEnvelope envelope;
        await using (var seed = services.CreateAsyncScope()) { rows = await CreateAsync(seed.ServiceProvider, pair.A, commission: true); envelope = Envelope(rows); }
        loss.Enabled = true;
        await using (var attempt = services.CreateAsyncScope()) await Assert.ThrowsAsync<IOException>(() => Run(attempt.ServiceProvider, rows, envelope));
        loss.Enabled = false;
        await using var recovery = services.CreateAsyncScope(); var result = Assert.Single((await Run(recovery.ServiceProvider, rows, envelope)).Results);
        Assert.Equal("accepted", result.Status); Assert.True(result.IsReplay);
        await using var verify = fixture.CreateContext(pair.A.TenantId);
        Assert.Equal(1, await verify.Sales.CountAsync(value => value.OfflineSyncEventId != null, Token)); Assert.Equal(1, await verify.SalePayments.CountAsync(Token));
        Assert.Equal(1, await verify.StockMovements.CountAsync(value => value.MovementType == StockMovementType.Sale, Token)); Assert.Equal(1, await verify.CommissionEntries.CountAsync(Token));
        Assert.Equal(1, await verify.AuditLogs.CountAsync(value => value.Action == AuditAction.SaleConfirmed, Token));
    }

    private sealed class LostAcknowledgment : DbTransactionInterceptor
    {
        internal bool Enabled { get; set; }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) =>
            Enabled ? Task.FromException(new IOException("Response lost after PostgreSQL COMMIT.")) : Task.CompletedTask;
    }

    private sealed class FailWrite(string table) : DbCommandInterceptor
    {
        internal bool Enabled { get; set; }
        private void Check(DbCommand command)
        {
            if (Enabled && command.CommandText.Contains((table == "sync_events" ? "UPDATE " : "INSERT INTO ") + table, StringComparison.Ordinal))
                command.CommandText = "DO $$ BEGIN RAISE EXCEPTION 'Injected transactional write failure' USING ERRCODE = '23514'; END $$; " + command.CommandText;
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Check(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Check(command); return ValueTask.FromResult(result); }
    }
}
