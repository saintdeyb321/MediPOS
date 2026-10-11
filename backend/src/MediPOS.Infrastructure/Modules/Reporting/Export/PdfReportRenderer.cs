using System.Text;
using MediPOS.Application.Modules.Reporting.ExportOwnerReport;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace MediPOS.Infrastructure.Modules.Reporting.Export;

internal static class PdfReportRenderer
{
    private const double Margin = 26;
    private const double LineHeight = 12;
    private static readonly Lock FontLock = new();
    internal static byte[] Render(ReportExportDocument document, ReportTable table, CancellationToken token)
    {
        lock (FontLock) { GlobalFontSettings.FontResolver ??= new ReportFontResolver(); }
        using var pdf = new PdfDocument();
        pdf.Info.Title = $"REPORTE INTERNO MEDIPOS - {table.Title}";
        pdf.Info.Creator = "MediPOS";
        pdf.Info.CreationDate = document.Request.GeneratedAtUtc.UtcDateTime;
        // Preserve composite accented glyphs: this font's subset loses diacritic outlines in PDF viewers.
        var fontOptions = new XPdfFontOptions(PdfFontEncoding.Unicode, PdfFontEmbedding.EmbedCompleteFontFile);
        var font = new XFont("MediPOS Noto Sans", 8, XFontStyleEx.Regular, fontOptions);
        var heading = new XFont("MediPOS Noto Sans", 12, XFontStyleEx.Regular, fontOptions);
        var landscape = table.Headers.Length > 4;
        var size = PageSize.A4;
        var available = landscape ? 841.89 - 2 * Margin : 595.28 - 2 * Margin;
        // Numeric/date columns reserve their entire value width. Wider amounts use A3 instead of clipping digits.
        using var measurePage = new PdfDocument();
        using var measuring = XGraphics.FromPdfPage(measurePage.AddPage());
        var widths = new double[table.Headers.Length];
        var textColumns = new bool[widths.Length];
        for (var c = 0; c < widths.Length; c++)
        {
            token.ThrowIfCancellationRequested();
            textColumns[c] = !table.Rows.Any(row => row[c].Kind != ReportCellKind.Text);
            widths[c] = textColumns[c] ? 58 : Math.Max(64, table.Rows.Max(row => measuring.MeasureString(row[c].Display, font).Width) + 12);
        }
        if (widths.Sum() > available)
        { size = PageSize.A3; landscape = true; available = 1190.55 - 2 * Margin; }
        var extra = Math.Max(0, available - widths.Sum());
        var flexible = Math.Max(1, textColumns.Count(value => value));
        for (var c = 0; c < widths.Length; c++) if (textColumns[c]) widths[c] += extra / flexible;
        // With no text columns, share the unused width evenly.
        if (!textColumns.Any(value => value)) for (var c = 0; c < widths.Length; c++) widths[c] += extra / widths.Length;
        XGraphics? graphics = null;
        PdfPage? page = null;
        double y = 0;
        var hasTable = false;
        var headerLines = table.Headers.Select((value, c) => Wrap(value, widths[c] - 8, measuring, font, token)).ToArray();
        var headerHeight = headerLines.Max(lines => lines.Length) * LineHeight + 8;
        void NewPage(bool repeatTable)
        {
            token.ThrowIfCancellationRequested();
            graphics?.Dispose();
            page = pdf.AddPage(); page.Size = size; page.Orientation = landscape ? PageOrientation.Landscape : PageOrientation.Portrait;
            graphics = XGraphics.FromPdfPage(page);
            y = Margin;
            graphics.DrawString("REPORTE INTERNO MEDIPOS", heading, XBrushes.DarkSlateBlue, new XPoint(Margin, y + 12));
            y += 20;
            graphics.DrawString(table.Title, heading, XBrushes.Black, new XPoint(Margin, y + 12)); y += 26;
            if (repeatTable) DrawHeader();
        }
        void DrawHeader()
        {
            if (y + headerHeight > page!.Height.Point - Margin - 22) NewPage(false);
            var x = Margin;
            for (var c = 0; c < widths.Length; c++)
            {
                graphics!.DrawRectangle(XBrushes.AliceBlue, x, y, widths[c], headerHeight);
                for (var line = 0; line < headerLines[c].Length; line++)
                    graphics.DrawString(headerLines[c][line], font, XBrushes.Black, new XPoint(x + 4, y + 12 + line * LineHeight));
                x += widths[c];
            }
            y += headerHeight; hasTable = true;
        }
        void Paragraph(string value)
        {
            foreach (var line in Wrap(value, available, measuring, font, token))
            {
                if (y + LineHeight > page!.Height.Point - Margin - 22) NewPage(false);
                graphics!.DrawString(line, font, XBrushes.Black, new XPoint(Margin, y + 9)); y += LineHeight;
            }
            y += 4;
        }
        try
        {
            NewPage(false);
            foreach (var filter in document.AppliedFilters) Paragraph($"{filter.Label}: {filter.Value}");
            Paragraph($"Generado (Lima): {ReportCell.At(document.Request.GeneratedAtUtc).Display}");
            y += 8; DrawHeader();
            foreach (var row in table.Rows)
            {
                token.ThrowIfCancellationRequested();
                var lines = row.Select((value, c) => Wrap(value.Display, widths[c] - 8, measuring, font, token)).ToArray();
                var count = lines.Max(value => value.Length);
                if (y + count * LineHeight + 8 > page!.Height.Point - Margin - 22) NewPage(hasTable);
                // Text rows can continue across pages; numeric values always fit their reserved width on one line.
                for (var line = 0; line < count; line++)
                {
                    if (y + LineHeight > page!.Height.Point - Margin - 22) NewPage(true);
                    var x = Margin;
                    for (var c = 0; c < widths.Length; c++)
                    {
                        if (line < lines[c].Length) graphics!.DrawString(lines[c][line], font, XBrushes.Black, new XPoint(x + 4, y + 9));
                        x += widths[c];
                    }
                    y += LineHeight;
                }
                graphics!.DrawLine(XPens.LightGray, Margin, y + 3, Margin + widths.Sum(), y + 3); y += 8;
            }
            if (table.Rows.Count == 0) Paragraph("Sin resultados para los filtros aplicados");
            y += 10; Paragraph("Totales del conjunto completo");
            foreach (var (label, value) in table.Totals) Paragraph($"{label}: {value.Display}");
            graphics!.Dispose(); graphics = null;
            for (var i = 0; i < pdf.PageCount; i++)
            {
                token.ThrowIfCancellationRequested();
                using var footer = XGraphics.FromPdfPage(pdf.Pages[i], XGraphicsPdfPageOptions.Append);
                footer.DrawString($"Página {i + 1} de {pdf.PageCount}", font, XBrushes.Gray, new XPoint(Margin, pdf.Pages[i].Height.Point - Margin));
            }
            using var output = new BoundedExportStream(token);
            pdf.Save(output, false);
            token.ThrowIfCancellationRequested();
            return output.ToArray();
        }
        finally { graphics?.Dispose(); }
    }

    private static string[] Wrap(string text, double width, XGraphics graphics, XFont font, CancellationToken token)
    {
        // Prefer word boundaries; long unbroken names still wrap at complete Unicode rune boundaries.
        var lines = new List<string>();
        var line = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            token.ThrowIfCancellationRequested();
            if (rune.Value == '\r') continue;
            if (rune.Value == '\n') { lines.Add(line.ToString()); line.Clear(); continue; }
            var value = rune.ToString();
            if (line.Length > 0 && graphics.MeasureString(line.ToString() + value, font).Width > width)
            {
                var current = line.ToString();
                var space = current.LastIndexOf(' ');
                if (space >= 0 && space < current.Length - 1)
                {
                    lines.Add(current[..(space + 1)]); line.Clear(); line.Append(current[(space + 1)..]);
                }
                else { lines.Add(current); line.Clear(); }
            }
            line.Append(value);
        }
        lines.Add(line.ToString());
        return lines.ToArray();
    }
    private sealed class ReportFontResolver : IFontResolver
    {
        private static readonly Lazy<byte[]> Font = new(() =>
        {
            using var resource = typeof(ReportFontResolver).Assembly.GetManifestResourceStream("MediPOS.Infrastructure.Modules.Reporting.Export.Fonts.NotoSans-Regular.ttf")
                ?? throw new InvalidOperationException("The embedded report font is missing.");
            using var data = new MemoryStream(); resource.CopyTo(data); return data.ToArray();
        });
        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
            familyName == "MediPOS Noto Sans" ? new FontResolverInfo("medipos-noto-regular") : null;
        public byte[] GetFont(string faceName) => Font.Value;
    }
}
