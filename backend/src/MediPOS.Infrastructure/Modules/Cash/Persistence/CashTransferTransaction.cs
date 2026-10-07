using System.Data;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Cash;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace MediPOS.Infrastructure.Modules.Cash.Persistence;

internal sealed class CashTransferTransaction(MediPosDbContext context) : ICashTransferTransaction
{
    public async Task<ICashTransferDispatchScope> BeginDispatchAsync(Guid tenantId, Guid branchId, Guid sessionId, Guid destinationBranchId, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        try
        {
            DetachSession(sessionId);
            // Same cash row boundary as ConfirmSale, VoidSale and CloseCashSession. No tenant lock.
            var session = await LockCashAsync(tenantId, branchId, sessionId, cancellationToken).ConfigureAwait(false);
            if (!await context.Branches.AnyAsync(b => b.TenantId == tenantId && b.Id == destinationBranchId, cancellationToken).ConfigureAwait(false))
                throw new ApplicationErrorException(CashTransferErrors.NotFound);
            return new DispatchScope(context, transaction, session, destinationBranchId);
        }
        catch (PostgresException error) when (IsConcurrent(error))
        { await transaction.DisposeAsync().ConfigureAwait(false); context.ChangeTracker.Clear(); throw new ApplicationErrorException(CashTransferErrors.Concurrent); }
        catch { await transaction.DisposeAsync().ConfigureAwait(false); context.ChangeTracker.Clear(); throw; }
    }
    public async Task<ICashTransferReceiveScope> BeginReceiveAsync(Guid tenantId, Guid transferId, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var entry in context.ChangeTracker.Entries<CashTransfer>().Where(e => e.Entity.Id == transferId).ToArray()) entry.State = EntityState.Detached;
            // Receipt order: CashTransfer first, destination CashSession second; close never locks CashTransfer.
            var transfer = await context.CashTransfers.FromSqlInterpolated($"SELECT t.* FROM cash_transfers t WHERE t.tenant_id = {tenantId} AND t.id = {transferId} FOR UPDATE OF t")
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? throw new ApplicationErrorException(CashTransferErrors.NotFound);
            return new ReceiveScope(this, context, transaction, transfer);
        }
        catch (PostgresException error) when (IsConcurrent(error))
        { await transaction.DisposeAsync().ConfigureAwait(false); context.ChangeTracker.Clear(); throw new ApplicationErrorException(CashTransferErrors.Concurrent); }
        catch { await transaction.DisposeAsync().ConfigureAwait(false); context.ChangeTracker.Clear(); throw; }
    }
    private void DetachSession(Guid id)
    {
        foreach (var entry in context.ChangeTracker.Entries<CashSession>().Where(e => e.Entity.Id == id).ToArray()) entry.State = EntityState.Detached;
    }
    private async Task<CashSession> LockCashAsync(Guid tenant, Guid branch, Guid id, CancellationToken token)
    {
        try
        {
            return await context.CashSessions.FromSqlInterpolated($"SELECT c.* FROM cash_sessions c WHERE c.tenant_id = {tenant} AND c.branch_id = {branch} AND c.id = {id} FOR UPDATE OF c")
                .SingleOrDefaultAsync(token).ConfigureAwait(false) ?? throw new ApplicationErrorException(CashTransferErrors.NotFound);
        }
        catch (PostgresException error) when (IsConcurrent(error)) { throw new ApplicationErrorException(CashTransferErrors.Concurrent); }
    }
    private static bool IsConcurrent(PostgresException error) => error.SqlState is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure;
    private static async Task CommitAsync(MediPosDbContext db, IDbContextTransaction transaction, CashTransfer transfer, AuditLog audit, bool receipt, CancellationToken token)
    {
        db.AddAudit(audit, transfer.TenantId, receipt ? AuditAction.CashTransferReceived : AuditAction.CashTransferDispatched, transfer.Id);
        try { await db.SaveAuditedChangesAsync(token).ConfigureAwait(false); await transaction.CommitAsync(token).ConfigureAwait(false); }
        catch (DbUpdateConcurrencyException) { throw new ApplicationErrorException(CashTransferErrors.Concurrent); }
        catch (Exception error) when ((error is PostgresException postgres ? postgres : error.InnerException as PostgresException)?.SqlState
            is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure)
        { throw new ApplicationErrorException(CashTransferErrors.Concurrent); }
    }
    private sealed class DispatchScope(MediPosDbContext db, IDbContextTransaction transaction, CashSession session, Guid destination) : ICashTransferDispatchScope
    {
        private bool _committed;
        private CashPaymentLedger? _ledger;
        public CashSession Session => session;
        public async Task<CashPaymentLedger> ReadLedgerAsync(CancellationToken cancellationToken)
        {
            if (_committed || _ledger is not null || session.Status != CashSessionStatus.Open) throw new InvalidOperationException("Read current ledger after locking an open source session.");
            _ledger = await CashSessionReconciliationReader.LoadLedgerAsync(db, session, cancellationToken).ConfigureAwait(false); return _ledger;
        }
        public async Task CompleteAsync(CashTransfer transfer, AuditLog audit, CancellationToken cancellationToken)
        {
            transfer.Validate();
            if (_committed || _ledger is null || session.Status != CashSessionStatus.Open || transfer.Status != CashTransferStatus.InTransit ||
                transfer.TenantId != session.TenantId || transfer.SourceBranchId != session.BranchId || transfer.SourceCashSessionId != session.Id || transfer.DestinationBranchId != destination ||
                transfer.Amount > CashReconciliation.ExpectedCash(session.OpeningAmount, CashReconciliation.Calculate(session, _ledger), CashReconciliation.CalculateTransfers(session, _ledger)))
                throw new InvalidOperationException("Dispatch must match its locked source and current expected cash.");
            db.CashTransfers.Add(transfer);
            await CommitAsync(db, transaction, transfer, audit, receipt: false, cancellationToken).ConfigureAwait(false); _committed = true;
        }
        public async ValueTask DisposeAsync()
        {
            try { await transaction.DisposeAsync().ConfigureAwait(false); } finally { if (!_committed) db.ChangeTracker.Clear(); }
        }
    }
    private sealed class ReceiveScope(CashTransferTransaction owner, MediPosDbContext db, IDbContextTransaction transaction, CashTransfer transfer) : ICashTransferReceiveScope
    {
        private bool _committed;
        private CashSession? _destination;
        public CashTransfer Transfer => transfer;
        public async Task<CashSession> LockDestinationAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            if (_committed || _destination is not null || transfer.Status != CashTransferStatus.InTransit) throw new InvalidOperationException("Lock a destination only for the locked in-transit transfer.");
            owner.DetachSession(sessionId);
            _destination = await owner.LockCashAsync(transfer.TenantId, transfer.DestinationBranchId, sessionId, cancellationToken).ConfigureAwait(false); return _destination;
        }
        public async Task CompleteAsync(AuditLog audit, CancellationToken cancellationToken)
        {
            transfer.Validate();
            if (_committed || _destination is null || _destination.Status != CashSessionStatus.Open || transfer.Status != CashTransferStatus.Received ||
                transfer.DestinationCashSessionId != _destination.Id || transfer.ReceivedAt < _destination.OpenedAt)
                throw new InvalidOperationException("Receipt must match its locked open destination.");
            await CommitAsync(db, transaction, transfer, audit, receipt: true, cancellationToken).ConfigureAwait(false); _committed = true;
        }
        public async ValueTask DisposeAsync()
        {
            try { await transaction.DisposeAsync().ConfigureAwait(false); } finally { if (!_committed) db.ChangeTracker.Clear(); }
        }
    }
}
