using System.Text.Json.Serialization;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.OfflineSync;
using MediPOS.Application.Tenancy;

namespace MediPOS.Api.Modules.OfflineSync;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResolveOfflineSaleConflictRequest(string ResolutionCode, string Note);

// Prepared mappings; no public route until a real authenticated server session exists.
public static class OfflineSaleConflictHttpMapping
{
    public static GetOfflineSaleConflictsCommand Query(OfflineSaleConflictFilter filter, ITenantDataContext serverContext) => new(Tenant(serverContext), filter);
    public static ResolveOfflineSaleConflictCommand Resolve(Guid conflictId, ResolveOfflineSaleConflictRequest request, ITenantDataContext serverContext)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new(Tenant(serverContext), conflictId, request.ResolutionCode, request.Note);
    }
    private static Guid Tenant(ITenantDataContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.TenantId is { } tenant && tenant != Guid.Empty ? tenant :
            throw new ApplicationErrorException(new("sync.tenant_context_required", ErrorCategory.Forbidden, "An authenticated server tenant context is required."));
    }
}
