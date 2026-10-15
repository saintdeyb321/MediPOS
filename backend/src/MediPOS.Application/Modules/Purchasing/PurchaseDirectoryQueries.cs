using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.Purchasing.CreateSupplier;
using MediPOS.Application.Modules.Purchasing.ReplacePurchaseLines;

namespace MediPOS.Application.Modules.Purchasing;

public sealed record PurchaseView(PurchaseDetails Purchase, IReadOnlyList<PurchaseLineDetails> Lines);
public interface IPurchaseDirectoryReader
{
    Task<IReadOnlyList<SupplierDetails>> SuppliersAsync(Guid tenantId, int offset, int limit, CancellationToken token);
    Task<PurchaseView?> PurchaseAsync(Guid tenantId, Guid branchId, Guid purchaseId, CancellationToken token);
}
public sealed class PurchaseDirectoryQueries(IPurchaseDirectoryReader reader, ITenantOperationAuthorizer authorization)
{
    public async Task<IReadOnlyList<SupplierDetails>> SuppliersAsync(Guid tenantId, Guid branchId, int offset, int limit, CancellationToken token)
    {
        if (offset is < 0 or > 10_000 || limit is < 1 or > 100) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await authorization.RequireAsync(tenantId, branchId, null, TenantOperation.Purchasing, token).ConfigureAwait(false);
        var result = await reader.SuppliersAsync(tenantId, offset, limit, token).ConfigureAwait(false);
        await authorization.RequireAsync(tenantId, branchId, null, TenantOperation.Purchasing, token).ConfigureAwait(false);
        return result;
    }
    public async Task<PurchaseView> PurchaseAsync(Guid tenantId, Guid branchId, Guid purchaseId, CancellationToken token)
    {
        if (purchaseId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await authorization.RequireAsync(tenantId, branchId, null, TenantOperation.Purchasing, token).ConfigureAwait(false);
        var result = await reader.PurchaseAsync(tenantId, branchId, purchaseId, token).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(new("purchase.not_found", ErrorCategory.NotFound, "Purchase was not found."));
        await authorization.RequireAsync(tenantId, branchId, null, TenantOperation.Purchasing, token).ConfigureAwait(false);
        return result;
    }
}
