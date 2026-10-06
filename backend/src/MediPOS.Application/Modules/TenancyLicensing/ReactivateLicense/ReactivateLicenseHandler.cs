using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Domain.Modules.AuditSupport;

namespace MediPOS.Application.Modules.TenancyLicensing.ReactivateLicense;

public sealed class ReactivateLicenseHandler(ITenancyLicensingStore store, TimeProvider timeProvider)
{
    public async Task<LicenseDetails> HandleAsync(ReactivateLicenseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        AuditTrail.RequireActor(command.ActorId);
        var license = await store.FindLicenseAsync(command.TenantId, command.LicenseId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.LicenseNotFound);
        var now = timeProvider.GetUtcNow();
        var before = AuditTrail.LicenseState(license);
        var count = license.Changes.Count;
        try { license.Reactivate(command.Status, command.ActorId, now); }
        catch (ArgumentException) { throw new ApplicationErrorException(ApplicationErrors.InvalidRequest); }
        catch (InvalidOperationException) { throw new ApplicationErrorException(ApplicationErrors.LicenseStateConflict); }
        if (license.Changes.Count != count)
        {
            var audit = AuditTrail.Record(command.TenantId, command.ActorId, AuditAction.LicenseReactivated, license.Id, now, before, AuditTrail.LicenseState(license));
            await store.SaveLicenseAsync(license, audit, cancellationToken).ConfigureAwait(false);
        }
        return LicenseDetails.From(license);
    }
}
