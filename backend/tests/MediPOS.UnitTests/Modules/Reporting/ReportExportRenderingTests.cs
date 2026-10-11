using System.Globalization;
using System.Security.Cryptography;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Reporting.ExportOwnerReport;
using MediPOS.Application.Modules.Reporting.GetOwnerCommissionsReport;
using MediPOS.Application.Modules.Reporting.Operational;
using MediPOS.Infrastructure.Modules.Reporting.Export;
using UglyToad.PdfPig;

namespace MediPOS.UnitTests.Modules.Reporting;

public sealed class ReportExportRenderingTests
{
    public static TheoryData<OwnerReportType, ReportExportFormat> Reports => new(
        from type in Enum.GetValues<OwnerReportType>() from format in Enum.GetValues<ReportExportFormat>() select (type, format));

    [Theory]
    [MemberData(nameof(Reports))]
    public async Task RealDocumentsContainOriginalRowsTotalsMetadataAndLimaDates(OwnerReportType type, ReportExportFormat format)
    {
        var document = Document(type);
        ReportExportPolicy.Validate(document.Snapshot, document.Request);
        var bytes = await new ReportDocumentRenderer().RenderAsync(document, format, TestContext.Current.CancellationToken);
        var text = Text(bytes, format);
        Assert.Contains("REPORTE INTERNO MEDIPOS", text); Assert.Contains("Botica Ñ & <Centro>", text);
        Assert.Contains("Generado (Lima)", text); Assert.Contains("Totales del conjunto completo", text);
        if (type is OwnerReportType.Sales or OwnerReportType.ProductRotation) Assert.Contains("current_catalog_category", text);
        if (format == ReportExportFormat.Xlsx)
        {
            using var stream = new MemoryStream(bytes); using var workbook = SpreadsheetDocument.Open(stream, false);
            Assert.Empty(new OpenXmlValidator().Validate(workbook, TestContext.Current.CancellationToken));
            var cells = workbook.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<Cell>().ToArray();
            Assert.DoesNotContain(cells, cell => cell.CellFormula is not null);
            Assert.Equal(SpreadsheetDocumentType.Workbook, workbook.DocumentType);
            var date = workbook.WorkbookPart.WorksheetParts.Single().Worksheet.Descendants<Row>()
                .Single(row => row.Elements<Cell>().FirstOrDefault()?.InlineString?.InnerText == "Generado (Lima)").Elements<Cell>().Last();
            // 2026-10-09 23:30 in Lima; no conversion of monetary values through binary floating point.
            Assert.Equal(new DateOnly(2026, 10, 9).DayNumber - new DateOnly(1899, 12, 30).DayNumber + 23.5m / 24m,
                decimal.Parse(date.CellValue!.Text, CultureInfo.InvariantCulture));
        }
        else
        {
            using var pdf = PdfDocument.Open(bytes);
            Assert.Contains("09/10/2026 23:30:00", text);
            Assert.All(pdf.GetPages(), page =>
            {
                Assert.Contains($"Página {page.Number} de {pdf.NumberOfPages}", page.Text);
                Assert.All(page.Letters, letter => { Assert.True(letter.BoundingBox.Left >= 0); Assert.True(letter.BoundingBox.Right <= page.Width + 1); });
            });
        }
    }

    [Theory]
    [InlineData("=HYPERLINK(\"https://invalid\")")]
    [InlineData("+SUM(A1:A9)")]
    [InlineData("-1+2")]
    [InlineData("@SUM(A:A)")]
    public async Task UntrustedNamesAreLiteralInlineTextWithNoFormulasLinksOrMacros(string name)
    {
        var document = Document(OwnerReportType.Sales, name: name);
        var bytes = await new ReportDocumentRenderer().RenderAsync(document, ReportExportFormat.Xlsx, TestContext.Current.CancellationToken);
        using var stream = new MemoryStream(bytes); using var workbook = SpreadsheetDocument.Open(stream, false);
        var cells = workbook.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<Cell>().Where(cell => cell.InlineString?.InnerText == name).ToArray();
        Assert.Equal(2, cells.Length); // Selected-product metadata and the table row are both untrusted text.
        Assert.All(cells, cell => { Assert.Equal(CellValues.InlineString, cell.DataType!.Value); Assert.Null(cell.CellFormula); });
        Assert.Null(workbook.WorkbookPart.VbaProjectPart);
        Assert.Empty(workbook.WorkbookPart.ExternalRelationships);
        Assert.All(workbook.WorkbookPart.WorksheetParts, part => { Assert.Empty(part.ExternalRelationships); Assert.Empty(part.HyperlinkRelationships); });
    }

    [Fact]
    public async Task PdfEmbedsTheLicensedFontIntactSoCompositeAccentsAreNotLost()
    {
        var bytes = await new ReportDocumentRenderer().RenderAsync(Document(OwnerReportType.Commissions), ReportExportFormat.Pdf, TestContext.Current.CancellationToken);
        using var stream = new MemoryStream(bytes);
        using var pdf = PdfSharp.Pdf.IO.PdfReader.Open(stream, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        var descriptors = pdf.Internals.GetAllObjects().OfType<PdfSharp.Pdf.PdfDictionary>().Where(value => value.Elements.ContainsKey("/FontFile2")).ToArray();
        Assert.NotEmpty(descriptors);
        using var resource = typeof(ReportDocumentRenderer).Assembly.GetManifestResourceStream("MediPOS.Infrastructure.Modules.Reporting.Export.Fonts.NotoSans-Regular.ttf")!;
        using var font = new MemoryStream(); await resource.CopyToAsync(font, TestContext.Current.CancellationToken);
        var expected = SHA256.HashData(font.ToArray());
        Assert.All(descriptors, descriptor =>
        {
            var embedded = Assert.IsAssignableFrom<PdfSharp.Pdf.PdfDictionary>(descriptor.Elements.GetReference("/FontFile2")!.Value);
            Assert.Equal(expected, SHA256.HashData(embedded.Stream.UnfilteredValue));
        });
    }

    [Fact]
    public async Task XlsxNumericsRetainLargeExactDecimalsRatherThanClosedXmlDoubleValues()
    {
        const decimal amount = 123456789012345678901234.5678m;
        var document = Document(OwnerReportType.Sales, amount: amount);
        var bytes = await new ReportDocumentRenderer().RenderAsync(document, ReportExportFormat.Xlsx, TestContext.Current.CancellationToken);
        using var stream = new MemoryStream(bytes); using var workbook = SpreadsheetDocument.Open(stream, false);
        var cells = workbook.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<Cell>().Where(cell => cell.StyleIndex?.Value == 1).ToArray();
        Assert.Equal(2, cells.Length);
        Assert.All(cells, cell => { Assert.Equal(CellValues.Number, cell.DataType!.Value); Assert.Equal(amount, decimal.Parse(cell.CellValue!.Text, CultureInfo.InvariantCulture)); });
        var pdf = await new ReportDocumentRenderer().RenderAsync(document, ReportExportFormat.Pdf, TestContext.Current.CancellationToken);
        Assert.Contains("123456789012345678901234.5678", Text(pdf, ReportExportFormat.Pdf));
    }

    [Theory]
    [InlineData(ReportExportFormat.Xlsx)]
    [InlineData(ReportExportFormat.Pdf)]
    public async Task EmptyReportStillContainsFiltersAndExactZeroTotals(ReportExportFormat format)
    {
        var document = Document(OwnerReportType.Sales, count: 0);
        var bytes = await new ReportDocumentRenderer().RenderAsync(document, format, TestContext.Current.CancellationToken);
        var text = Text(bytes, format);
        Assert.Contains("Sin resultados para los filtros aplicados", text); Assert.Contains("Importe total PEN", text);
        Assert.Contains("Solo líneas coincidentes", text);
    }

    [Theory]
    [InlineData(ReportExportFormat.Xlsx)]
    [InlineData(ReportExportFormat.Pdf)]
    public async Task LongNamesAndManyRowsRemainCompleteAndPdfRepeatsHeaders(ReportExportFormat format)
    {
        var document = Document(OwnerReportType.Sales, count: 140, name: new string('Ñ', 180));
        var bytes = await new ReportDocumentRenderer().RenderAsync(document, format, TestContext.Current.CancellationToken);
        if (format == ReportExportFormat.Pdf)
        {
            using var pdf = PdfDocument.Open(bytes); Assert.True(pdf.NumberOfPages > 2);
            Assert.All(pdf.GetPages().Where(page => page.Text.Contains('Ñ')), page =>
            {
                Assert.Contains("Ventas distintas", page.Text); Assert.Contains("Importe PEN", page.Text);
                Assert.All(page.Letters, letter => Assert.InRange(letter.BoundingBox.Right, 0, page.Width + 1));
            });
            Assert.Contains("140", pdf.GetPage(pdf.NumberOfPages).Text);
        }
        else
        {
            using var stream = new MemoryStream(bytes); using var workbook = SpreadsheetDocument.Open(stream, false);
            Assert.Equal(140, workbook.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<Cell>().Count(cell => cell.InlineString?.InnerText == new string('Ñ', 180)));
        }
    }

    [Fact]
    public async Task ExactlyTenThousandRowsAreExportedAndOneMoreIsRejected()
    {
        var document = Document(OwnerReportType.Sales, count: 10_000);
        ReportExportPolicy.Validate(document.Snapshot, document.Request);
        var bytes = await new ReportDocumentRenderer().RenderAsync(document, ReportExportFormat.Xlsx, TestContext.Current.CancellationToken);
        using var stream = new MemoryStream(bytes); using var workbook = SpreadsheetDocument.Open(stream, false);
        Assert.Equal(10_000, workbook.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<Cell>().Count(cell => cell.InlineString?.InnerText == "Producto Ñ"));
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => new ReportDocumentRenderer().RenderAsync(Document(OwnerReportType.Sales, count: 10_001), ReportExportFormat.Xlsx, TestContext.Current.CancellationToken));
        Assert.Equal("reports.export_too_large", error.Error.Code);
    }

    [Fact]
    public async Task RealWorkbookSerializationEnforcesTheTenMiBLimit()
    {
        var document = Document(OwnerReportType.Sales, count: 10_000);
        var data = (SalesExportData)document.Snapshot.Data;
        var random = new Random(481);
        var rows = data.Page.Rows.Select(row => row with { GroupName = Convert.ToBase64String(RandomBytes()) }).ToArray();
        byte[] RandomBytes() { var value = new byte[2000]; random.NextBytes(value); return value; }
        document = document with { Snapshot = document.Snapshot with { Data = new SalesExportData(data.Page with { Rows = rows }) } };
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => new ReportDocumentRenderer().RenderAsync(document, ReportExportFormat.Xlsx, TestContext.Current.CancellationToken));
        Assert.Equal("reports.export_too_large", error.Error.Code);
    }

    [Fact]
    public async Task LocalRenderersPreserveOriginalTotalsForEveryReport()
    {
        foreach (var type in Enum.GetValues<OwnerReportType>())
        {
            var document = Document(type);
            decimal[] expected = type switch
            {
                OwnerReportType.Sales => [1m, 1m, 9.1251m],
                OwnerReportType.Commissions => [1m, .2001m, .2001m, 0m],
                OwnerReportType.StockRisk => [1m, 1m],
                OwnerReportType.Expirations => [1m, 1m, 0m, 0m],
                OwnerReportType.InventoryCapital => [21.1333m, 1m, 9.1333m],
                _ => [1m, 9.1251m],
            };
            var bytes = await new ReportDocumentRenderer().RenderAsync(document, ReportExportFormat.Xlsx, TestContext.Current.CancellationToken);
            using var stream = new MemoryStream(bytes); using var workbook = SpreadsheetDocument.Open(stream, false);
            var rows = workbook.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<Row>().ToArray();
            var totalStart = Array.FindIndex(rows, row => row.Elements<Cell>().FirstOrDefault()?.InlineString?.InnerText == "Totales del conjunto completo") + 1;
            Assert.Equal(expected, rows.Skip(totalStart).Select(row => decimal.Parse(row.Elements<Cell>().Last().CellValue!.Text, CultureInfo.InvariantCulture)));
            var pdfBytes = await new ReportDocumentRenderer().RenderAsync(document, ReportExportFormat.Pdf, TestContext.Current.CancellationToken);
            using var pdf = PdfDocument.Open(pdfBytes);
            var numericWords = pdf.GetPages().SelectMany(page => page.GetWords()).Select(word => word.Text)
                .Where(word => decimal.TryParse(word, NumberStyles.AllowDecimalPoint, CultureInfo.GetCultureInfo("es-PE"), out _))
                .Select(word => decimal.Parse(word, CultureInfo.GetCultureInfo("es-PE"))).ToArray();
            foreach (var value in expected) Assert.Contains(value, numericWords);
        }
    }

    [Theory]
    [InlineData(ReportExportFormat.Xlsx)]
    [InlineData(ReportExportFormat.Pdf)]
    public async Task CancellationDuringGenerationNeverReturnsAnIncompleteDocument(ReportExportFormat format)
    {
        var document = Document(OwnerReportType.Sales, count: 10_000, name: new string('Ñ', 180));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(20));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ReportDocumentRenderer().RenderAsync(document, format, cancellation.Token));
    }

    [Fact]
    public async Task RendererHonorsCancellationBeforeGeneratingAFile()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ReportDocumentRenderer().RenderAsync(Document(OwnerReportType.Sales), ReportExportFormat.Pdf, cancellation.Token));
    }

    internal static string Text(byte[] bytes, ReportExportFormat format)
    {
        if (format == ReportExportFormat.Pdf) { using var pdf = PdfDocument.Open(bytes); return string.Join("\n", pdf.GetPages().Select(page => page.Text)); }
        using var stream = new MemoryStream(bytes); using var workbook = SpreadsheetDocument.Open(stream, false);
        return string.Join("\n", workbook.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<Cell>().Select(cell => cell.InlineString?.InnerText ?? cell.CellValue?.Text));
    }
    internal static ReportExportDocument Document(OwnerReportType type, int count = 1, string name = "Producto Ñ", decimal amount = 9.1251m)
    {
        var tenant = Guid.NewGuid(); var branch = Guid.NewGuid(); var product = Guid.NewGuid(); var category = Guid.NewGuid(); var employee = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 10, 10, 4, 30, 0, TimeSpan.Zero);
        var dated = type is OwnerReportType.Sales or OwnerReportType.Commissions or OwnerReportType.ProductRotation;
        var query = new ExportOwnerReportQuery(tenant, type, ReportExportFormat.Xlsx,
            Period: dated ? new(OperationalPeriodType.Day, new(2026, 10, 9)) : null,
            Dimension: type == OwnerReportType.Sales ? OwnerSalesDimension.Product : null,
            BusinessProductId: type == OwnerReportType.Sales && count <= 1 ? product : null,
            ExpirationRange: type == OwnerReportType.Expirations ? new(new(2026, 10, 1), new(2026, 11, 1)) : null,
            RotationMode: type == OwnerReportType.ProductRotation ? OwnerProductRotationMode.LowActivity : null);
        var request = ReportExportPolicy.CreateRequest(query, at);
        ReportExportData data = type switch
        {
            OwnerReportType.Sales => new SalesExportData(new(Enumerable.Range(0, count).Select(_ => new OwnerSalesRow { GroupId = count == 1 ? product : Guid.NewGuid(), GroupName = name, SalesAmount = amount, SaleCount = 1, BaseQuantitySold = 2m }).ToArray(), new(count, count, amount * count))),
            OwnerReportType.Commissions => new CommissionsExportData(new([new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), branch, employee, "Ana Ñ", product, name, at.AddHours(-1), .2001m, .2001m, 0m, at)], new(1, .2001m, .2001m, 0m))),
            OwnerReportType.StockRisk => new StockRiskExportData(new([new() { BranchId = branch, BranchName = "Centro", BusinessProductId = product, ProductName = name, IsProductActive = true, PhysicalStockBase = 8m, SellableStockBase = 5m, MinimumStockBase = 5m, IsCritical = true }], new(1, 1))),
            OwnerReportType.Expirations => new ExpirationsExportData(new([new() { InventoryLotId = Guid.NewGuid(), BusinessProductId = product, ProductName = name, BranchId = branch, BatchNumber = "LOT-Ñ", ExpirationDate = new(2026, 10, 8), QuantityAvailableBase = 3m, State = OwnerExpirationState.Expired }], new(1, 1, 0, 0))),
            OwnerReportType.InventoryCapital => new CapitalExportData(new([new() { BranchId = branch, TotalInventoryCapital = 21.1333m, ExpiredInventoryCapital = 1m, ExpiringInventoryCapital = 9.1333m }], new(21.1333m, 1m, 9.1333m))),
            _ => new RotationExportData(new([new() { BusinessProductId = product, ProductName = name, CategoryId = category, SalesCount = 1, BaseQuantitySold = 2m, SalesAmount = amount, CurrentSellableStockBase = 5m }], new(1, amount))),
        };
        var metadata = new ReportExportMetadata("Botica Ñ & <Centro>", new Dictionary<Guid, string> { [branch] = "Centro" }, new Dictionary<Guid, string> { [category] = "Categoría Ñ" }, "Ana Ñ", name, "Categoría Ñ");
        return new(request, new(data, metadata), ReportExportPolicy.Filters(request, metadata));
    }
}
