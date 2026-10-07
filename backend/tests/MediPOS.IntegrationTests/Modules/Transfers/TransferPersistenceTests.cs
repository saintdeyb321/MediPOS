using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Transfers;
using MediPOS.Application.Modules.Transfers.CancelTransfer;
using MediPOS.Application.Modules.Transfers.GetTransfer;
using MediPOS.Application.Modules.Transfers.ReceiveTransfer;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.Transfers;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.Transfers;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class TransferPersistenceTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task MigrationMatchesModelEvolvesPurchaseProvenanceAndPreservesAppendOnlyEffectUniqueness()
    {
        await using var context = fixture.CreateConstraintContext();
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Contains(await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken), name => name.EndsWith("_AddProductTransfers", StringComparison.Ordinal));
        Assert.Equal(4, await context.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM pg_class WHERE relnamespace = 'public'::regnamespace
            AND relname IN ('transfers','transfer_lines','transfer_events','transfer_lot_allocations') AND relrowsecurity AND relforcerowsecurity
            """).SingleAsync(TestContext.Current.CancellationToken));
        Assert.False(await context.Database.SqlQueryRaw<bool>("""
            SELECT i.indisunique AS "Value" FROM pg_index i JOIN pg_class c ON c.oid = i.indexrelid
            WHERE c.relname = 'IX_inventory_lots_tenant_id_source_purchase_line_id'
            """).SingleAsync(TestContext.Current.CancellationToken));
        Assert.True(await context.Database.SqlQueryRaw<bool>("""
            SELECT i.indisunique AS "Value" FROM pg_index i JOIN pg_class c ON c.oid = i.indexrelid WHERE c.relname = 'ux_stock_movements_transfer_effect'
            """).SingleAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MultiLotPurchaseDispatchAndFullOrPartialReceiptPreserveBothBalancesLineageEventsAndAudit(bool partial)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var rows = await TransferTestData.CreateAsync(source, tenant);
        await using var verify = fixture.CreateContext(tenant.TenantId);
        var original = await verify.InventoryLots.AsNoTracking().OrderBy(l => l.Id).ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.All(original, l => Assert.Null(l.SourceTransferLotAllocationId));
        Assert.Equal(5m, original.Sum(l => l.QuantityAvailableBase));
        Assert.Equal(2, await verify.StockMovements.CountAsync(TestContext.Current.CancellationToken));
        await TransferTestData.ApproveAsync(source, rows);
        Assert.Equal(5m, await verify.InventoryLots.SumAsync(l => l.QuantityAvailableBase, TestContext.Current.CancellationToken));
        Assert.Equal(2, await verify.StockMovements.CountAsync(TestContext.Current.CancellationToken));
        var dispatched = await TransferTestData.DispatchAsync(source, rows);
        Assert.Equal("in_transit", dispatched.Status);
        Assert.Equal(2, dispatched.Allocations.Count);
        Assert.Equal(0m, await verify.InventoryLots.SumAsync(l => l.QuantityAvailableBase, TestContext.Current.CancellationToken));
        Assert.Equal(0, await verify.InventoryLots.CountAsync(l => l.BranchId == tenant.SpareBranchId, TestContext.Current.CancellationToken));
        var received = await TransferTestData.ReceiveAsync(source, rows, dispatched, partial);
        Assert.Equal("received", received.Status);
        Assert.Equal(partial ? 2m : 0m, received.Allocations.Sum(a => a.DifferenceBase!.Value));
        Assert.Equal(partial ? 3m : 5m, received.Allocations.Sum(a => a.ReceivedQuantityBase!.Value));
        var destination = await verify.InventoryLots.AsNoTracking().Where(l => l.BranchId == tenant.SpareBranchId).ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, destination.Length);
        foreach (var allocation in received.Allocations)
        {
            var old = original.Single(l => l.Id == allocation.SourceInventoryLotId);
            var newLot = destination.Single(l => l.SourceTransferLotAllocationId == allocation.Id);
            Assert.NotEqual(old.Id, newLot.Id);
            Assert.Equal((old.SourcePurchaseLineId, old.BatchNumber, old.ExpirationDate, old.BusinessProductId),
                (newLot.SourcePurchaseLineId, newLot.BatchNumber, newLot.ExpirationDate, newLot.BusinessProductId));
            Assert.Equal((old.SourcePurchaseLineId, old.BatchNumber, old.ExpirationDate), (allocation.SourcePurchaseLineId, allocation.BatchNumber, allocation.ExpirationDate));
            Assert.Equal(allocation.ReceivedQuantityBase, newLot.QuantityAvailableBase);
            Assert.Equal(2, await verify.InventoryLots.CountAsync(l => l.SourcePurchaseLineId == old.SourcePurchaseLineId, TestContext.Current.CancellationToken));
        }
        var allLots = await verify.InventoryLots.AsNoTracking().ToArrayAsync(TestContext.Current.CancellationToken);
        foreach (var lot in allLots)
        {
            var sum = await verify.StockMovements.Where(m => m.InventoryLotId == lot.Id).SumAsync(m => m.QuantityDeltaBase, TestContext.Current.CancellationToken);
            Assert.Equal(lot.QuantityAvailableBase, sum);
            if (rows.SourceLotIds.Contains(lot.Id)) Assert.Equal(tenant.Identity.BranchId, lot.BranchId);
        }
        var events = await verify.TransferEvents.Where(e => e.TransferId == rows.Requested.Id).ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(4, events.Length);
        Assert.Equal(tenant.Identity.UserId, Assert.Single(events, e => e.EventType == TransferEventType.Requested).ActorId);
        Assert.All(events.Where(e => e.EventType != TransferEventType.Requested), e => Assert.Equal(rows.OwnerId, e.ActorId));
        Assert.All(events, e => Assert.Equal(TimeSpan.Zero, e.OccurredAt.Offset));
        var audits = await verify.AuditLogs.Where(a => a.EntityType == AuditEntityType.Transfer && a.EntityId == rows.Requested.Id).ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(4, audits.Length);
        var receiptAudit = Assert.Single(audits, a => a.Action == AuditAction.TransferReceived);
        using var after = System.Text.Json.JsonDocument.Parse(receiptAudit.AfterJson!);
        Assert.Equal(4, after.RootElement.EnumerateObject().Count());
        Assert.Equal(partial ? 2m : 0m, after.RootElement.GetProperty("totalDifference").GetDecimal());
        var get = await source.GetRequiredService<GetTransferHandler>().HandleAsync(new(tenant.TenantId, rows.Requested.Id), TestContext.Current.CancellationToken);
        Assert.Equal(received.Allocations, get.Allocations);
        Assert.Equal(4, get.Events.Count);
        var overwrite = await Assert.ThrowsAsync<PostgresException>(() => verify.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE transfer_lot_allocations SET received_quantity_base = 0 WHERE id = {received.Allocations[0].Id}", TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.CheckViolation, overwrite.SqlState);
        Assert.Equal("ck_transfer_allocation_receipt_once", overwrite.ConstraintName);
    }

    [Fact]
    public async Task ZeroReceiptRecordsFullTransitDifferenceWithoutEmptyDestinationLotsOrPositiveMovements()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var rows = await TransferTestData.CreateAsync(source, tenant);
        await TransferTestData.ApproveAsync(source, rows);
        var dispatched = await TransferTestData.DispatchAsync(source, rows);
        var result = await source.GetRequiredService<ReceiveTransferHandler>().HandleAsync(new(tenant.TenantId, rows.Requested.Id,
            dispatched.Allocations.Select(a => new TransferReceiptSelection(a.Id, 0m)).ToArray()), TestContext.Current.CancellationToken);
        Assert.Equal(5m, result.Allocations.Sum(a => a.DifferenceBase!.Value));
        await using var context = fixture.CreateContext(tenant.TenantId);
        Assert.False(await context.InventoryLots.AnyAsync(l => l.BranchId == tenant.SpareBranchId, TestContext.Current.CancellationToken));
        Assert.False(await context.StockMovements.AnyAsync(m => m.MovementType == StockMovementType.TransferReceipt, TestContext.Current.CancellationToken));
        Assert.All(await context.TransferLotAllocations.ToArrayAsync(TestContext.Current.CancellationToken), a => Assert.Equal(0m, a.ReceivedQuantityBase));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequestedOrApprovedCancellationHasOneReasonEventAndAuditAndNeverMovesStock(bool approved)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var rows = await TransferTestData.CreateAsync(source, tenant);
        if (approved) await TransferTestData.ApproveAsync(source, rows);
        var result = await source.GetRequiredService<CancelTransferHandler>().HandleAsync(new(tenant.TenantId, rows.Requested.Id, "  Origen rechazó  "), TestContext.Current.CancellationToken);
        Assert.Equal("cancelled", result.Status);
        Assert.Equal("Origen rechazó", result.Events[^1].Reason);
        await using var context = fixture.CreateContext(tenant.TenantId);
        Assert.Equal(5m, await context.InventoryLots.SumAsync(l => l.QuantityAvailableBase, TestContext.Current.CancellationToken));
        Assert.Equal(2, await context.StockMovements.CountAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.TransferLotAllocations.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.AuditLogs.CountAsync(a => a.Action == AuditAction.TransferCancelled, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OverReceiptCannotPartiallyRecordQuantitiesAndEfCannotEditOrDeleteTransitionHistory()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var rows = await TransferTestData.CreateAsync(scope.ServiceProvider, tenant);
        await TransferTestData.ApproveAsync(scope.ServiceProvider, rows);
        var dispatched = await TransferTestData.DispatchAsync(scope.ServiceProvider, rows);
        var quantities = dispatched.Allocations.Select(a => new TransferReceiptSelection(a.Id, a.DispatchedQuantityBase)).ToArray();
        quantities[^1] = quantities[^1] with { QuantityBase = quantities[^1].QuantityBase + 1m };
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => scope.ServiceProvider.GetRequiredService<ReceiveTransferHandler>()
            .HandleAsync(new(tenant.TenantId, rows.Requested.Id, quantities), TestContext.Current.CancellationToken));
        Assert.Equal(TransferErrors.InvalidReceipt, error.Error);
        await using var verify = fixture.CreateContext(tenant.TenantId);
        Assert.All(await verify.TransferLotAllocations.ToArrayAsync(TestContext.Current.CancellationToken), a => Assert.Null(a.ReceivedQuantityBase));
        Assert.Equal(TransferStatus.InTransit, await verify.Transfers.Select(t => t.Status).SingleAsync(TestContext.Current.CancellationToken));
        var e = await verify.TransferEvents.FirstAsync(TestContext.Current.CancellationToken);
        verify.TransferEvents.Remove(e);
        await Assert.ThrowsAsync<InvalidOperationException>(() => verify.SaveChangesAsync(TestContext.Current.CancellationToken));
        verify.ChangeTracker.Clear();
        var line = await verify.TransferLines.FirstAsync(TestContext.Current.CancellationToken);
        verify.Entry(line).Property(l => l.RequestedQuantity).CurrentValue = 1m;
        await Assert.ThrowsAsync<InvalidOperationException>(() => verify.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("dispatch", true)]
    [InlineData("dispatch", false)]
    [InlineData("receipt", true)]
    [InlineData("receipt", false)]
    public async Task AuditOrMovementFailureRollsBackAllBalancesAllocationsTransitionAndHistory(string action, bool failAudit)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var rows = await TransferTestData.CreateAsync(source, tenant);
        await TransferTestData.ApproveAsync(source, rows);
        var dispatched = action == "receipt" ? await TransferTestData.DispatchAsync(source, rows) : null;
        var before = await MediPOS.IntegrationTests.Modules.SalesPos.InternalTicketTestData.HistoryAsync(fixture, tenant.TenantId);
        var constraint = "test_transfer_" + Guid.NewGuid().ToString("N");
        var auditCode = action == "receipt" ? "transfer.received" : "transfer.dispatched";
        var movementCode = action == "receipt" ? "transfer_receipt" : "transfer_dispatch";
        var branch = action == "receipt" ? tenant.SpareBranchId : tenant.Identity.BranchId;
        await using var admin = fixture.CreateConstraintContext();
        var ddl = failAudit
            ? await admin.Database.SqlQuery<string>($"SELECT format('ALTER TABLE audit_logs ADD CONSTRAINT %I CHECK (action <> %L OR entity_id <> %L::uuid)', {constraint}, {auditCode}, {rows.Requested.Id.ToString()}) AS \"Value\"").SingleAsync(TestContext.Current.CancellationToken)
            : await admin.Database.SqlQuery<string>($"SELECT format('ALTER TABLE stock_movements ADD CONSTRAINT %I CHECK (movement_type <> %L OR branch_id <> %L::uuid)', {constraint}, {movementCode}, {branch.ToString()}) AS \"Value\"").SingleAsync(TestContext.Current.CancellationToken);
        await admin.Database.ExecuteSqlRawAsync(ddl, TestContext.Current.CancellationToken);
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => action == "receipt" ? TransferTestData.ReceiveAsync(source, rows, dispatched!) : TransferTestData.DispatchAsync(source, rows));
            Assert.Empty(source.GetRequiredService<MediPosDbContext>().ChangeTracker.Entries());
            Assert.Equal(before, await MediPOS.IntegrationTests.Modules.SalesPos.InternalTicketTestData.HistoryAsync(fixture, tenant.TenantId));
            await using var verify = fixture.CreateContext(tenant.TenantId);
            Assert.Equal(action == "receipt" ? TransferStatus.InTransit : TransferStatus.Approved, await verify.Transfers.Select(t => t.Status).SingleAsync(TestContext.Current.CancellationToken));
            Assert.Equal(action == "receipt" ? 2 : 0, await verify.TransferLotAllocations.CountAsync(TestContext.Current.CancellationToken));
            Assert.All(await verify.TransferLotAllocations.ToArrayAsync(TestContext.Current.CancellationToken), a => Assert.Null(a.ReceivedQuantityBase));
            Assert.Equal(action == "receipt" ? 3 : 2, await verify.TransferEvents.CountAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            var table = failAudit ? "audit_logs" : "stock_movements";
            var drop = await admin.Database.SqlQuery<string>($"SELECT format('ALTER TABLE %I DROP CONSTRAINT %I', {table}, {constraint}) AS \"Value\"").SingleAsync(TestContext.Current.CancellationToken);
            await admin.Database.ExecuteSqlRawAsync(drop, TestContext.Current.CancellationToken);
        }
        if (action == "receipt") await TransferTestData.ReceiveAsync(source, rows, dispatched!);
        else await TransferTestData.DispatchAsync(source, rows);
    }
}
