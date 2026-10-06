namespace MediPOS.Application.Modules.TenancyLicensing;

// Serializes tenant provisioning with current license changes; all participating stores share the scoped transaction.
public interface ITenantLicenseProvisioning
{
    Task<ITenantLicenseProvisioningScope?> BeginAsync(Guid tenantId, CancellationToken cancellationToken);
}

public interface ITenantLicenseProvisioningScope : IAsyncDisposable
{
    Guid TenantId { get; }
    int MaxBranches { get; }
    bool AllowsOperation(DateTimeOffset at);
    Task CompleteAsync(CancellationToken cancellationToken);
}
