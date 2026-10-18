using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.Authentication;

namespace MediPOS.Application.Modules.TenancyLicensing;

public sealed record PlatformTenantSummary(Guid TenantId, string TradingName, DateTimeOffset CreatedAt);
public sealed record PlatformTenantPage(IReadOnlyList<PlatformTenantSummary> Items, Guid? NextAfterTenantId);
public sealed record PlatformTenantDetails(PlatformTenantSummary Tenant, LicenseDetails License, Guid? InitialOwnerUserId);
public sealed record PlatformGoogleIdentity(Guid UserId, string DisplayName, string Email);

// Exact identity lookup only; never a global customer, employee or commercial-data directory.
public interface IPlatformDirectoryReader
{
    Task<PlatformTenantPage> TenantsAsync(Guid? afterTenantId, int limit, CancellationToken token);
    Task<PlatformTenantDetails?> TenantAsync(Guid tenantId, CancellationToken token);
    Task<PlatformGoogleIdentity?> GoogleIdentityAsync(Guid userId, CancellationToken token);
    Task<LicenseDetails?> CreationResultAsync(Guid actorId, Guid requestKey, CancellationToken token);
}

public sealed class PlatformDirectoryQueries(IPlatformDirectoryReader reader, IPlatformAdministration administration)
{
    public async Task<PlatformTenantPage> TenantsAsync(Guid? afterTenantId, int limit, CancellationToken token)
    {
        await PlatformAdministrationAccess.RequireAsync(administration, null, token).ConfigureAwait(false);
        if (afterTenantId == Guid.Empty || limit is < 1 or > 100) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var page = await reader.TenantsAsync(afterTenantId, limit, token).ConfigureAwait(false);
        await PlatformAdministrationAccess.RequireAsync(administration, null, token).ConfigureAwait(false);
        return page;
    }

    public async Task<PlatformTenantDetails> TenantAsync(Guid tenantId, CancellationToken token)
    {
        await PlatformAdministrationAccess.RequireAsync(administration, null, token).ConfigureAwait(false);
        if (tenantId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var result = await reader.TenantAsync(tenantId, token).ConfigureAwait(false);
        await PlatformAdministrationAccess.RequireAsync(administration, null, token).ConfigureAwait(false);
        return result ?? throw new ApplicationErrorException(ApplicationErrors.LicenseNotFound);
    }

    public async Task<PlatformGoogleIdentity> GoogleIdentityAsync(Guid userId, CancellationToken token)
    {
        await PlatformAdministrationAccess.RequireAsync(administration, null, token).ConfigureAwait(false);
        if (userId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var result = await reader.GoogleIdentityAsync(userId, token).ConfigureAwait(false);
        await PlatformAdministrationAccess.RequireAsync(administration, null, token).ConfigureAwait(false);
        return result ?? throw new ApplicationErrorException(new("platform.identity_not_found", ErrorCategory.NotFound, "A previously registered Google identity is required."));
    }

    public async Task<LicenseDetails> CreationResultAsync(Guid requestKey, CancellationToken token)
    {
        var actor = await PlatformAdministrationAccess.RequireAsync(administration, null, token).ConfigureAwait(false);
        if (requestKey == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var result = await reader.CreationResultAsync(actor, requestKey, token).ConfigureAwait(false);
        await PlatformAdministrationAccess.RequireAsync(administration, actor, token).ConfigureAwait(false);
        return result ?? throw new ApplicationErrorException(new("platform.request_not_found", ErrorCategory.NotFound, "No committed result is visible; do not assume that a pending request failed."));
    }
}
