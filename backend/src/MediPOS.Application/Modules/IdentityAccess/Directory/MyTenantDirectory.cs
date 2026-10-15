using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.Application.Modules.IdentityAccess.Directory;

// Membership role is descriptive only. Each operational request must resolve current access again.
public sealed record MyTenantSummary(Guid TenantId, string TradingName, TenantRole Role);
public sealed record MyTenantPage(IReadOnlyList<MyTenantSummary> Items, Guid? NextAfterTenantId);
public interface IMyTenantDirectoryReader
{
    Task<MyTenantPage> ReadAsync(Guid userId, Guid? afterTenantId, int limit, CancellationToken token);
}
public sealed class ListMyTenantsHandler(IAuthenticatedMediPosUser user, IMyTenantDirectoryReader reader)
{
    public async Task<MyTenantPage> HandleAsync(Guid? afterTenantId, int limit, CancellationToken token)
    {
        if (user.UserId is not { } actor || actor == Guid.Empty)
            throw new ApplicationErrorException(new("access.unauthenticated", ErrorCategory.Forbidden, "Authentication is required."));
        if (afterTenantId == Guid.Empty || limit is < 1 or > 100) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var result = await reader.ReadAsync(actor, afterTenantId, limit, token).ConfigureAwait(false);
        if (user.UserId != actor) throw new ApplicationErrorException(new("access.unauthenticated", ErrorCategory.Forbidden, "Authentication is required."));
        return result;
    }
}
