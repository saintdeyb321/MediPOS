using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.AuditSupport;

namespace MediPOS.Application.Modules.Catalog.SetBusinessProductStatus;

// TenantId and ActorId are supplied only by an authenticated server caller.
public sealed record SetBusinessProductStatusCommand(Guid TenantId, Guid ProductId, bool IsActive, Guid ActorId);

public sealed class SetBusinessProductStatusHandler(IBusinessProductStore store, ITenantLicenseProvisioning provisioning, TimeProvider timeProvider)
{
    public async Task<BusinessProductDetails> HandleAsync(SetBusinessProductStatusCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        AuditTrail.RequireActor(command.ActorId);
        if (command.TenantId == Guid.Empty || command.ProductId == Guid.Empty)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await using var scope = await provisioning.BeginAsync(command.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.LicenseNotFound);
        if (scope.TenantId != command.TenantId)
            throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        var now = timeProvider.GetUtcNow();
        if (!scope.AllowsOperation(now))
            throw new ApplicationErrorException(ApplicationErrors.LicenseDenied);
        var product = await store.FindAsync(command.TenantId, command.ProductId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(CatalogErrors.BusinessProductNotFound);
        if (product.TenantId != command.TenantId)
            throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        var before = CatalogAudit.Status(product);
        if (product.SetStatus(command.IsActive))
        {
            var audit = AuditTrail.Record(command.TenantId, command.ActorId, AuditAction.BusinessProductStatusChanged, product.Id, now,
                before, CatalogAudit.Status(product));
            await store.SaveAsync(product, audit, cancellationToken).ConfigureAwait(false);
        }
        await scope.CompleteAsync(cancellationToken).ConfigureAwait(false);
        return BusinessProductDetails.From(product);
    }
}
