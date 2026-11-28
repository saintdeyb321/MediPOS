using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.Application.Modules.TenancyLicensing.RenewLicense;

public sealed class RenewLicenseHandler(ITenancyLicensingStore store, TimeProvider timeProvider, IPlatformAdministration administration)
{
    public async Task<LicenseDetails> HandleAsync(RenewLicenseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        AuditTrail.RequireActor(command.ActorId);
        await PlatformAdministrationAccess.RequireAsync(administration, command.ActorId, cancellationToken).ConfigureAwait(false);
        var license = await store.FindLicenseAsync(command.TenantId, command.LicenseId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.LicenseNotFound);
        var now = timeProvider.GetUtcNow();
        var before = AuditTrail.LicenseState(license);
        var count = license.Changes.Count;
        try
        {
            var expiration = command.ExpiresAt;
            if (command.TermMonths is { } months)
            {
                if (!LicenseTerm.IsPlan(months)) throw new ArgumentOutOfRangeException(nameof(command));
                // In a term-based request ExpiresAt is the previously observed value.
                if (license.ExpiresAt != command.ExpiresAt) throw new ApplicationErrorException(ApplicationErrors.LicenseConcurrency);
                var anchor = license.ExpiresAt > now ? license.ExpiresAt : now;
                // Match timestamptz precision for the response and next optimistic precondition.
                anchor = new DateTimeOffset(anchor.UtcTicks - anchor.UtcTicks % 10, TimeSpan.Zero);
                expiration = LicenseTerm.Expiration(anchor, months);
            }
            license.Renew(expiration, command.ActorId, now);
        }
        catch (ArgumentException) { throw new ApplicationErrorException(ApplicationErrors.InvalidRequest); }
        catch (InvalidOperationException) { throw new ApplicationErrorException(ApplicationErrors.LicenseStateConflict); }
        if (license.Changes.Count != count)
        {
            var audit = AuditTrail.Record(command.TenantId, command.ActorId, AuditAction.LicenseRenewed, license.Id, now, before, AuditTrail.LicenseState(license));
            await PlatformAdministrationAccess.RequireAsync(administration, command.ActorId, cancellationToken).ConfigureAwait(false);
            await store.SaveLicenseAsync(license, audit, cancellationToken).ConfigureAwait(false);
        }
        return LicenseDetails.From(license);
    }
}
