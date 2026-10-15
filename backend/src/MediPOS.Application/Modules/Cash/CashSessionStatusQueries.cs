using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;

namespace MediPOS.Application.Modules.Cash;

public sealed record CurrentCashSession(OpenCashSessionDetails? Session);
public sealed record CashSessionStatusDetails(Guid CashSessionId, Guid BranchId, Guid MembershipId, string Status,
    decimal OpeningAmount, DateTimeOffset OpenedAt, DateTimeOffset? ClosedAt, decimal? ExpectedCashAmount);
public sealed class CashSessionStatusQueries(ResolveAccessContextHandler resolver, IFindOpenCashSession openSessions,
    ICashSessionReconciliationReader reader, TimeProvider clock)
{
    public async Task<CurrentCashSession> CurrentAsync(Guid tenantId, Guid branchId, CancellationToken token)
    {
        var access = await CashOperationalAccess.ResolveAsync(resolver, tenantId, branchId, clock.GetUtcNow(), token).ConfigureAwait(false);
        var result = await openSessions.FindAsync(tenantId, branchId, access.MembershipId, token).ConfigureAwait(false);
        await CashOperationalAccess.ResolveAsync(resolver, tenantId, branchId, clock.GetUtcNow(), token).ConfigureAwait(false);
        return new(result);
    }
    public async Task<CashSessionStatusDetails> StatusAsync(Guid tenantId, Guid branchId, Guid sessionId, CancellationToken token)
    {
        if (sessionId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var access = await CashOperationalAccess.ResolveAsync(resolver, tenantId, branchId, clock.GetUtcNow(), token).ConfigureAwait(false);
        var result = await reader.FindAsync(tenantId, branchId, sessionId, token).ConfigureAwait(false) ?? throw new ApplicationErrorException(CashSessionErrors.NotFound);
        access = await CashOperationalAccess.ResolveAsync(resolver, tenantId, branchId, clock.GetUtcNow(), token).ConfigureAwait(false);
        CashReconciliationAccess.Require(result.Session, access, closing: false);
        var session = result.Session;
        return new(session.Id, session.BranchId, session.MembershipId, Domain.Modules.Cash.CashSessionStatusCodes.ToCode(session.Status),
            session.OpeningAmount, session.OpenedAt, session.ClosedAt, session.ExpectedCashAmount);
    }
}
