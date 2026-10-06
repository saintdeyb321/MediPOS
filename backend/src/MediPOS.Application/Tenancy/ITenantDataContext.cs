using MediPOS.Application.Errors;

namespace MediPOS.Application.Tenancy;

// Data selection only: selecting a tenant does not authenticate a user or authorize an operation.
public interface ITenantDataContext
{
    Guid? TenantId { get; }
    void SelectTenant(Guid tenantId);
}

public sealed class TenantDataContext : ITenantDataContext
{
    private readonly Lock _gate = new();
    private Guid? _tenantId;

    public Guid? TenantId
    {
        get { lock (_gate) return _tenantId; }
    }

    public void SelectTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        lock (_gate)
        {
            if (_tenantId.HasValue && _tenantId != tenantId)
                throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
            _tenantId = tenantId;
        }
    }
}
