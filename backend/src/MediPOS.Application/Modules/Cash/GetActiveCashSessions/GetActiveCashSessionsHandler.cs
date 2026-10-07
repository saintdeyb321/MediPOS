using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.Application.Modules.Cash.GetActiveCashSessions;

public sealed record GetActiveCashSessionsQuery(Guid TenantId, Guid? BranchId = null, int Offset = 0, int Limit = 50);

public sealed record ActiveCashSessionDetails(Guid CashSessionId, Guid BranchId, string BranchName,
    Guid MembershipId, Guid UserId, string DisplayName, decimal OpeningAmount, DateTimeOffset OpenedAt, decimal AccumulatedSales);

public interface IActiveCashSessionsReader
{
    Task<IReadOnlyList<ActiveCashSessionDetails>> ReadAsync(GetActiveCashSessionsQuery query, CancellationToken cancellationToken);
}

public sealed class GetActiveCashSessionsHandler(
    ResolveAccessContextHandler accessResolver, IActiveCashSessionsReader reader, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<ActiveCashSessionDetails>> HandleAsync(
        GetActiveCashSessionsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.TenantId == Guid.Empty || query.BranchId == Guid.Empty || query.Offset is < 0 or > 100000 || query.Limit is < 1 or > 100)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var access = await CashOperationalAccess.ResolveAsync(accessResolver, query.TenantId, query.BranchId,
            timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        if (access.Role != TenantRole.Owner) throw new ApplicationErrorException(CashSessionErrors.OwnerRequired);
        return await reader.ReadAsync(query, cancellationToken).ConfigureAwait(false);
    }
}
