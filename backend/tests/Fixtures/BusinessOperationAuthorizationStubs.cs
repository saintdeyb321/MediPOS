using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.Tests.Fixtures;

// Existing business-rule unit tests isolate authorization explicitly. Security tests use the real policy
// and a fixed server identity instead. These stubs are not registered by production or PostgreSQL security tests.
internal sealed class BusinessRuleAuthorizationStub : ITenantOperationAuthorizer
{
    public Task<AccessContext> RequireAsync(Guid tenantId, Guid? branchId, Guid? actorId, TenantOperation operation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(new AccessContext(actorId ?? Guid.NewGuid(), tenantId, Guid.NewGuid(), TenantRole.Owner,
            branchId, true, true, null, null, null, true, DateTimeOffset.UnixEpoch));
    }
    public Task<AccessContext> IdentifyForBranchLookupAsync(Guid tenantId, Guid? actorId, TenantOperation operation, CancellationToken token) =>
        RequireAsync(tenantId, null, actorId, operation, token);
}

internal sealed class PlatformAdministrationStub(Guid? actor = null) : IPlatformAdministration
{
    public Guid? Actor { get; set; } = actor;
    public Task<Guid?> GetAuthorizedActorAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Actor);
    }
}
