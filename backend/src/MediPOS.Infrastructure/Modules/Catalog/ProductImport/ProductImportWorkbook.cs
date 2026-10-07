using System.Globalization;
using System.Xml;
using ClosedXML.Excel;
using ClosedXML.Graphics;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog.ProductImport;

namespace MediPOS.Infrastructure.Modules.Catalog.ProductImport;

public sealed class ProductImportWorkbook : IProductImportWorkbookReader, IProductImportTemplateWriter
{
    private static readonly Lazy<IXLGraphicEngine> GraphicEngine = new(() =>
    {
        // ClosedXML 0.105.1 bundles these metrics. No installed fonts or filesystem enumeration is needed.
        using var font = typeof(XLWorkbook).Assembly.GetManifestResourceStream("ClosedXML.Graphics.Fonts.CarlitoBare-Regular.ttf")
            ?? throw new InvalidOperationException("ClosedXML's bundled font metrics are required.");
        return DefaultGraphicEngine.CreateOnlyWithFonts(font);
    });

    public static LoadOptions CreateLoadOptions() => new() { GraphicEngine = GraphicEngine.Value, RecalculateAllFormulas = false };

    public Task<byte[]> GenerateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var workbook = new XLWorkbook(CreateLoadOptions());
        workbook.CalculateMode = XLCalculateMode.Manual;
        var sheet = workbook.AddWorksheet(ProductImportTemplate.SheetName);
        sheet.Columns(1, ProductImportLimits.MaximumColumns).Style.NumberFormat.Format = "@";
        sheet.Columns(1, ProductImportLimits.MaximumColumns).Width = 22;
        sheet.Cell(ProductImportTemplate.MetadataRow, 1).Value = ProductImportTemplate.Name;
        sheet.Cell(ProductImportTemplate.MetadataRow, ProductImportTemplate.VersionColumn).Value = ProductImportTemplate.Version;
        sheet.Range(ProductImportTemplate.InstructionsRow, 1, ProductImportTemplate.InstructionsRow, ProductImportLimits.MaximumColumns).Merge();
        sheet.Cell(ProductImportTemplate.InstructionsRow, 1).Value = ProductImportTemplate.Instructions;
        sheet.Row(ProductImportTemplate.InstructionsRow).Height = 45;
        sheet.Row(ProductImportTemplate.InstructionsRow).Style.Alignment.WrapText = true;
        for (var index = 0; index < ProductImportTemplate.Columns.Count; index++)
            sheet.Cell(ProductImportTemplate.HeaderRow, index + 1).Value = ProductImportTemplate.Columns[index];
        var headers = sheet.Range(ProductImportTemplate.HeaderRow, 1, ProductImportTemplate.HeaderRow, ProductImportLimits.MaximumColumns);
        headers.Style.Font.Bold = true;
        headers.Style.Fill.BackgroundColor = XLColor.DarkBlue;
        headers.Style.Font.FontColor = XLColor.White;
        sheet.SheetView.FreezeRows(ProductImportTemplate.HeaderRow);
        using var result = new MemoryStream();
        workbook.SaveAs(result, false, false);
        return Task.FromResult(result.ToArray());
    }

    public async Task<MediPOS.Application.Modules.Catalog.ProductImport.ProductImportWorkbook> ReadAsync(
        Stream stream, string fileName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new ApplicationErrorException(ProductImportErrors.InvalidFile);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + count > ProductImportLimits.MaximumFileBytes) throw new ApplicationErrorException(ProductImportErrors.FileLimit);
            await buffer.WriteAsync(chunk.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
        }
        if (buffer.Length == 0) throw new ApplicationErrorException(ProductImportErrors.EmptyFile);
        buffer.Position = 0;
        try
        {
            var numeric = ProductImportPackageValidation.Validate(buffer, cancellationToken);
            using var workbook = new XLWorkbook(buffer, CreateLoadOptions());
            if (workbook.Worksheets.Count != 1 || !workbook.Worksheets.TryGetWorksheet(ProductImportTemplate.SheetName, out var sheet))
                throw new ApplicationErrorException(ProductImportErrors.TemplateVersion);
            if (Literal(sheet.Cell(ProductImportTemplate.MetadataRow, 1), numeric) != ProductImportTemplate.Name ||
                Literal(sheet.Cell(ProductImportTemplate.MetadataRow, ProductImportTemplate.VersionColumn), numeric) != ProductImportTemplate.Version)
                throw new ApplicationErrorException(ProductImportErrors.TemplateVersion);
            var headers = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var column = 1; column <= ProductImportLimits.MaximumColumns; column++)
            {
                var header = Literal(sheet.Cell(ProductImportTemplate.HeaderRow, column), numeric).Trim();
                if (!ProductImportTemplate.Columns.Contains(header, StringComparer.Ordinal) || !headers.TryAdd(header, column))
                    throw new ApplicationErrorException(ProductImportErrors.Headers);
            }
            if (headers.Count != ProductImportTemplate.Columns.Count) throw new ApplicationErrorException(ProductImportErrors.Headers);
            var last = sheet.LastRowUsed(XLCellsUsedOptions.Contents)?.RowNumber() ?? ProductImportTemplate.HeaderRow;
            if (last > ProductImportLimits.MaximumWorksheetRow) throw new ApplicationErrorException(ProductImportErrors.FileLimit);
            var rows = new List<ProductImportRow>();
            for (var number = ProductImportTemplate.FirstDataRow; number <= last; number++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var cellError = headers.Values.Any(column => sheet.Cell(number, column).CachedValue.Type
                    is XLDataType.Error or XLDataType.DateTime or XLDataType.TimeSpan) ? ProductImportErrors.CellValue : null;
                string Value(string column) => Literal(sheet.Cell(number, headers[column]), numeric, true);
                var values = ProductImportTemplate.Columns.Select(Value).ToArray();
                if (cellError is null && values.All(string.IsNullOrWhiteSpace)) continue;
                rows.Add(new(number,
                    Value(nameof(ProductImportRow.InternalCode)), Value(nameof(ProductImportRow.ProductType)), Value(nameof(ProductImportRow.Name)),
                    Value(nameof(ProductImportRow.CategoryId)), Value(nameof(ProductImportRow.BrandOrLaboratory)), Value(nameof(ProductImportRow.RetailPrice)),
                    Value(nameof(ProductImportRow.BaseUnitName)), Value(nameof(ProductImportRow.Barcode)), Value(nameof(ProductImportRow.WholesalePrice)),
                    Value(nameof(ProductImportRow.IsActive)), Value(nameof(ProductImportRow.ActiveIngredients)), Value(nameof(ProductImportRow.Strengths)),
                    Value(nameof(ProductImportRow.DosageForm)), Value(nameof(ProductImportRow.Route)), Value(nameof(ProductImportRow.SanitaryRegistration)), cellError));
            }
            return new(ProductImportTemplate.Version, rows);
        }
        catch (ApplicationErrorException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Stable public error only; no parser exception text is exposed or persisted.
            throw new ApplicationErrorException(ProductImportErrors.InvalidFile, exception);
        }
    }

    private static string Literal(IXLCell cell, Dictionary<(int Row, int Column), string> numeric, bool dataCell = false)
    {
        if (cell.HasFormula) throw new ApplicationErrorException(ProductImportErrors.UnsafeWorkbook);
        // CachedValue never evaluates formulas. Numeric literals come directly from bounded XML, preserving decimal precision.
        var value = cell.CachedValue;
        if (value.Type == XLDataType.Number && numeric.TryGetValue((cell.Address.RowNumber, cell.Address.ColumnNumber), out var raw)) return raw;
        return value.Type switch
        {
            XLDataType.Blank => string.Empty,
            XLDataType.Boolean => value.GetBoolean() ? "true" : "false",
            XLDataType.Text => value.GetText(),
            XLDataType.DateTime => value.GetDateTime().ToString("O", CultureInfo.InvariantCulture),
            XLDataType.TimeSpan => value.GetTimeSpan().ToString("c", CultureInfo.InvariantCulture),
            _ => dataCell ? string.Empty : throw new ApplicationErrorException(ProductImportErrors.InvalidFile),
        };
    }
}
