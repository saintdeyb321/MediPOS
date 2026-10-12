using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;

namespace MediPOS.Infrastructure.Modules.IdentityAccess;

internal sealed class DisabledPlatformAdministration : IPlatformAdministration
{
    public Task<Guid?> GetAuthorizedActorAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Guid?>(null);
    }
}

internal sealed class DisabledTenantOperationAuthorizer : ITenantOperationAuthorizer
{
    public Task<AccessContext> RequireAsync(Guid tenantId, Guid? branchId, Guid? actorId, TenantOperation operation, CancellationToken token) => Deny(token);
    public Task<AccessContext> IdentifyForBranchLookupAsync(Guid tenantId, Guid? actorId, TenantOperation operation, CancellationToken token) => Deny(token);
    private static Task<AccessContext> Deny(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        throw new ApplicationErrorException(new("access.unauthenticated", ErrorCategory.Forbidden, "An authenticated server session is required."));
    }
}
