namespace MediPOS.Application.Modules.TenancyLicensing.SuspendLicense;

public sealed class SuspendLicenseHandler(ITenancyLicensingStore store, TimeProvider timeProvider)
{
    public async Task<LicenseDetails> HandleAsync(SuspendLicenseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var license = await store.FindLicenseAsync(command.TenantId, command.LicenseId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("License was not found for the tenant.");

        license.Suspend(command.ActorId, timeProvider.GetUtcNow());
        await store.SaveLicenseAsync(license, cancellationToken).ConfigureAwait(false);

        return LicenseDetails.From(license);
    }
}
