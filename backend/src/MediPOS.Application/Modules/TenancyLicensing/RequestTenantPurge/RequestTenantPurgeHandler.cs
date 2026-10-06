namespace MediPOS.Application.Modules.TenancyLicensing.RequestTenantPurge;

public sealed class RequestTenantPurgeHandler(ITenancyLicensingStore store, TimeProvider timeProvider)
{
    public async Task<LicenseDetails> HandleAsync(RequestTenantPurgeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var license = await store.FindLicenseAsync(command.TenantId, command.LicenseId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("License was not found for the tenant.");

        license.RequestPurge(command.ActorId, timeProvider.GetUtcNow());
        await store.SaveLicenseAsync(license, cancellationToken).ConfigureAwait(false);

        return LicenseDetails.From(license);
    }
}
