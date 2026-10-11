using MediPOS.Application.Modules.Reporting.ExportOwnerReport;

namespace MediPOS.Infrastructure.Modules.Reporting.Export;

public sealed class ReportDocumentRenderer : IReportDocumentRenderer
{
    public Task<byte[]> RenderAsync(ReportExportDocument document, ReportExportFormat format, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ReportExportLimits.RequireRows(document.Snapshot.Data.RowCount);
        var table = ReportTable.From(document);
        var bytes = format switch
        {
            ReportExportFormat.Xlsx => XlsxReportRenderer.Render(document, table, token),
            ReportExportFormat.Pdf => PdfReportRenderer.Render(document, table, token),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
        return Task.FromResult(bytes);
    }
}
