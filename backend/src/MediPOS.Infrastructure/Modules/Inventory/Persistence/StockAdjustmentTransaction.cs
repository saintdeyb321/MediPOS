using System.Data;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Inventory;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MediPOS.Infrastructure.Modules.Inventory.Persistence;

internal sealed class StockAdjustmentTransaction(MediPosDbContext context) : IStockAdjustmentTransaction
{
    public async Task<IStockAdjustmentScope?> BeginAsync(Guid tenantId, Guid inventoryLotId, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        try
        {
            var lot = await context.InventoryLots.FromSqlInterpolated(
                $"SELECT l.* FROM inventory_lots AS l WHERE l.tenant_id = {tenantId} AND l.id = {inventoryLotId} FOR UPDATE")
                .AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (lot is null)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
                return null;
            }
            // Licensing is read after the lot lock wait; it is never the stock serialization lock.
            var license = await context.Licenses.AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new ApplicationErrorException(ApplicationErrors.LicenseNotFound);
            foreach (var entry in context.ChangeTracker.Entries<InventoryLot>().Where(value => value.Entity.Id == lot.Id).ToArray())
                entry.State = EntityState.Detached;
            context.InventoryLots.Attach(lot);
            return new AdjustmentScope(context, transaction, lot, license);
        }
        catch
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();
            throw;
        }
    }

    private sealed class AdjustmentScope(MediPosDbContext context, IDbContextTransaction transaction, InventoryLot lot, License license)
        : IStockAdjustmentScope
    {
        private bool _committed;
        public InventoryLot Lot => lot;
        public bool AllowsOperation(DateTimeOffset at) => license.TenantId == lot.TenantId && license.AllowsOperation(at);
        public async Task CompleteAsync(StockMovement adjustment, AuditLog audit, CancellationToken cancellationToken)
        {
            if (_committed || adjustment.MovementType != StockMovementType.Adjustment)
                throw new InvalidOperationException("Only a single adjustment may complete this transaction.");
            context.ValidateAudit(audit, lot.TenantId, AuditAction.InventoryAdjusted, lot.Id);
            lot.ApplyAdjustment(adjustment);
            context.StockMovements.Add(adjustment);
            context.AddAudit(audit, lot.TenantId, AuditAction.InventoryAdjusted, lot.Id);
            await context.SaveAuditedChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _committed = true;
        }
        public async ValueTask DisposeAsync()
        {
            try { await transaction.DisposeAsync().ConfigureAwait(false); }
            finally { if (!_committed) context.ChangeTracker.Clear(); }
        }
    }
}
