using System.Data;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Transfers;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.Transfers;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace MediPOS.Infrastructure.Modules.Transfers.Persistence;

internal sealed class TransferTransaction(MediPosDbContext context) : ITransferTransaction
{
    public async Task<ITransferScope> BeginAsync(Guid tenantId, Guid transferId, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var entry in context.ChangeTracker.Entries<TransferLine>().Where(e => e.Entity.TransferId == transferId).ToArray()) entry.State = EntityState.Detached;
            foreach (var entry in context.ChangeTracker.Entries<TransferLotAllocation>().Where(e => e.Entity.TransferId == transferId).ToArray()) entry.State = EntityState.Detached;
            foreach (var entry in context.ChangeTracker.Entries<Transfer>().Where(e => e.Entity.Id == transferId).ToArray()) entry.State = EntityState.Detached;
            var transfer = await context.Transfers.FromSqlInterpolated($"""
                SELECT t.* FROM transfers t WHERE t.tenant_id = {tenantId} AND t.id = {transferId} FOR UPDATE OF t
                """).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? throw new ApplicationErrorException(TransferErrors.NotFound);
            await context.Entry(transfer).Collection(t => t.Lines).Query().OrderBy(l => l.Id).Take(Transfer.MaximumLines + 1).LoadAsync(cancellationToken).ConfigureAwait(false);
            var snapshot = await TransferReader.LoadChildrenAsync(context, transfer, trackedAllocations: true, cancellationToken).ConfigureAwait(false);
            return new Scope(context, transaction, snapshot);
        }
        catch (PostgresException error) when (error.SqlState is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure)
        { await transaction.DisposeAsync().ConfigureAwait(false); context.ChangeTracker.Clear(); throw new ApplicationErrorException(TransferErrors.ConcurrentOperation); }
        catch { await transaction.DisposeAsync().ConfigureAwait(false); context.ChangeTracker.Clear(); throw; }
    }
    private sealed class Scope(MediPosDbContext context, IDbContextTransaction transaction, TransferSnapshot snapshot) : ITransferScope
    {
        private bool _committed;
        private IReadOnlyList<InventoryLot>? _locked;
        public TransferSnapshot Snapshot => snapshot;
        public async Task<IReadOnlyList<InventoryLot>> LockSourceLotsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
        {
            if (_locked is not null || _committed || snapshot.Transfer.Status != TransferStatus.Approved || ids.Count is 0 or > Transfer.MaximumAllocations)
                throw new InvalidOperationException("Source lots require a locked approved transfer.");
            var ordered = ids.Distinct().Order().ToArray();
            foreach (var entry in context.ChangeTracker.Entries<InventoryLot>().Where(e => ordered.Contains(e.Entity.Id)).ToArray()) entry.State = EntityState.Detached;
            try
            {
                // Globally ordered source lot locks, shared with every writer of these real balances.
                _locked = await context.InventoryLots.FromSqlInterpolated($"""
                    SELECT l.* FROM inventory_lots l WHERE l.tenant_id = {snapshot.Transfer.TenantId}
                    AND l.branch_id = {snapshot.Transfer.SourceBranchId} AND l.id = ANY({ordered}) ORDER BY l.id FOR UPDATE OF l
                    """).ToArrayAsync(cancellationToken).ConfigureAwait(false);
                return _locked;
            }
            catch (PostgresException error) when (error.SqlState is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure)
            { throw new ApplicationErrorException(TransferErrors.ConcurrentOperation); }
        }
        public async Task CompleteAsync(IReadOnlyList<TransferLotAllocation> newAllocations, IReadOnlyList<InventoryLot> newLots,
            IReadOnlyList<StockMovement> movements, TransferEvent transition, AuditLog audit, CancellationToken cancellationToken)
        {
            if (_committed || (transition.EventType == TransferEventType.Dispatched && _locked is null)) throw new InvalidOperationException("A transition completes its concrete locked scope only once.");
            TransferHistory.Validate(snapshot.Transfer, [.. snapshot.Events, transition], newAllocations.Count > 0 ? newAllocations : snapshot.Allocations);
            foreach (var movement in movements.Where(m => m.MovementType == StockMovementType.TransferDispatch))
                _locked!.Single(l => l.Id == movement.InventoryLotId).ApplyTransferDispatch(movement);
            context.TransferLotAllocations.AddRange(newAllocations); context.InventoryLots.AddRange(newLots);
            context.StockMovements.AddRange(movements); context.TransferEvents.Add(transition);
            context.AddAudit(audit, snapshot.Transfer.TenantId, transition.EventType switch
            {
                TransferEventType.Approved => AuditAction.TransferApproved,
                TransferEventType.Dispatched => AuditAction.TransferDispatched,
                TransferEventType.Received => AuditAction.TransferReceived,
                TransferEventType.Cancelled => AuditAction.TransferCancelled,
                _ => throw new InvalidOperationException("Request uses its own creation boundary."),
            }, snapshot.Transfer.Id);
            try
            {
                await context.SaveAuditedChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false); _committed = true;
            }
            catch (DbUpdateConcurrencyException) { throw new ApplicationErrorException(TransferErrors.ConcurrentOperation); }
            catch (Exception error) when ((error is PostgresException postgres ? postgres : error.InnerException as PostgresException)?.SqlState
                is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure)
            { throw new ApplicationErrorException(TransferErrors.ConcurrentOperation); }
        }
        public async ValueTask DisposeAsync()
        {
            try { await transaction.DisposeAsync().ConfigureAwait(false); }
            finally { if (!_committed) context.ChangeTracker.Clear(); }
        }
    }
}
