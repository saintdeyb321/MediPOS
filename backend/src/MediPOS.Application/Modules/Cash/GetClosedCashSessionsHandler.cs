using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.Reporting.Operational;

namespace MediPOS.Application.Modules.Cash;

public sealed record GetClosedCashSessionsQuery(Guid TenantId, Guid BranchId, DateOnly? From = null, DateOnly? To = null,
    int Offset = 0, int Limit = 50);

public sealed record ClosedCashSessionDetails(Guid CashSessionId, string DisplayName, DateTimeOffset ClosedAt,
    decimal ExpectedCashAmount, decimal CountedCashAmount, decimal CashDifference);

public sealed record ClosedCashSessionPage(IReadOnlyList<ClosedCashSessionDetails> Items, bool HasMore);

public interface IClosedCashSessionsReader
{
    Task<ClosedCashSessionPage> ReadClosedAsync(Guid tenantId, Guid branchId, Guid? membershipId,
        DateTimeOffset start, DateTimeOffset endExclusive, int offset, int limit, CancellationToken cancellationToken);
}

public sealed class GetClosedCashSessionsHandler(ResolveAccessContextHandler resolver, IClosedCashSessionsReader reader, TimeProvider clock)
{
    public async Task<ClosedCashSessionPage> HandleAsync(GetClosedCashSessionsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.TenantId == Guid.Empty || query.BranchId == Guid.Empty || query.Offset is < 0 or > 10_000 ||
            query.Limit is < 1 or > 100 || query.From.HasValue != query.To.HasValue)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);

        var now = clock.GetUtcNow();
        var last = query.To ?? OperationalReportPolicy.Today(now);
        var first = query.From ?? last.AddDays(-29);
        if (last < first || last.DayNumber - first.DayNumber >= 90 || last == DateOnly.MaxValue)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var period = OperationalReportPolicy.Resolve(new(OperationalPeriodType.Custom, first, last), now);
        var access = await CashOperationalAccess.ResolveAsync(resolver, query.TenantId, query.BranchId, now, cancellationToken).ConfigureAwait(false);
        var membership = CashReconciliationAccess.RestrictedMembership(access);
        var page = await reader.ReadClosedAsync(query.TenantId, query.BranchId, membership, period.StartUtc,
            period.EndExclusiveUtc, query.Offset, query.Limit, cancellationToken).ConfigureAwait(false);
        var current = await CashOperationalAccess.ResolveAsync(resolver, query.TenantId, query.BranchId, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        if (current.MembershipId != access.MembershipId || CashReconciliationAccess.RestrictedMembership(current) != membership)
            throw new ApplicationErrorException(CashSessionErrors.ForbiddenReconciliation);
        return page;
    }
}
