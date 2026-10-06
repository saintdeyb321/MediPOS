namespace MediPOS.Application.Modules.TenancyLicensing.ReactivateLicense;

public sealed class ReactivateLicenseHandler(ITenancyLicensingStore store, TimeProvider timeProvider)
{
    public async Task<LicenseDetails> HandleAsync(ReactivateLicenseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var license = await store.FindLicenseAsync(command.TenantId, command.LicenseId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("License was not found for the tenant.");

        license.Reactivate(command.Status, command.ActorId, timeProvider.GetUtcNow());
        await store.SaveLicenseAsync(license, cancellationToken).ConfigureAwait(false);

        return LicenseDetails.From(license);
    }
}
