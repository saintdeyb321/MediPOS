using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Domain.Modules.AuditSupport;

namespace MediPOS.Application.Modules.TenancyLicensing.RenewLicense;

public sealed class RenewLicenseHandler(ITenancyLicensingStore store, TimeProvider timeProvider)
{
    public async Task<LicenseDetails> HandleAsync(RenewLicenseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        AuditTrail.RequireActor(command.ActorId);
        var license = await store.FindLicenseAsync(command.TenantId, command.LicenseId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.LicenseNotFound);
        var now = timeProvider.GetUtcNow();
        var before = AuditTrail.LicenseState(license);
        var count = license.Changes.Count;
        try { license.Renew(command.ExpiresAt, command.ActorId, now); }
        catch (ArgumentException) { throw new ApplicationErrorException(ApplicationErrors.InvalidRequest); }
        catch (InvalidOperationException) { throw new ApplicationErrorException(ApplicationErrors.LicenseStateConflict); }
        if (license.Changes.Count != count)
        {
            var audit = AuditTrail.Record(command.TenantId, command.ActorId, AuditAction.LicenseRenewed, license.Id, now, before, AuditTrail.LicenseState(license));
            await store.SaveLicenseAsync(license, audit, cancellationToken).ConfigureAwait(false);
        }
        return LicenseDetails.From(license);
    }
}
