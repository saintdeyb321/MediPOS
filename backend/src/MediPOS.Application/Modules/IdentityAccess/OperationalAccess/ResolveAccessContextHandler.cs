using MediPOS.Application.Modules.IdentityAccess.Authentication;

namespace MediPOS.Application.Modules.IdentityAccess.OperationalAccess;

public sealed class ResolveAccessContextHandler(
    IAuthenticatedMediPosUser authenticatedUser, IOperationalAccessReader reader)
{
    // Tenant/branch are selection requests only. Identity comes exclusively from the validated server session.
    // B0.5 must re-evaluate per protected operation with server time; this result is not a durable authorization token.
    public async Task<OperationalAccessResult> HandleAsync(
        Guid tenantId, Guid? branchId, DateTimeOffset serverInstant, CancellationToken cancellationToken)
    {
        var userId = authenticatedUser.UserId;
        if (!userId.HasValue || userId == Guid.Empty)
            return OperationalAccessResult.Denied("access.unauthenticated");
        var snapshot = await reader.ReadAsync(userId.Value, tenantId, branchId, cancellationToken).ConfigureAwait(false);
        return EvaluateOperationalAccess.Evaluate(userId.Value, tenantId, branchId, serverInstant, snapshot);
    }
}
