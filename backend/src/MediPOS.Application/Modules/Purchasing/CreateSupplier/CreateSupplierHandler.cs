using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.Purchasing;

namespace MediPOS.Application.Modules.Purchasing.CreateSupplier;

// Tenant/actor must come from authenticated server context; administrative authorization belongs to the caller.
public sealed record CreateSupplierCommand(Guid TenantId, string Name, string? Ruc, string? Contact, Guid ActorId);
public sealed record SupplierDetails(Guid Id, string Name, string? Ruc, string? Contact, bool IsActive, DateTimeOffset CreatedAt);

public sealed class CreateSupplierHandler(IPurchasingStore store, ITenantLicenseProvisioning provisioning, TimeProvider clock)
{
    public async Task<SupplierDetails> HandleAsync(CreateSupplierCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        AuditTrail.RequireActor(command.ActorId);
        await using var scope = await PurchasingValidation.BeginAsync(command.TenantId, provisioning, clock, cancellationToken).ConfigureAwait(false);
        Supplier supplier;
        try { supplier = Supplier.Create(command.TenantId, command.Name, command.Ruc, command.Contact, clock.GetUtcNow()); }
        catch (ArgumentException) { throw new ApplicationErrorException(ApplicationErrors.InvalidRequest); }
        await store.AddSupplierAsync(supplier, cancellationToken).ConfigureAwait(false);
        await scope.CompleteAsync(cancellationToken).ConfigureAwait(false);
        return new(supplier.Id, supplier.Name, supplier.Ruc, supplier.Contact, supplier.IsActive, supplier.CreatedAt);
    }
}
