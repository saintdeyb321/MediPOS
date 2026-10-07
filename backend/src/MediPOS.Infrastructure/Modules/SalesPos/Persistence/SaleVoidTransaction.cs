using System.Data;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.VoidSale;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Infrastructure.Modules.Inventory.Persistence.Configurations;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace MediPOS.Infrastructure.Modules.SalesPos.Persistence;

internal sealed class SaleVoidTransaction(MediPosDbContext context) : ISaleVoidTransaction
{
    public async Task<SaleVoidSnapshot?> FindAsync(Guid tenantId, Guid branchId, Guid saleId, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        return await context.Sales.AsNoTracking().Where(sale => sale.TenantId == tenantId && sale.BranchId == branchId && sale.Id == saleId)
            .Select(sale => new SaleVoidSnapshot(sale, EF.Property<uint>(sale, "Version"))).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ISaleVoidScope> BeginAsync(Guid tenantId, Guid branchId, Guid cashSessionId, Guid saleId, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        try
        {
            var cash = await context.CashSessions.FromSqlInterpolated($"""
                SELECT c.* FROM cash_sessions c WHERE c.tenant_id = {tenantId} AND c.branch_id = {branchId} AND c.id = {cashSessionId} FOR UPDATE OF c
                """).AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new ApplicationErrorException(SalesPosErrors.CashSessionClosed);
            foreach (var entry in context.ChangeTracker.Entries<SaleLine>().Where(entry => entry.Entity.SaleId == saleId).ToArray()) entry.State = EntityState.Detached;
            foreach (var entry in context.ChangeTracker.Entries<Sale>().Where(entry => entry.Entity.Id == saleId).ToArray()) entry.State = EntityState.Detached;
            var sale = await context.Sales.FromSqlInterpolated($"""
                SELECT s.*, s.xmin FROM sales s WHERE s.tenant_id = {tenantId} AND s.branch_id = {branchId} AND s.id = {saleId} FOR UPDATE OF s
                """).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new ApplicationErrorException(SalesPosErrors.SaleNotFound);
            await context.Entry(sale).Collection(value => value.Lines).LoadAsync(cancellationToken).ConfigureAwait(false);
            return new VoidScope(context, transaction, cash, sale, context.Entry(sale).Property<uint>("Version").CurrentValue);
        }
        catch (PostgresException error) when (IsLockConflict(error))
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();
            throw new ApplicationErrorException(SalesPosErrors.ConcurrentEdit);
        }
        catch
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();
            throw;
        }
    }

    private static bool IsLockConflict(PostgresException error) => error.SqlState is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.LockNotAvailable;

    private sealed class VoidScope(MediPosDbContext context, IDbContextTransaction transaction, CashSession cash, Sale sale, uint version) : ISaleVoidScope
    {
        private bool _committed;
        private bool _lotsLocked;
        private SaleVoidEffects? _effects;
        private readonly Dictionary<Guid, InventoryLot> _lots = [];
        public CashSession CashSession => cash;
        public Sale Sale => sale;
        public uint Version => version;

        public async Task<SaleVoidEffects> LoadEffectsAsync(CancellationToken cancellationToken)
        {
            if (_effects is not null || _committed || sale.Status != SaleStatus.Confirmed) throw new InvalidOperationException("Load original effects once from the locked confirmed sale.");
            var lineIds = sale.Lines.Select(line => line.Id).ToArray();
            var payments = await context.SalePayments.AsNoTracking().Where(payment => payment.TenantId == sale.TenantId && payment.SaleId == sale.Id)
                .OrderBy(payment => payment.Id).ToArrayAsync(cancellationToken).ConfigureAwait(false);
            var movements = await context.StockMovements.AsNoTracking().Where(movement => movement.TenantId == sale.TenantId &&
                movement.MovementType == StockMovementType.Sale && movement.SourceSaleLineId.HasValue && lineIds.Contains(movement.SourceSaleLineId.Value))
                .OrderBy(movement => movement.Id).ToArrayAsync(cancellationToken).ConfigureAwait(false);
            var movementIds = movements.Select(movement => movement.Id).ToArray();
            var confirmation = await context.AuditLogs.AsNoTracking().Where(audit => audit.EntityId == sale.Id && audit.Action == AuditAction.SaleConfirmed)
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);
            if (confirmation.Length != 1 || confirmation[0].OccurredAt != sale.ConfirmedAt || movements.Any(movement => movement.ActorId != confirmation[0].ActorId))
                throw new ApplicationErrorException(SalesPosErrors.CorruptedHistory);
            if (await context.SalePaymentReversals.AnyAsync(reversal => reversal.SaleId == sale.Id, cancellationToken).ConfigureAwait(false) ||
                await context.StockMovements.AnyAsync(movement => movement.ReversesStockMovementId.HasValue && movementIds.Contains(movement.ReversesStockMovementId.Value), cancellationToken).ConfigureAwait(false))
                throw new ApplicationErrorException(SalesPosErrors.ReversalAlreadyExists);
            if (await context.AuditLogs.AnyAsync(audit => audit.EntityId == sale.Id && audit.Action == AuditAction.SaleVoided, cancellationToken).ConfigureAwait(false))
                throw new ApplicationErrorException(SalesPosErrors.CorruptedHistory);
            foreach (var payment in payments)
            {
                foreach (var tracked in context.ChangeTracker.Entries<SalePayment>().Where(entry => entry.Entity.Id == payment.Id).ToArray()) tracked.State = EntityState.Detached;
                context.SalePayments.Attach(payment);
            }
            foreach (var movement in movements)
            {
                foreach (var tracked in context.ChangeTracker.Entries<StockMovement>().Where(entry => entry.Entity.Id == movement.Id).ToArray()) tracked.State = EntityState.Detached;
                context.StockMovements.Attach(movement);
            }
            _effects = new(payments, movements);
            return _effects;
        }

        public async Task<IReadOnlyList<InventoryLot>> LockLotsAsync(CancellationToken cancellationToken)
        {
            if (_effects is null || _lotsLocked || _committed) throw new InvalidOperationException("Lock original lots once after loading effects.");
            foreach (var id in _effects.Movements.Select(movement => movement.InventoryLotId).Distinct().Order())
            {
                InventoryLot lot;
                try
                {
                    // Checkout uses FEFO/product order; NOWAIT prevents a wait cycle between that order and global lot IDs.
                    lot = await context.InventoryLots.FromSqlInterpolated($"""
                        SELECT l.* FROM inventory_lots l WHERE l.tenant_id = {sale.TenantId} AND l.branch_id = {sale.BranchId} AND l.id = {id} FOR UPDATE OF l NOWAIT
                        """).AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                        ?? throw new ApplicationErrorException(SalesPosErrors.CorruptedHistory);
                }
                catch (PostgresException error) when (IsLockConflict(error)) { throw new ApplicationErrorException(SalesPosErrors.ConcurrentEdit); }
                foreach (var tracked in context.ChangeTracker.Entries<InventoryLot>().Where(entry => entry.Entity.Id == lot.Id).ToArray()) tracked.State = EntityState.Detached;
                context.InventoryLots.Attach(lot);
                _lots.Add(lot.Id, lot);
            }
            _lotsLocked = true;
            return _lots.Values.ToArray();
        }

        public async Task<uint> CompleteAsync(IReadOnlyList<SalePaymentReversal> payments, IReadOnlyList<StockMovement> movements,
            AuditLog audit, CancellationToken cancellationToken)
        {
            if (_committed || !_lotsLocked || _effects is null || sale.Status != SaleStatus.Voided || cash.Status != CashSessionStatus.Open ||
                cash.Id != sale.CashSessionId || cash.TenantId != sale.TenantId || cash.BranchId != sale.BranchId || cash.MembershipId != sale.SellerMembershipId)
                throw new InvalidOperationException("Void completion requires its locked original sale/cash/lots.");
            SaleVoidHistory.ValidateReversals(sale, _effects.Payments, _effects.Movements, payments, movements);
            var originals = _effects.Movements.ToDictionary(movement => movement.Id);
            foreach (var movement in movements) _lots[movement.InventoryLotId].ApplySaleReversal(movement, originals[movement.ReversesStockMovementId!.Value]);
            context.SalePaymentReversals.AddRange(payments);
            context.StockMovements.AddRange(movements);
            context.AddAudit(audit, sale.TenantId, AuditAction.SaleVoided, sale.Id);
            try
            {
                await context.SaveAuditedChangesAsync(cancellationToken).ConfigureAwait(false);
                var currentVersion = context.Entry(sale).Property<uint>(nameof(Version)).CurrentValue;
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                _committed = true;
                return currentVersion;
            }
            catch (DbUpdateConcurrencyException) { throw new ApplicationErrorException(SalesPosErrors.ConcurrentEdit); }
            catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } unique &&
                unique.ConstraintName is SalePaymentReversalConfiguration.OriginalPaymentIndex or StockMovementConfiguration.ReversalIndex)
            { throw new ApplicationErrorException(SalesPosErrors.ReversalAlreadyExists); }
            catch (Exception error) when ((error is PostgresException postgres ? postgres : error.InnerException as PostgresException) is { } conflict && IsLockConflict(conflict))
            { throw new ApplicationErrorException(SalesPosErrors.ConcurrentEdit); }
        }

        public async ValueTask DisposeAsync()
        {
            try { await transaction.DisposeAsync().ConfigureAwait(false); }
            finally { if (!_committed) context.ChangeTracker.Clear(); }
        }
    }
}
