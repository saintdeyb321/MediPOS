using MediPOS.Application.Errors;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.Application.Modules.TenancyLicensing.CreateTenant;

// Admission policy for administrative creation, not the general license lifecycle factory.
public static class TenantCreationStatusPolicy
{
    public static readonly ApplicationError InvalidStatus = new("tenant.initial_status_invalid", ErrorCategory.Validation,
        "The initial tenant license status is not allowed.");

    public static void RequireAllowed(LicenseStatus status)
    {
        // Suspended permits provisioning without operational access. Terminal/deletion states
        // require their lifecycle processes and must never be an initial administrative choice.
        if (status is not (LicenseStatus.Trial or LicenseStatus.Active or LicenseStatus.Grace or LicenseStatus.Suspended))
            throw new ApplicationErrorException(InvalidStatus);
    }
}
