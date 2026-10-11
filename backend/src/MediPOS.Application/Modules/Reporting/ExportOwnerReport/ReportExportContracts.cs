using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Reporting.GetOwnerCommissionsReport;
using MediPOS.Application.Modules.Reporting.Operational;

namespace MediPOS.Application.Modules.Reporting.ExportOwnerReport;

public enum OwnerReportType { Sales, Commissions, StockRisk, Expirations, InventoryCapital, ProductRotation }
public enum ReportExportFormat { Xlsx, Pdf }
public sealed record ExportExpirationRange(DateOnly From, DateOnly To);
public sealed record ExportOwnerReportQuery(Guid TenantId, OwnerReportType ReportType, ReportExportFormat Format,
    Guid? BranchId = null, OperationalPeriodInput? Period = null, OwnerSalesDimension? Dimension = null,
    Guid? EmployeeMembershipId = null, Guid? BusinessProductId = null, Guid? CategoryId = null,
    bool? CriticalOnly = null, ExportExpirationRange? ExpirationRange = null,
    OwnerProductRotationMode? RotationMode = null, OwnerSalesSort? Sort = null);

// Hard resource bounds cannot be raised by the caller. Only the server's timeout is configurable.
public sealed record ReportExportLimits
{
    public const int MaximumRows = 10_000;
    public const int MaximumBytes = 10 * 1024 * 1024;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    public static void RequireRows(long count)
    {
        if (count > MaximumRows) throw new ApplicationErrorException(ReportExportErrors.TooLarge);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
    }
}
public static class ReportExportErrors
{
    public static readonly ApplicationError TooLarge = new("reports.export_too_large", ErrorCategory.Validation,
        "The complete report exceeds 10000 rows or 10 MiB.");
    public static readonly ApplicationError Timeout = new("reports.export_timeout", ErrorCategory.Conflict, "The report export exceeded its time limit.");
}

public sealed record ReportExportReadRequest(ExportOwnerReportQuery Query, OperationalReadScope Scope,
    OperationalReportPeriod? Period, DateTimeOffset GeneratedAtUtc);
public sealed record ReportExportMetadata(string BusinessName, IReadOnlyDictionary<Guid, string> BranchNames,
    IReadOnlyDictionary<Guid, string> CategoryNames, string? EmployeeName, string? ProductName, string? CategoryName);
public sealed record ReportExportFilter(string Key, string Label, string Value);
public abstract record ReportExportData { public abstract int RowCount { get; } }
public sealed record SalesExportData(SalesReportPage Page) : ReportExportData { public override int RowCount => Page.Rows.Count; }
public sealed record CommissionsExportData(CommissionReportPage Page) : ReportExportData { public override int RowCount => Page.Rows.Count; }
public sealed record StockRiskExportData(StockRiskPage Page) : ReportExportData { public override int RowCount => Page.Rows.Count; }
public sealed record ExpirationsExportData(ExpirationPage Page) : ReportExportData { public override int RowCount => Page.Rows.Count; }
public sealed record CapitalExportData(InventoryCapitalSnapshot Snapshot) : ReportExportData { public override int RowCount => Snapshot.Branches.Count; }
public sealed record RotationExportData(ProductRotationPage Page) : ReportExportData { public override int RowCount => Page.Rows.Count; }
public sealed record ReportExportSnapshot(ReportExportData Data, ReportExportMetadata Metadata);
public sealed record ReportExportDocument(ReportExportReadRequest Request, ReportExportSnapshot Snapshot,
    IReadOnlyList<ReportExportFilter> AppliedFilters);
public sealed record ExportedReport(string FileName, string ContentType, ReadOnlyMemory<byte> Content,
    DateTimeOffset GeneratedAtUtc, OwnerReportType ReportType, IReadOnlyList<ReportExportFilter> AppliedFilters);
public interface IReportExportReader
{
    Task<ReportExportSnapshot> ReadAsync(ReportExportReadRequest request, CancellationToken token);
}
public interface IReportDocumentRenderer
{
    Task<byte[]> RenderAsync(ReportExportDocument document, ReportExportFormat format, CancellationToken token);
}
