using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.Application.Modules.TenancyLicensing.CreateTenant;

public sealed class CreateTenantHandler(ITenancyLicensingStore store, TimeProvider timeProvider)
{
    public async Task<LicenseDetails> HandleAsync(CreateTenantCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        AuditTrail.RequireActor(command.ActorId);
        var now = timeProvider.GetUtcNow();
        Tenant tenant;
        try
        {
            tenant = Tenant.Create(command.TradingName, command.StartsAt, command.ExpiresAt,
                command.MaxBranches, command.Status, command.ActorId, now);
        }
        catch (ArgumentException) { throw new ApplicationErrorException(ApplicationErrors.InvalidRequest); }
        var audit = AuditTrail.Record(tenant.Id, command.ActorId, AuditAction.TenantCreated, tenant.Id, now, null, AuditTrail.TenantCreated(tenant));
        await store.CreateTenantAsync(tenant, audit, cancellationToken).ConfigureAwait(false);
        return LicenseDetails.From(tenant.License);
    }
}
