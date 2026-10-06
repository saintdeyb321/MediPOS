using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.Application.Modules.TenancyLicensing.CreateTenant;

public sealed class CreateTenantHandler(ITenancyLicensingStore store, TimeProvider timeProvider)
{
    public async Task<LicenseDetails> HandleAsync(CreateTenantCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenant = Tenant.Create(command.TradingName, command.StartsAt, command.ExpiresAt,
            command.MaxBranches, command.Status, command.ActorId, timeProvider.GetUtcNow());

        await store.CreateTenantAsync(tenant, cancellationToken).ConfigureAwait(false);

        return LicenseDetails.From(tenant.License);
    }
}
