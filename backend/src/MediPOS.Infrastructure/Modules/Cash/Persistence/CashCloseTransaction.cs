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

internal sealed class CashCloseTransaction(MediPosDbContext context) : ICashCloseTransaction
{
    public async Task<ICashCloseScope> BeginAsync(Guid tenantId, Guid branchId, Guid sessionId, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var tracked in context.ChangeTracker.Entries<CashSession>().Where(entry => entry.Entity.Id == sessionId).ToArray()) tracked.State = EntityState.Detached;
            // This same first row lock is used by checkout and sale void; ledger reads happen strictly afterwards.
            var session = await context.CashSessions.FromSqlInterpolated($"""
                SELECT c.* FROM cash_sessions c WHERE c.tenant_id = {tenantId} AND c.branch_id = {branchId} AND c.id = {sessionId} FOR UPDATE OF c
                """).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new ApplicationErrorException(CashSessionErrors.NotFound);
            var description = await new CashSessionReconciliationReader(context).FindAsync(tenantId, branchId, sessionId, cancellationToken).ConfigureAwait(false)
                ?? throw new ApplicationErrorException(CashSessionErrors.NotFound);
            return new CloseScope(context, transaction, session, description.Party);
        }
        catch (PostgresException error) when (error.SqlState is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure)
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();
            throw new ApplicationErrorException(CashSessionErrors.ConcurrentClose);
        }
        catch
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();
            throw;
        }
    }

    private sealed class CloseScope(MediPosDbContext context, IDbContextTransaction transaction, CashSession session, CashSessionParty party) : ICashCloseScope
    {
        private bool _committed;
        private CashPaymentLedger? _ledger;
        public CashSession Session => session;
        public CashSessionParty Party => party;

        public async Task<CashPaymentLedger> ReadLedgerAsync(CancellationToken cancellationToken)
        {
            if (_ledger is not null || _committed || session.Status != CashSessionStatus.Open) throw new InvalidOperationException("Read ledger once after locking an open cash session.");
            _ledger = await CashSessionReconciliationReader.LoadLedgerAsync(context, session, cancellationToken).ConfigureAwait(false);
            return _ledger;
        }

        public async Task CompleteAsync(CashPaymentTotals totals, AuditLog audit, CancellationToken cancellationToken)
        {
            if (_committed || _ledger is null || session.Status != CashSessionStatus.Closed) throw new InvalidOperationException("Close completion requires its locked session and ledger.");
            var actual = CashReconciliation.Calculate(session, _ledger);
            session.ValidateClosed();
            if (actual != totals || session.ExpectedCashAmount != CashReconciliation.ExpectedCash(session.OpeningAmount, actual, CashReconciliation.CalculateTransfers(session, _ledger)))
                throw new InvalidOperationException("Close must match its locked ledger exactly.");
            context.AddAudit(audit, session.TenantId, AuditAction.CashSessionClosed, session.Id);
            try
            {
                await context.SaveAuditedChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                _committed = true;
            }
            catch (DbUpdateConcurrencyException) { throw new ApplicationErrorException(CashSessionErrors.ConcurrentClose); }
            catch (Exception error) when ((error is PostgresException postgres ? postgres : error.InnerException as PostgresException)?.SqlState
                is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure)
            { throw new ApplicationErrorException(CashSessionErrors.ConcurrentClose); }
        }

        public async ValueTask DisposeAsync()
        {
            try { await transaction.DisposeAsync().ConfigureAwait(false); }
            finally { if (!_committed) context.ChangeTracker.Clear(); }
        }
    }
}
