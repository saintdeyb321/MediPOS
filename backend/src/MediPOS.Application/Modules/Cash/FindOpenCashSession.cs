using MediPOS.Domain.Modules.Cash;

namespace MediPOS.Application.Modules.Cash;

public sealed record OpenCashSessionDetails(Guid CashSessionId, Guid TenantId, Guid BranchId, Guid MembershipId,
    decimal OpeningAmount, DateTimeOffset OpenedAt, Guid OpenedByActorId)
{
    public static OpenCashSessionDetails From(CashSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new(session.Id, session.TenantId, session.BranchId, session.MembershipId,
            session.OpeningAmount, session.OpenedAt, session.OpenedByActorId);
    }
}

// Internal collaboration for checkout. Callers must resolve operational access for each protected operation.
// These keys identify a session; this lookup is not an authorization token.
public interface IFindOpenCashSession
{
    Task<OpenCashSessionDetails?> FindAsync(Guid tenantId, Guid branchId, Guid membershipId, CancellationToken cancellationToken);
}
