using System.Data;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Commissions;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.ConfirmSale;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Commissions;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Infrastructure.Modules.Commissions.Persistence;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace MediPOS.Infrastructure.Modules.SalesPos.Persistence;

internal sealed class SaleCheckoutTransaction(MediPosDbContext context) : ISaleCheckoutTransaction, IOfflineSaleCheckoutTransaction
{
    public async Task<ISaleCheckoutScope> BeginAsync(Sale draft, CancellationToken cancellationToken)
    {
        context.SelectTenant(draft.TenantId);
        var transaction = context.Database.CurrentTransaction ?? throw new InvalidOperationException("Offline checkout requires its sync transaction.");
        if (!draft.OfflineSyncEventId.HasValue || !context.ChangeTracker.Entries<Domain.Modules.OfflineSync.SyncEvent>().Any(entry =>
            entry.Entity.Id == draft.OfflineSyncEventId && entry.Entity.TenantId == draft.TenantId && entry.Entity.ProcessingStatus == "pending"))
            throw new InvalidOperationException("Offline checkout requires its reserved pending receipt.");
        await transaction.CreateSavepointAsync("offline_checkout", cancellationToken).ConfigureAwait(false);
        var retained = context.ChangeTracker.Entries().Select(entry => entry.Entity).ToHashSet(ReferenceEqualityComparer.Instance);
        try
        {
            var cash = await context.CashSessions.FromSqlInterpolated($"""
                SELECT c.* FROM cash_sessions c WHERE c.tenant_id = {draft.TenantId} AND c.branch_id = {draft.BranchId}
                    AND c.membership_id = {draft.SellerMembershipId} AND c.id = {draft.CashSessionId} FOR UPDATE OF c
                """).AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new ApplicationErrorException(SalesPosErrors.CashSessionRequired);
            if (cash.Status != CashSessionStatus.Open) throw new ApplicationErrorException(SalesPosErrors.CashSessionRequired);
            // Insert draft and lines inside the ambient transaction, respecting existing draft-only line policies.
            context.Sales.Add(draft);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new CheckoutScope(context, transaction, cash, draft, context.Entry(draft).Property<uint>("Version").CurrentValue, false, retained);
        }
        catch
        {
            await transaction.RollbackToSavepointAsync("offline_checkout", cancellationToken).ConfigureAwait(false);
            DetachNew(context, retained);
            throw;
        }
    }

    private static void DetachNew(MediPosDbContext context, HashSet<object> retained)
    {
        foreach (var entry in context.ChangeTracker.Entries().Where(entry => !retained.Contains(entry.Entity)).ToArray()) entry.State = EntityState.Detached;
    }
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
                await context.CommissionEntries.AnyAsync(entry => entry.SaleId == sale.Id, cancellationToken).ConfigureAwait(false) ||
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
        CashSession cash, Sale sale, uint version, bool ownsTransaction = true, HashSet<object>? retained = null) : ISaleCheckoutScope
    {
        private bool _committed;
        private CommissionConfigurationSnapshot? _commissionConfiguration;
        private Guid? _lastProductId;
        private readonly Dictionary<Guid, InventoryLot> _lockedLots = [];
        public CashSession CashSession => cash;
        public Sale Sale => sale;
        public uint Version => version;

        public async Task<CommissionConfigurationSnapshot> ReadCommissionConfigurationAsync(CancellationToken cancellationToken)
        {
            if (_committed || _commissionConfiguration is not null || _lastProductId.HasValue || sale.Status != SaleStatus.Draft)
                throw new InvalidOperationException("Read commission configuration once from the locked draft before stock locks.");
            _commissionConfiguration = await CommissionConfigurationReader.ReadAsync(context, sale.TenantId,
                sale.Lines.Select(line => line.BusinessProductId).Distinct().ToArray(), cancellationToken).ConfigureAwait(false);
            return _commissionConfiguration;
        }

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
            // Describe the shortage while its sellable rows remain locked. An unrelated expired product cannot
            // misclassify this product's insufficient stock; expired balances are never offered for allocation.
            if (!ownsTransaction && remaining > 0 && productType == ProductType.Medicine &&
                await context.InventoryLots.AnyAsync(lot => lot.TenantId == sale.TenantId && lot.BranchId == sale.BranchId &&
                    lot.BusinessProductId == productId && lot.QuantityAvailableBase > 0 && lot.ExpirationDate < today, cancellationToken).ConfigureAwait(false))
                throw new ApplicationErrorException(new("inventory.expired_stock", ErrorCategory.Conflict, "Stock for this medication includes expired, unsellable lots."));
            return lots;
        }

        public async Task<uint> CompleteAsync(IReadOnlyList<SalePayment> payments, IReadOnlyList<StockMovement> movements,
            IReadOnlyList<CommissionEntry> commissions, AuditLog audit, CancellationToken cancellationToken)
        {
            if (_committed || cash.Status != CashSessionStatus.Open || sale.Status != SaleStatus.Confirmed ||
                sale.TenantId != cash.TenantId || sale.BranchId != cash.BranchId || sale.SellerMembershipId != cash.MembershipId || sale.CashSessionId != cash.Id)
                throw new InvalidOperationException("Completion requires the locked open cash session and its confirmed sale.");
            context.ValidateAudit(audit, sale.TenantId, AuditAction.SaleConfirmed, sale.Id);
            sale.ValidateForCheckout();
            sale.ValidatePayments(payments);
            if (_commissionConfiguration is null || !sale.CommissionEntryCount.HasValue)
                throw new InvalidOperationException("Confirmation requires its coherent commission configuration and posting marker.");
            SaleCommissionHistory.ValidateOriginals(sale, commissions);
            var expected = new List<(Guid LineId, Guid RuleId, decimal Amount)>();
            if (_commissionConfiguration.IsEnabled)
                foreach (var line in sale.Lines)
                {
                    var rule = _commissionConfiguration.Rules.SingleOrDefault(rule => rule.BusinessProductId == line.BusinessProductId && rule.AppliesAt(sale.ConfirmedAt!.Value));
                    if (rule is null) continue;
                    var amount = CommissionCalculation.Calculate(line, rule);
                    if (amount > 0) expected.Add((line.Id, rule.Id, amount));
                }
            if (expected.Count != commissions.Count || expected.Any(item => !commissions.Any(entry =>
                entry.SaleLineId == item.LineId && entry.CommissionRuleId == item.RuleId && entry.Amount == item.Amount)))
                throw new InvalidOperationException("Earned entries must match exactly the captured rule version and sale snapshots.");
            foreach (var movement in movements)
            {
                if (movement.MovementType != StockMovementType.Sale || !_lockedLots.TryGetValue(movement.InventoryLotId, out var lot))
                    throw new InvalidOperationException("Consumption requires its locked lot.");
                lot.ApplySale(movement);
            }
            context.SalePayments.AddRange(payments);
            context.StockMovements.AddRange(movements);
            context.CommissionEntries.AddRange(commissions);
            context.AddAudit(audit, sale.TenantId, AuditAction.SaleConfirmed, sale.Id);
            try
            {
                await context.SaveAuditedChangesAsync(cancellationToken).ConfigureAwait(false);
                var currentVersion = context.Entry(sale).Property<uint>(nameof(Version)).CurrentValue;
                if (ownsTransaction) await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
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
            if (!ownsTransaction)
            {
                if (!_committed)
                {
                    await transaction.RollbackToSavepointAsync("offline_checkout").ConfigureAwait(false);
                    DetachNew(context, retained!);
                }
                return;
            }
            try { await transaction.DisposeAsync().ConfigureAwait(false); }
            finally { if (!_committed) context.ChangeTracker.Clear(); }
        }
    }
}
