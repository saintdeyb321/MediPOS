using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using MediPOS.Application.Modules.Reporting.ExportOwnerReport;

namespace MediPOS.Infrastructure.Modules.Reporting.Export;

internal static class XlsxReportRenderer
{
    internal static byte[] Render(ReportExportDocument document, ReportTable table, CancellationToken token)
    {
        using var output = new BoundedExportStream(token);
        using (var workbook = SpreadsheetDocument.Create(output, SpreadsheetDocumentType.Workbook, true))
        {
            var part = workbook.AddWorkbookPart();
            part.Workbook = new Workbook();
            var styles = part.AddNewPart<WorkbookStylesPart>();
            styles.Stylesheet = Styles();
            var sheet = part.AddNewPart<WorksheetPart>();
            List<ReportCell[]> metadata =
            [
                [ReportCell.T("REPORTE INTERNO MEDIPOS")], [ReportCell.T(table.Title)],
            ];
            metadata.AddRange(document.AppliedFilters.Select(filter => new[] { ReportCell.T(filter.Label), ReportCell.T(filter.Value) }));
            metadata.Add([ReportCell.T("Generado (Lima)"), ReportCell.At(document.Request.GeneratedAtUtc)]);
            // Excel itself has a 15-significant-digit calculation/display limit; stored OOXML numerics remain exact.
            metadata.Add([ReportCell.T("Precisión XLSX"), ReportCell.T("Valores numéricos exactos en el archivo; Excel puede limitar su visualización/cálculo a 15 cifras significativas.")]);
            var headerRow = (uint)metadata.Count + 2;
            var merges = new List<MergeCell>();
            using (var writer = OpenXmlWriter.Create(sheet))
            {
                writer.WriteStartElement(new Worksheet());
                writer.WriteElement(new SheetViews(new SheetView(new Pane { State = PaneStateValues.Frozen, VerticalSplit = headerRow, TopLeftCell = $"A{headerRow + 1}", ActivePane = PaneValues.BottomLeft }) { WorkbookViewId = 0 }));
                writer.WriteElement(new Columns(Enumerable.Range(1, table.Headers.Length).Select(i => new Column { Min = (uint)i, Max = (uint)i, Width = i == 1 ? 38 : 28, CustomWidth = true })));
                writer.WriteStartElement(new SheetData());
                uint rowIndex = 0;
                foreach (var row in metadata)
                {
                    WriteRow(writer, ++rowIndex, row, token, rowIndex <= 2 ? 5u : null, table.Headers.Length);
                    if (row.Length > 1 && row[1].Kind == ReportCellKind.Text && table.Headers.Length > 2)
                        merges.Add(new MergeCell { Reference = $"B{rowIndex}:{Column(table.Headers.Length - 1)}{rowIndex}" });
                }
                WriteRow(writer, ++rowIndex, [], token);
                WriteRow(writer, ++rowIndex, table.Headers.Select(ReportCell.T).ToArray(), token, 5);
                foreach (var row in table.Rows) WriteRow(writer, ++rowIndex, row, token);
                if (table.Rows.Count == 0) WriteRow(writer, ++rowIndex, [ReportCell.T("Sin resultados para los filtros aplicados")], token);
                WriteRow(writer, ++rowIndex, [], token);
                WriteRow(writer, ++rowIndex, [ReportCell.T("Totales del conjunto completo")], token, 5);
                foreach (var (label, value) in table.Totals) WriteRow(writer, ++rowIndex, [ReportCell.T(label), value], token);
                writer.WriteEndElement();
                if (merges.Count > 0) writer.WriteElement(new MergeCells(merges));
                writer.WriteElement(new PageMargins { Left = .3, Right = .3, Top = .4, Bottom = .4, Header = .2, Footer = .2 });
                writer.WriteElement(new PageSetup { Orientation = table.Headers.Length > 4 ? OrientationValues.Landscape : OrientationValues.Portrait, FitToWidth = 1, FitToHeight = 0 });
                writer.WriteEndElement();
            }
            part.Workbook.Append(new Sheets(new Sheet { Id = part.GetIdOfPart(sheet), SheetId = 1, Name = "Reporte" }));
            part.Workbook.Save();
        }
        token.ThrowIfCancellationRequested();
        return output.ToArray();
    }
    private static void WriteRow(OpenXmlWriter writer, uint index, ReportCell[] values, CancellationToken token, uint? style = null, int? mergedMetadataWidth = null)
    {
        token.ThrowIfCancellationRequested();
        var lines = values.Select((value, i) => value.Kind != ReportCellKind.Text ? 1 :
            value.Text!.Split('\n').Sum(line => Math.Max(1, (int)Math.Ceiling(line.Length / (decimal)(i == 0 ? 36 : mergedMetadataWidth.HasValue ? 26 * (mergedMetadataWidth.Value - 1) : 26)))))
            .DefaultIfEmpty(1).Max();
        writer.WriteStartElement(new Row { RowIndex = index, Height = Math.Min(409.5, 15 * lines), CustomHeight = true });
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            var cell = new Cell { CellReference = $"{Column(i)}{index}" };
            if (value.Kind == ReportCellKind.Text)
            {
                // Inline strings never become formulas, including names beginning with =, +, -, @.
                cell.DataType = CellValues.InlineString;
                cell.InlineString = new InlineString(new Text(value.Text!) { Space = SpaceProcessingModeValues.Preserve });
                cell.StyleIndex = style ?? 0;
            }
            else
            {
                cell.DataType = CellValues.Number;
                var number = value.Number;
                if (value.Kind is ReportCellKind.Date or ReportCellKind.Timestamp)
                    number = (DateOnly.FromDateTime(value.Date).DayNumber - new DateOnly(1899, 12, 30).DayNumber) + value.Date.TimeOfDay.Ticks / (decimal)TimeSpan.TicksPerDay;
                cell.CellValue = new CellValue(number.ToString(CultureInfo.InvariantCulture));
                cell.StyleIndex = value.Kind switch { ReportCellKind.Money => 1u, ReportCellKind.Date => 3u, ReportCellKind.Timestamp => 4u, _ => 2u };
            }
            writer.WriteElement(cell);
        }
        writer.WriteEndElement();
    }
    private static string Column(int index)
    {
        var result = "";
        do { result = (char)('A' + index % 26) + result; index = index / 26 - 1; } while (index >= 0);
        return result;
    }
    private static Stylesheet Styles() => new(
        new NumberingFormats(new NumberingFormat { NumberFormatId = 164, FormatCode = "\"PEN\" #,##0.0000" },
            new NumberingFormat { NumberFormatId = 165, FormatCode = "0.############################" },
            new NumberingFormat { NumberFormatId = 166, FormatCode = "dd/mm/yyyy" },
            new NumberingFormat { NumberFormatId = 167, FormatCode = "dd/mm/yyyy hh:mm:ss" })
        { Count = 4 },
        new Fonts(new Font(new FontSize { Val = 11 }, new FontName { Val = "Calibri" }), new Font(new Bold(), new FontSize { Val = 11 }, new FontName { Val = "Calibri" })) { Count = 2 },
        new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 })) { Count = 2 },
        new Borders(new Border()) { Count = 1 },
        new CellStyleFormats(new CellFormat()) { Count = 1 },
        new CellFormats(new CellFormat(new Alignment { WrapText = true, Vertical = VerticalAlignmentValues.Top }),
            Numeric(164), Numeric(165), Numeric(166), Numeric(167),
            new CellFormat(new Alignment { WrapText = true }) { FontId = 1, ApplyFont = true })
        { Count = 6 },
        new CellStyles(new CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }) { Count = 1 });
    private static CellFormat Numeric(uint format) => new(new Alignment { Vertical = VerticalAlignmentValues.Top }) { NumberFormatId = format, ApplyNumberFormat = true };
}
