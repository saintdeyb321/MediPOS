using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Branches;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.Inventory;

namespace MediPOS.Application.Modules.Inventory.GetExpiringLots;

public sealed record GetExpiringLotsQuery(Guid TenantId, Guid? BranchId = null);
public sealed record ExpiringLotDetails(Guid InventoryLotId, Guid BranchId, Guid BusinessProductId, string ProductName,
    decimal QuantityAvailableBase, DateOnly ExpirationDate, decimal? CostValue, decimal PotentialSaleValue)
{
    public static ExpiringLotDetails From(ExpiringLotSource source, MediPOS.Domain.Modules.IdentityAccess.TenantRole role) => new(source.InventoryLotId, source.BranchId, source.BusinessProductId, source.ProductName,
        source.QuantityAvailableBase, source.ExpirationDate, role == MediPOS.Domain.Modules.IdentityAccess.TenantRole.Owner ? InventoryValuation.CostValue(source.QuantityAvailableBase, source.UnitCost, source.ConversionToBaseSnapshot) : null,
        InventoryValuation.PotentialSaleValue(source.QuantityAvailableBase, source.RetailPrice));
}

public sealed class GetExpiringLotsHandler(IInventoryReadStore inventory, IBranchesStore branches, TimeProvider clock, ITenantOperationAuthorizer authorization)
{
    public async Task<IReadOnlyList<ExpiringLotDetails>> HandleAsync(GetExpiringLotsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var access = await authorization.RequireAsync(query.TenantId, query.BranchId, null, TenantOperation.Inventory, cancellationToken).ConfigureAwait(false);
        if (query.BranchId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await InventoryAccess.RequireLicenseAsync(query.TenantId, inventory, clock, cancellationToken).ConfigureAwait(false);
        if (query.BranchId.HasValue)
        {
            var branch = await branches.FindBranchAsync(query.TenantId, query.BranchId.Value, cancellationToken).ConfigureAwait(false)
                ?? throw new ApplicationErrorException(ApplicationErrors.BranchNotFound);
            if (branch.TenantId != query.TenantId) throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        }
        var today = InventoryCalendar.Today(clock);
        var sources = await inventory.FindExpiringLotsAsync(query.TenantId, query.BranchId, today, today.AddDays(30), cancellationToken).ConfigureAwait(false);
        if (sources.Any(value => value.TenantId != query.TenantId || (query.BranchId.HasValue && value.BranchId != query.BranchId)))
            throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        access = await authorization.RequireAsync(query.TenantId, query.BranchId, null, TenantOperation.Inventory, cancellationToken).ConfigureAwait(false);
        try
        {
            return sources.Where(value => InventoryCalendar.IsExpiring(value.ExpirationDate, value.QuantityAvailableBase, today))
                .Select(value => ExpiringLotDetails.From(value, access.Role)).ToArray();
        }
        catch (ArithmeticException) { throw new ApplicationErrorException(InventoryErrors.ValuationOutOfRange); }
    }
}
