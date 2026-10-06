using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.Branches;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.Purchasing;

namespace MediPOS.Application.Modules.Purchasing.CreatePurchase;

public sealed record CreatePurchaseCommand(Guid TenantId, Guid BranchId, Guid? SupplierId, string? DocumentReference, Guid ActorId);

public sealed class CreatePurchaseHandler(IPurchasingStore store, IBranchesStore branches,
    ITenantLicenseProvisioning provisioning, TimeProvider clock)
{
    public async Task<PurchaseDetails> HandleAsync(CreatePurchaseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        AuditTrail.RequireActor(command.ActorId);
        if (command.BranchId == Guid.Empty || command.SupplierId == Guid.Empty)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await using var scope = await PurchasingValidation.BeginAsync(command.TenantId, provisioning, clock, cancellationToken).ConfigureAwait(false);
        await PurchasingValidation.ReferencesAsync(command.TenantId, command.BranchId, command.SupplierId, branches, store, cancellationToken).ConfigureAwait(false);
        Purchase purchase;
        try { purchase = Purchase.Create(command.TenantId, command.BranchId, command.SupplierId, command.DocumentReference, command.ActorId, clock.GetUtcNow()); }
        catch (ArgumentException) { throw new ApplicationErrorException(ApplicationErrors.InvalidRequest); }
        await store.AddPurchaseAsync(purchase, cancellationToken).ConfigureAwait(false);
        await scope.CompleteAsync(cancellationToken).ConfigureAwait(false);
        return PurchaseDetails.From(purchase);
    }
}
