using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.Transfers;

namespace MediPOS.Application.Modules.Transfers;

internal static class TransferAccess
{
    public static async Task<AccessContext> TenantAsync(ResolveAccessContextHandler resolver, Guid tenantId, DateTimeOffset now, CancellationToken token)
    {
        if (tenantId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var result = await resolver.HandleAsync(tenantId, null, now, token).ConfigureAwait(false);
        // branch_required is emitted only after authenticated membership and license have been validated.
        if (result.Context is null || (!result.IsAllowed && result.Code != "access.branch_required")) throw Denied(result.Code);
        return result.Context;
    }
    public static async Task<AccessContext> BranchAsync(ResolveAccessContextHandler resolver, Guid tenantId, Guid branchId, DateTimeOffset now, CancellationToken token)
    {
        var result = await resolver.HandleAsync(tenantId, branchId, now, token).ConfigureAwait(false);
        if (!result.IsAllowed || result.Context is null) throw Denied(result.Code);
        return result.Context;
    }
    // MVP policy specified by the task from current roles; SPEC does not enumerate this permission matrix.
    public static async Task<AccessContext> ActionAsync(ResolveAccessContextHandler resolver, TransferSnapshot snapshot,
        TransferEventType? action, DateTimeOffset now, CancellationToken token)
    {
        var t = snapshot.Transfer;
        var tenant = await TenantAsync(resolver, t.TenantId, now, token).ConfigureAwait(false);
        var requester = snapshot.Events.SingleOrDefault(e => e.EventType == TransferEventType.Requested)?.ActorId;
        var destination = action is TransferEventType.Requested or TransferEventType.Received ||
            (action == TransferEventType.Cancelled && t.Status == TransferStatus.Requested && tenant.UserId == requester && tenant.Role != TenantRole.Owner);
        var branch = destination ? t.DestinationBranchId : t.SourceBranchId;
        if (action == TransferEventType.Cancelled && destination)
        {
            var requestAccess = await resolver.HandleAsync(t.TenantId, t.DestinationBranchId, now, token).ConfigureAwait(false);
            if (!requestAccess.IsAllowed) branch = t.SourceBranchId; // A Pharmacist requester may still reject as source operator.
        }
        if (action is null)
        {
            var source = await resolver.HandleAsync(t.TenantId, t.SourceBranchId, now, token).ConfigureAwait(false);
            if (source.IsAllowed && source.Context is not null) return source.Context;
            return await BranchAsync(resolver, t.TenantId, t.DestinationBranchId, now, token).ConfigureAwait(false);
        }
        var access = await BranchAsync(resolver, t.TenantId, branch, now, token).ConfigureAwait(false);
        var allowed = action switch
        {
            TransferEventType.Requested => access.Role is TenantRole.Owner or TenantRole.Pharmacist or TenantRole.Cashier,
            TransferEventType.Approved or TransferEventType.Dispatched or TransferEventType.Received => access.Role is TenantRole.Owner or TenantRole.Pharmacist,
            TransferEventType.Cancelled => access.Role == TenantRole.Owner ||
                (t.Status == TransferStatus.Requested && access.UserId == requester && branch == t.DestinationBranchId) ||
                (access.Role == TenantRole.Pharmacist && branch == t.SourceBranchId),
            _ => false,
        };
        if (!allowed) throw new ApplicationErrorException(TransferErrors.Forbidden);
        return access;
    }
    public static void Validate(TransferSnapshot snapshot)
    {
        try { TransferHistory.Validate(snapshot.Transfer, snapshot.Events, snapshot.Allocations); }
        catch (Exception error) when (error is ArgumentException or ArithmeticException or InvalidOperationException)
        { throw new ApplicationErrorException(TransferErrors.CorruptedHistory); }
    }
    private static ApplicationErrorException Denied(string code) => new(new ApplicationError(code, ErrorCategory.Forbidden, "Operational access was denied."));
}
