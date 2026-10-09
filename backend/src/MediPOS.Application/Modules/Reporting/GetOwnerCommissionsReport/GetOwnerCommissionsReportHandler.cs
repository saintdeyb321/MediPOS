using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.Reporting.GetOwnerBranchOverview;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.Application.Modules.Reporting.GetOwnerCommissionsReport;

public sealed record GetOwnerCommissionsReportQuery(Guid TenantId, Guid? BranchId, Guid? SellerMembershipId, Guid? BusinessProductId,
    DateOnly FromLocalDate, DateOnly ToLocalDate, int Offset = 0, int Limit = 50);

public static class CommissionReportErrors
{
    public static readonly ApplicationError Forbidden = new("commissions.report_forbidden", ErrorCategory.Forbidden, "Owner permission is required.");
    public static readonly ApplicationError InvalidPeriod = new("commissions.report_invalid_period", ErrorCategory.Validation, "A local period of at most 31 days is required.");
    public static readonly ApplicationError CorruptedHistory = new("commissions.report_corrupted_history", ErrorCategory.Conflict, "Commission history is inconsistent.");
    public static readonly ApplicationError AmountOverflow = new("commissions.report_amount_overflow", ErrorCategory.Conflict, "Commission totals exceed the supported exact range.");
}

public sealed class GetOwnerCommissionsReportHandler(ResolveAccessContextHandler resolver, IOwnerCommissionsReportReader reader, TimeProvider clock)
{
    public async Task<OwnerCommissionsReport> HandleAsync(GetOwnerCommissionsReportQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.TenantId == Guid.Empty || query.BranchId == Guid.Empty || query.SellerMembershipId == Guid.Empty || query.BusinessProductId == Guid.Empty ||
            query.Offset is < 0 or > OwnerCommissionsReport.MaximumOffset || query.Limit is < 1 or > OwnerCommissionsReport.MaximumLimit)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        (DateTimeOffset StartUtc, DateTimeOffset EndExclusiveUtc) period;
        try { period = OwnerOverviewPeriod.Create(query.FromLocalDate, query.ToLocalDate); }
        catch (ArgumentException) { throw new ApplicationErrorException(CommissionReportErrors.InvalidPeriod); }
        var generatedAt = clock.GetUtcNow().ToUniversalTime();
        await RequireOwnerAsync(query, generatedAt, cancellationToken).ConfigureAwait(false);
        try
        {
            var request = new OwnerCommissionsReadRequest(query.TenantId, query.BranchId, query.SellerMembershipId,
                query.BusinessProductId, period.StartUtc, period.EndExclusiveUtc, query.Offset, query.Limit);
            var page = await reader.ReadAsync(request, cancellationToken).ConfigureAwait(false);
            await RequireOwnerAsync(query, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
            return OwnerCommissionsReport.From(request, page, generatedAt);
        }
        catch (OverflowException) { throw new ApplicationErrorException(CommissionReportErrors.AmountOverflow); }
        catch (ArgumentException) { throw new ApplicationErrorException(CommissionReportErrors.CorruptedHistory); }
    }

    private async Task RequireOwnerAsync(GetOwnerCommissionsReportQuery query, DateTimeOffset now, CancellationToken token)
    {
        var result = await resolver.HandleAsync(query.TenantId, query.BranchId, now, token).ConfigureAwait(false);
        if (result.Context?.Role is TenantRole.Cashier or TenantRole.Pharmacist)
            throw new ApplicationErrorException(CommissionReportErrors.Forbidden);
        if (!result.IsAllowed || result.Context is null)
            throw new ApplicationErrorException(new(result.Code, ErrorCategory.Forbidden, "Operational access was denied."));
        if (result.Context.Role != TenantRole.Owner) throw new ApplicationErrorException(CommissionReportErrors.Forbidden);
    }
}
