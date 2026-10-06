using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Branches;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Inventory;

namespace MediPOS.Application.Modules.Inventory.PlanFefoAllocation;

public sealed record PlanFefoAllocationQuery(Guid TenantId, Guid BranchId, Guid BusinessProductId, decimal RequestedBaseQuantity);

public sealed class PlanFefoAllocationHandler(IInventoryReadStore inventory, IBranchesStore branches, IBusinessProductStore products, TimeProvider clock)
{
    public async Task<IReadOnlyList<LotAllocation>> HandleAsync(PlanFefoAllocationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.BranchId == Guid.Empty || query.BusinessProductId == Guid.Empty || query.RequestedBaseQuantity <= 0)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await InventoryAccess.RequireLicenseAsync(query.TenantId, inventory, clock, cancellationToken).ConfigureAwait(false);
        var branch = await branches.FindBranchAsync(query.TenantId, query.BranchId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.BranchNotFound);
        var product = await products.FindAsync(query.TenantId, query.BusinessProductId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(CatalogErrors.BusinessProductNotFound);
        if (branch.TenantId != query.TenantId || product.TenantId != query.TenantId)
            throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        if (product.ProductType != ProductType.Medicine) throw new ApplicationErrorException(InventoryErrors.MedicineRequired);
        var today = InventoryCalendar.Today(clock);
        var lots = await inventory.FindAvailableMedicineLotsAsync(query.TenantId, query.BranchId, query.BusinessProductId, today, cancellationToken).ConfigureAwait(false);
        if (lots.Any(value => value.TenantId != query.TenantId || value.BranchId != query.BranchId || value.BusinessProductId != query.BusinessProductId))
            throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        try { return FefoAllocation.Plan(lots, query.RequestedBaseQuantity, today); }
        catch (InsufficientStockException) { throw new ApplicationErrorException(InventoryErrors.InsufficientStock); }
        catch (ArithmeticException) { throw new ApplicationErrorException(ApplicationErrors.InvalidRequest); }
    }
}
