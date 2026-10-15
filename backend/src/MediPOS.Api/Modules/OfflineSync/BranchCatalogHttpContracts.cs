using System.Text.Json.Serialization;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.OfflineSync;
using MediPOS.Application.Tenancy;

namespace MediPOS.Api.Modules.OfflineSync;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BranchCatalogRequest(Guid DeviceId, string? Cursor = null, int PageSize = 100);

// Mapped by the opt-in TenantApi. Device is a selection, not a credential; the handler rechecks ownership.
public static class BranchCatalogHttpMapping
{
    public static GetBranchCatalogSnapshotCommand Snapshot(BranchCatalogRequest request, ITenantDataContext serverContext)
    { ArgumentNullException.ThrowIfNull(request); return new(Tenant(serverContext), request.DeviceId, request.Cursor, request.PageSize); }
    public static GetBranchCatalogChangesCommand Changes(BranchCatalogRequest request, ITenantDataContext serverContext)
    { ArgumentNullException.ThrowIfNull(request); return new(Tenant(serverContext), request.DeviceId, request.Cursor, request.PageSize); }
    private static Guid Tenant(ITenantDataContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.TenantId is { } tenant && tenant != Guid.Empty ? tenant : throw new ApplicationErrorException(new("sync.tenant_context_required", ErrorCategory.Forbidden, "An authenticated server tenant context is required."));
    }
}
