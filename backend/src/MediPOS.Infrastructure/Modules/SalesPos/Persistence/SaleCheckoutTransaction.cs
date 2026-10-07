using System.Data;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.ConfirmSale;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace MediPOS.Infrastructure.Modules.SalesPos.Persistence;

internal sealed class SaleCheckoutTransaction(MediPosDbContext context) : ISaleCheckoutTransaction
{
    public async Task<ISaleCheckoutScope> BeginAsync(Guid tenantId, Guid branchId, Guid membershipId,
        Guid cashSessionId, Guid saleId, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        try
        {
            var cash = await context.CashSessions.FromSqlInterpolated($"""
                SELECT c.* FROM cash_sessions c WHERE c.tenant_id = {tenantId} AND c.branch_id = {branchId}
                    AND c.membership_id = {membershipId} AND c.id = {cashSessionId} FOR UPDATE OF c
                """).AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new ApplicationErrorException(SalesPosErrors.CashSessionRequired);
            foreach (var entry in context.ChangeTracker.Entries<SaleLine>().Where(entry => entry.Entity.SaleId == saleId).ToArray())
                entry.State = EntityState.Detached;
            foreach (var entry in context.ChangeTracker.Entries<Sale>().Where(entry => entry.Entity.Id == saleId).ToArray())
                entry.State = EntityState.Detached;
            // xmin is a system column, deliberately selected separately from s.*.
            var sale = await context.Sales.FromSqlInterpolated($"""
                SELECT s.*, s.xmin FROM sales s WHERE s.tenant_id = {tenantId} AND s.branch_id = {branchId} AND s.id = {saleId} FOR UPDATE OF s
                """).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new ApplicationErrorException(SalesPosErrors.DraftNotFound);
            // A fresh statement after the header lock wait observes the winning editor's lines.
            await context.Entry(sale).Collection(value => value.Lines).LoadAsync(cancellationToken).ConfigureAwait(false);
            var lineIds = sale.Lines.Select(line => line.Id).ToArray();
            if (sale.Status == SaleStatus.Draft && (
                await context.SalePayments.AnyAsync(payment => payment.SaleId == sale.Id, cancellationToken).ConfigureAwait(false) ||
                await context.StockMovements.AnyAsync(movement => movement.SourceSaleLineId.HasValue && lineIds.Contains(movement.SourceSaleLineId.Value), cancellationToken).ConfigureAwait(false) ||
                await context.AuditLogs.AnyAsync(audit => audit.EntityId == sale.Id && audit.Action == AuditAction.SaleConfirmed, cancellationToken).ConfigureAwait(false)))
                throw new ApplicationErrorException(SalesPosErrors.InconsistentDraft);
            return new CheckoutScope(context, transaction, cash, sale, context.Entry(sale).Property<uint>("Version").CurrentValue);
        }
        catch (PostgresException error) when (error.SqlState is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure)
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

    private sealed class CheckoutScope(MediPosDbContext context, IDbContextTransaction transaction,
        CashSession cash, Sale sale, uint version) : ISaleCheckoutScope
    {
        private bool _committed;
        private Guid? _lastProductId;
        private readonly Dictionary<Guid, InventoryLot> _lockedLots = [];
        public CashSession CashSession => cash;
        public Sale Sale => sale;
        public uint Version => version;

        public async Task<IReadOnlyList<InventoryLot>> LockLotsAsync(Guid productId, ProductType productType, decimal requested,
            DateOnly today, CancellationToken cancellationToken)
        {
            if (_committed || requested <= 0 || !Enum.IsDefined(productType) || !sale.Lines.Any(line => line.BusinessProductId == productId) ||
                (_lastProductId.HasValue && productId.CompareTo(_lastProductId.Value) <= 0))
                throw new InvalidOperationException("Products must be locked once in strictly increasing order.");
            _lastProductId = productId;
            var remaining = requested;
            var lots = new List<InventoryLot>();
            while (remaining > 0)
            {
                var selected = lots.Select(lot => lot.Id).ToArray();
                var query = productType == ProductType.Medicine
                    ? context.InventoryLots.FromSqlInterpolated($"""
                        SELECT l.* FROM inventory_lots l WHERE l.tenant_id = {sale.TenantId} AND l.branch_id = {sale.BranchId}
                            AND l.business_product_id = {productId} AND l.quantity_available_base > 0 AND l.expiration_date >= {today}
                            AND l.id <> ALL ({selected})
                        ORDER BY l.expiration_date, l.created_at, l.id LIMIT 1 FOR UPDATE OF l
                        """)
                    : context.InventoryLots.FromSqlInterpolated($"""
                        SELECT l.* FROM inventory_lots l WHERE l.tenant_id = {sale.TenantId} AND l.branch_id = {sale.BranchId}
                            AND l.business_product_id = {productId} AND l.quantity_available_base > 0 AND l.id <> ALL ({selected})
                        ORDER BY l.created_at, l.id LIMIT 1 FOR UPDATE OF l
                        """);
                InventoryLot? lot;
                try { lot = await query.AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false); }
                catch (PostgresException error) when (error.SqlState is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure)
                { throw new ApplicationErrorException(SalesPosErrors.ConcurrentEdit); }
                if (lot is null) break;
                foreach (var entry in context.ChangeTracker.Entries<InventoryLot>().Where(entry => entry.Entity.Id == lot.Id).ToArray())
                    entry.State = EntityState.Detached;
                context.InventoryLots.Attach(lot);
                _lockedLots.Add(lot.Id, lot);
                lots.Add(lot);
                remaining = StockQuantity.Add(remaining, -Math.Min(remaining, lot.QuantityAvailableBase));
            }
            return lots;
        }

        public async Task<uint> CompleteAsync(IReadOnlyList<SalePayment> payments, IReadOnlyList<StockMovement> movements,
            AuditLog audit, CancellationToken cancellationToken)
        {
            if (_committed || cash.Status != CashSessionStatus.Open || sale.Status != SaleStatus.Confirmed ||
                sale.TenantId != cash.TenantId || sale.BranchId != cash.BranchId || sale.SellerMembershipId != cash.MembershipId || sale.CashSessionId != cash.Id)
                throw new InvalidOperationException("Completion requires the locked open cash session and its confirmed sale.");
            context.ValidateAudit(audit, sale.TenantId, AuditAction.SaleConfirmed, sale.Id);
            sale.ValidateForCheckout();
            sale.ValidatePayments(payments);
            foreach (var movement in movements)
            {
                if (movement.MovementType != StockMovementType.Sale || !_lockedLots.TryGetValue(movement.InventoryLotId, out var lot))
                    throw new InvalidOperationException("Consumption requires its locked lot.");
                lot.ApplySale(movement);
            }
            context.SalePayments.AddRange(payments);
            context.StockMovements.AddRange(movements);
            context.AddAudit(audit, sale.TenantId, AuditAction.SaleConfirmed, sale.Id);
            try
            {
                await context.SaveAuditedChangesAsync(cancellationToken).ConfigureAwait(false);
                var currentVersion = context.Entry(sale).Property<uint>(nameof(Version)).CurrentValue;
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                _committed = true;
                return currentVersion;
            }
            catch (DbUpdateConcurrencyException) { throw new ApplicationErrorException(SalesPosErrors.ConcurrentEdit); }
            catch (DbUpdateException error) when (error.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: SalePaymentConfiguration.MethodIndex })
            { throw new ApplicationErrorException(SalesPosErrors.DuplicatePaymentMethod); }
            catch (Exception error) when (IsDatabaseConflict(error)) { throw new ApplicationErrorException(SalesPosErrors.ConcurrentEdit); }
        }

        private static bool IsDatabaseConflict(Exception error) =>
            (error is PostgresException postgres ? postgres : error.InnerException as PostgresException)?.SqlState
                is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure;

        public async ValueTask DisposeAsync()
        {
            try { await transaction.DisposeAsync().ConfigureAwait(false); }
            finally { if (!_committed) context.ChangeTracker.Clear(); }
        }
    }
}
