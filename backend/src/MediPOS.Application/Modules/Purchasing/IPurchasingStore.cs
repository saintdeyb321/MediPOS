using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Purchasing;

namespace MediPOS.Application.Modules.Purchasing;

// Commands and queries use the authenticated server tenant. Callers enforce membership/role,
// branch and schedule authorization before invoking these internal use cases.
public interface IPurchasingStore
{
    Task<Supplier?> FindSupplierAsync(Guid tenantId, Guid supplierId, CancellationToken cancellationToken);
    Task AddSupplierAsync(Supplier supplier, CancellationToken cancellationToken);
    Task AddPurchaseAsync(Purchase purchase, CancellationToken cancellationToken);
    Task<Purchase?> LockPurchaseAsync(Guid tenantId, Guid purchaseId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PurchaseLine>> FindLinesAsync(Guid tenantId, Guid purchaseId, CancellationToken cancellationToken);
    Task ReplaceLinesAsync(Purchase purchase, IReadOnlyList<PurchaseLine> lines, CancellationToken cancellationToken);
    Task SaveConfirmationAsync(Purchase purchase, AuditLog audit, CancellationToken cancellationToken);
    Task<IReadOnlyList<Purchase>> FindByDocumentReferenceAsync(Guid tenantId, string reference, CancellationToken cancellationToken);
}

public sealed record PurchaseDetails(Guid Id, Guid BranchId, Guid? SupplierId, string? DocumentReference,
    string Status, DateTimeOffset CreatedAt, DateTimeOffset? ConfirmedAt, Guid CreatedByActorId, Guid? ConfirmedByActorId)
{
    public static PurchaseDetails From(Purchase purchase) => new(purchase.Id, purchase.BranchId, purchase.SupplierId,
        purchase.DocumentReference, PurchaseStatusCodes.ToCode(purchase.Status), purchase.CreatedAt,
        purchase.ConfirmedAt, purchase.CreatedByActorId, purchase.ConfirmedByActorId);
}
