using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.Reporting.GetOwnerCommissionsReport;
using MediPOS.Application.Modules.Reporting.Operational;

namespace MediPOS.Application.Modules.Reporting.ExportOwnerReport;

public sealed class ExportOwnerReportHandler(ResolveAccessContextHandler resolver, IReportExportReader reader,
    IReportDocumentRenderer renderer, TimeProvider clock, ReportExportLimits limits)
{
    public async Task<ExportedReport> HandleAsync(ExportOwnerReportQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var at = clock.GetUtcNow().ToUniversalTime();
        var request = ReportExportPolicy.CreateRequest(query, at);
        if (limits.Timeout <= TimeSpan.Zero || limits.Timeout > TimeSpan.FromMinutes(5))
            throw new InvalidOperationException("Export timeout must be positive and at most five minutes.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(limits.Timeout);
        var token = timeout.Token;
        try
        {
            token.ThrowIfCancellationRequested();
            await OperationalReportPolicy.RequireOwnerAsync(resolver, query.TenantId, query.BranchId, at, token).ConfigureAwait(false);
            var snapshot = await reader.ReadAsync(request, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            await OperationalReportPolicy.RequireOwnerAsync(resolver, query.TenantId, query.BranchId, clock.GetUtcNow(), token).ConfigureAwait(false);
            ReportExportPolicy.Validate(snapshot, request);
            var filters = ReportExportPolicy.Filters(request, snapshot.Metadata);
            var bytes = await renderer.RenderAsync(new(request, snapshot, filters), query.Format, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (bytes.Length > ReportExportLimits.MaximumBytes) throw new ApplicationErrorException(ReportExportErrors.TooLarge);
            if (bytes.Length == 0) throw new ArgumentException("An export document is empty.");
            await OperationalReportPolicy.RequireOwnerAsync(resolver, query.TenantId, query.BranchId, clock.GetUtcNow(), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var extension = query.Format == ReportExportFormat.Xlsx ? "xlsx" : "pdf";
            var contentType = query.Format == ReportExportFormat.Xlsx
                ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : "application/pdf";
            return new($"medipos-{ReportExportPolicy.TypeCode(query.ReportType)}-{request.Scope.TodayLocal:yyyy-MM-dd}.{extension}",
                contentType, bytes, at, query.ReportType, filters);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        { throw new ApplicationErrorException(ReportExportErrors.Timeout); }
        catch (OverflowException) { throw new ApplicationErrorException(OperationalReportErrors.Overflow); }
        catch (ArgumentException) { throw new ApplicationErrorException(OperationalReportErrors.Corrupted); }
    }
}
