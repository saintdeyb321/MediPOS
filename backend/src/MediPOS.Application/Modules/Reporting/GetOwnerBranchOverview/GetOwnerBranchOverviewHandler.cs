using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.Application.Modules.Reporting.GetOwnerBranchOverview;

public sealed record GetOwnerBranchOverviewQuery(Guid TenantId, Guid? BranchId, DateOnly FromLocalDate, DateOnly ToLocalDate);
public static class OwnerOverviewErrors
{
    public static readonly ApplicationError Forbidden = new("owner_overview.forbidden", ErrorCategory.Forbidden, "Owner permission is required.");
    public static readonly ApplicationError InvalidPeriod = new("owner_overview.invalid_period", ErrorCategory.Validation, "An inclusive local date range of at most 31 days is required.");
    public static readonly ApplicationError CorruptedOverview = new("owner_overview.corrupted_data", ErrorCategory.Conflict, "Overview metrics are inconsistent.");
    public static readonly ApplicationError MetricOverflow = new("owner_overview.metric_overflow", ErrorCategory.Conflict, "Overview metrics exceed the supported exact range.");
}
public static class OwnerOverviewPeriod
{
    private static readonly TimeZoneInfo Lima = TimeZoneInfo.FindSystemTimeZoneById("America/Lima");
    public static (DateTimeOffset StartUtc, DateTimeOffset EndExclusiveUtc) Create(DateOnly from, DateOnly to)
    {
        if (to < from || to.DayNumber - from.DayNumber >= 31 || to == DateOnly.MaxValue) throw new ArgumentException("Invalid bounded inclusive period.");
        static DateTime Midnight(DateOnly date) => date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return (new(TimeZoneInfo.ConvertTimeToUtc(Midnight(from), Lima)), new(TimeZoneInfo.ConvertTimeToUtc(Midnight(to.AddDays(1)), Lima)));
    }
    public static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Lima).DateTime);
}
public sealed class GetOwnerBranchOverviewHandler(ResolveAccessContextHandler resolver, IOwnerBranchOverviewReader reader, TimeProvider clock)
{
    public async Task<OwnerBranchOverview> HandleAsync(GetOwnerBranchOverviewQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.TenantId == Guid.Empty || query.BranchId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        (DateTimeOffset StartUtc, DateTimeOffset EndExclusiveUtc) period;
        try { period = OwnerOverviewPeriod.Create(query.FromLocalDate, query.ToLocalDate); }
        catch (ArgumentException) { throw new ApplicationErrorException(OwnerOverviewErrors.InvalidPeriod); }
        var generatedAt = clock.GetUtcNow().ToUniversalTime();
        await RequireOwnerAsync(query, generatedAt, cancellationToken).ConfigureAwait(false);
        var request = new OwnerOverviewReadRequest(query.TenantId, query.BranchId, period.StartUtc, period.EndExclusiveUtc, OwnerOverviewPeriod.Today(generatedAt));
        try
        {
            var branches = await reader.ReadAsync(request, cancellationToken).ConfigureAwait(false);
            await RequireOwnerAsync(query, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
            return OwnerBranchOverview.From(request, branches, generatedAt);
        }
        catch (OverflowException) { throw new ApplicationErrorException(OwnerOverviewErrors.MetricOverflow); }
        catch (ArgumentException) { throw new ApplicationErrorException(OwnerOverviewErrors.CorruptedOverview); }
    }
    private async Task RequireOwnerAsync(GetOwnerBranchOverviewQuery query, DateTimeOffset now, CancellationToken token)
    {
        var result = await resolver.HandleAsync(query.TenantId, query.BranchId, now, token).ConfigureAwait(false);
        if (result.Context?.Role is TenantRole.Cashier or TenantRole.Pharmacist) throw new ApplicationErrorException(OwnerOverviewErrors.Forbidden);
        if (!result.IsAllowed || result.Context is null)
            throw new ApplicationErrorException(new(result.Code, ErrorCategory.Forbidden, "Operational access was denied."));
        if (result.Context.Role != TenantRole.Owner) throw new ApplicationErrorException(OwnerOverviewErrors.Forbidden);
    }
}
