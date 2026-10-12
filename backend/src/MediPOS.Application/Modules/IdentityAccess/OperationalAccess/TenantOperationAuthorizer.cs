using MediPOS.Application.Errors;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.Application.Modules.IdentityAccess.OperationalAccess;

public enum TenantOperation { Administration, Purchasing, Inventory, ProductSearch }

// Authorization for the foundational use cases. Lookup authenticates before resolving a record's branch;
// it is never sufficient to read its details or mutate it: Require must check that branch afterwards.
public interface ITenantOperationAuthorizer
{
    Task<AccessContext> RequireAsync(Guid tenantId, Guid? branchId, Guid? actorId, TenantOperation operation, CancellationToken token);
    Task<AccessContext> IdentifyForBranchLookupAsync(Guid tenantId, Guid? actorId, TenantOperation operation, CancellationToken token);
}

public sealed class TenantOperationAuthorizer(ResolveAccessContextHandler resolver, TimeProvider clock) : ITenantOperationAuthorizer
{
    public Task<AccessContext> RequireAsync(Guid tenantId, Guid? branchId, Guid? actorId, TenantOperation operation, CancellationToken token) =>
        ResolveAsync(tenantId, branchId, actorId, operation, false, token);

    public Task<AccessContext> IdentifyForBranchLookupAsync(Guid tenantId, Guid? actorId, TenantOperation operation, CancellationToken token) =>
        ResolveAsync(tenantId, null, actorId, operation, true, token);

    private async Task<AccessContext> ResolveAsync(Guid tenantId, Guid? branchId, Guid? actorId, TenantOperation operation, bool lookup, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(operation) || branchId == Guid.Empty)
            throw Denied("access.operation_denied");
        var result = await resolver.HandleAsync(tenantId, branchId, clock.GetUtcNow(), token).ConfigureAwait(false);
        if (result.Context is null || (!result.IsAllowed && !(lookup && result.Code == "access.branch_required")))
            throw Denied(result.Code);
        var access = result.Context;
        if (actorId.HasValue && actorId != access.UserId)
            throw Denied("access.actor_mismatch");
        var roleAllowed = operation switch
        {
            TenantOperation.Administration => access.Role == TenantRole.Owner,
            TenantOperation.Purchasing or TenantOperation.Inventory => access.Role is TenantRole.Owner or TenantRole.Pharmacist,
            TenantOperation.ProductSearch => access.Role is TenantRole.Owner or TenantRole.Pharmacist or TenantRole.Cashier,
            _ => false,
        };
        if (!roleAllowed) throw Denied("access.role_denied");
        return access;
    }

    private static ApplicationErrorException Denied(string code) =>
        new(new(code, ErrorCategory.Forbidden, "The operation is not authorized."));
}
