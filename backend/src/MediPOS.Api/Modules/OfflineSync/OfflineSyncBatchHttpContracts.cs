using System.Text.Json.Serialization;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.OfflineSync;
using MediPOS.Application.Tenancy;

namespace MediPOS.Api.Modules.OfflineSync;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OfflineSyncBatchRequest(Guid DeviceId, int ProtocolVersion,
    IReadOnlyList<SyncEventEnvelope>? Events, Guid? BatchRequestId = null);

// Published only by the opt-in TenantApi behind real Google session, CSRF and resource admission.
// Route selection grants no authority; the application revalidates the authenticated user/device/branch.
// The response contract is OfflineSyncBatchResult; it contains no event payload or unauthorized identity metadata.
public static class OfflineSyncBatchHttpMapping
{
    public const int MaximumBodyBytes = OfflineSyncBatchPolicy.MaximumRequestBytes;

    public static OfflineSyncBatchCommand ToCommand(OfflineSyncBatchRequest request, ITenantDataContext serverContext)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(serverContext);
        if (!serverContext.TenantId.HasValue || serverContext.TenantId == Guid.Empty)
            throw new ApplicationErrorException(new("sync.tenant_context_required", ErrorCategory.Forbidden, "An authenticated server tenant context is required."));
        // Selection alone never authorizes: ProcessOfflineSyncBatchHandler still resolves the real server session.
        return new(serverContext.TenantId.Value, request.DeviceId, request.ProtocolVersion, request.Events, request.BatchRequestId);
    }
}
