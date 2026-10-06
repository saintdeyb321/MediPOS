using MediPOS.Application.Errors;

namespace MediPOS.Application.Modules.Inventory;

public static class InventoryErrors
{
    public static readonly ApplicationError LotNotFound = new("inventory.lot_not_found", ErrorCategory.NotFound, "Inventory lot not found.");
    public static readonly ApplicationError InsufficientStock = new("inventory.insufficient_stock", ErrorCategory.Conflict, "Insufficient available stock.");
    public static readonly ApplicationError MedicineRequired = new("inventory.fefo_requires_medicine", ErrorCategory.Validation, "FEFO planning requires a medicine product.");
    public static readonly ApplicationError ValuationOutOfRange = new("inventory.valuation_out_of_range", ErrorCategory.Validation, "Valuation exceeds decimal precision.");
}

internal static class InventoryAccess
{
    public static async Task RequireLicenseAsync(Guid tenantId, IInventoryReadStore store, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var license = await store.FindLicenseAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.LicenseNotFound);
        if (license.TenantId != tenantId) throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        if (!license.AllowsOperation(clock.GetUtcNow())) throw new ApplicationErrorException(ApplicationErrors.LicenseDenied);
    }
}

public static class InventoryCalendar
{
    private static readonly TimeZoneInfo Lima = TimeZoneInfo.FindSystemTimeZoneById("America/Lima");
    public static DateOnly Today(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Lima).DateTime);
    }
    public static bool IsExpiring(DateOnly? expiration, decimal available, DateOnly today) =>
        available > 0 && expiration >= today && expiration <= today.AddDays(30);
}
