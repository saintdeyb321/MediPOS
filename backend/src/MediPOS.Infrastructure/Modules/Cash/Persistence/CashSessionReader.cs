using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.Cash.GetActiveCashSessions;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.Cash.Persistence;

internal sealed class CashSessionReader(MediPosDbContext context) : IFindOpenCashSession, IActiveCashSessionsReader, IClosedCashSessionsReader
{
    public async Task<OpenCashSessionDetails?> FindAsync(
        Guid tenantId, Guid branchId, Guid membershipId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || branchId == Guid.Empty || membershipId == Guid.Empty)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        context.SelectTenant(tenantId);
        return await context.CashSessions.AsNoTracking().Where(value => value.TenantId == tenantId &&
            value.BranchId == branchId && value.MembershipId == membershipId && value.Status == CashSessionStatus.Open)
            .Select(value => new OpenCashSessionDetails(value.Id, value.TenantId, value.BranchId, value.MembershipId,
                value.OpeningAmount, value.OpenedAt, value.OpenedByActorId))
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ActiveCashSessionDetails>> ReadAsync(
        GetActiveCashSessionsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.TenantId == Guid.Empty || query.BranchId == Guid.Empty || query.Offset is < 0 or > 100000 || query.Limit is < 1 or > 100)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        context.SelectTenant(query.TenantId);
        var sessions = from session in context.CashSessions.AsNoTracking()
                       where session.TenantId == query.TenantId && session.Status == CashSessionStatus.Open &&
                           (!query.BranchId.HasValue || session.BranchId == query.BranchId)
                       join branch in context.Branches on new { session.TenantId, Id = session.BranchId } equals new { branch.TenantId, branch.Id }
                       join membership in context.Memberships on new { session.TenantId, Id = session.MembershipId } equals new { membership.TenantId, membership.Id }
                       join user in context.Users on membership.UserId equals user.Id
                       orderby session.OpenedAt descending, session.Id descending
                       select new ActiveCashSessionDetails(session.Id, branch.Id, branch.Name, membership.Id,
                           user.Id, user.DisplayName, session.OpeningAmount, session.OpenedAt,
                           context.Sales.Where(sale => sale.TenantId == session.TenantId && sale.CashSessionId == session.Id && sale.Status == SaleStatus.Confirmed)
                               .Sum(sale => (decimal?)sale.TotalAmount) ?? 0m);
        return await sessions.Skip(query.Offset).Take(query.Limit).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ClosedCashSessionPage> ReadClosedAsync(Guid tenantId, Guid branchId, Guid? membershipId,
        DateTimeOffset start, DateTimeOffset endExclusive, int offset, int limit, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        var sessions = from session in context.CashSessions.AsNoTracking()
                       where session.TenantId == tenantId && session.BranchId == branchId &&
                           session.Status == CashSessionStatus.Closed && session.ClosedAt >= start && session.ClosedAt < endExclusive &&
                           (!membershipId.HasValue || session.MembershipId == membershipId)
                       join membership in context.Memberships on new { session.TenantId, Id = session.MembershipId } equals new { membership.TenantId, membership.Id }
                       join user in context.Users on membership.UserId equals user.Id
                       orderby session.ClosedAt descending, session.Id descending
                       select new ClosedCashSessionDetails(session.Id, user.DisplayName, session.ClosedAt!.Value,
                           session.ExpectedCashAmount!.Value, session.CountedCashAmount!.Value, session.CashDifference!.Value);
        var rows = await sessions.Skip(offset).Take(limit + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        var hasMore = rows.Count > limit;
        if (hasMore) rows.RemoveAt(limit);
        return new(rows, hasMore);
    }
}
