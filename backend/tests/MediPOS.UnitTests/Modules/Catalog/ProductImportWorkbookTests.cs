using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ClosedXML.Excel;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog.ProductImport;
using WorkbookAdapter = MediPOS.Infrastructure.Modules.Catalog.ProductImport.ProductImportWorkbook;
using XmlSaveOptions = System.Xml.Linq.SaveOptions;

namespace MediPOS.UnitTests.Modules.Catalog;

public sealed class ProductImportWorkbookTests
{
    private static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly Guid Category = Guid.NewGuid();
    private static readonly int[] ExpectedRows = [4, 6];

    [Fact]
    public async Task TemplateIsRealVersionedXlsxWithOneSheetStableHeadersTextFormatsAndNoExampleProduct()
    {
        var result = await new GenerateProductImportTemplateHandler(new WorkbookAdapter()).HandleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ProductImportTemplate.FileName, result.FileName);
        Assert.Equal(ProductImportTemplate.ContentType, result.ContentType);
        using var source = new MemoryStream(result.Content);
        using var workbook = new XLWorkbook(source, WorkbookAdapter.CreateLoadOptions());
        var sheet = Assert.Single(workbook.Worksheets);
        Assert.Equal(ProductImportTemplate.SheetName, sheet.Name);
        Assert.Equal("1", sheet.Cell(ProductImportTemplate.MetadataRow, ProductImportTemplate.VersionColumn).GetString());
        Assert.Equal(ProductImportTemplate.Columns,
            Enumerable.Range(1, ProductImportLimits.MaximumColumns).Select(column => sheet.Cell(ProductImportTemplate.HeaderRow, column).GetString()));
        Assert.Contains("|", sheet.Cell(ProductImportTemplate.InstructionsRow, 1).GetString(), StringComparison.Ordinal);
        Assert.Equal("@", sheet.Column(1).Style.NumberFormat.Format);
        source.Position = 0;
        Assert.Empty((await new WorkbookAdapter().ReadAsync(source, result.FileName, TestContext.Current.CancellationToken)).Rows);
        Assert.True(source.CanRead);
    }

    [Fact]
    public async Task RealLiteralRowsKeepCodesAccentsAndIngredientStrengthAssociationAndSkipEmptyRows()
    {
        var bytes = await CreateAsync(sheet =>
        {
            AddRetail(sheet, 4);
            AddRetail(sheet, 6);
            Set(sheet, 6, "ProductType", "medicine");
            Set(sheet, 6, "ActiveIngredients", "Paracetamol | Cafeína");
            Set(sheet, 6, "Strengths", "500 mg | 30 mg");
            Set(sheet, 6, "DosageForm", "Tableta");
        });
        var result = await ReadAsync(bytes);
        Assert.Equal(ExpectedRows, result.Rows.Select(value => value.RowNumber));
        Assert.Equal("001234", result.Rows[0].Barcode);
        Assert.Equal("Jabón", result.Rows[0].Name);
        Assert.Equal("Paracetamol | Cafeína", result.Rows[1].ActiveIngredients);
        Assert.Equal("500 mg | 30 mg", result.Rows[1].Strengths);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("sheet")]
    [InlineData("extra_sheet")]
    public async Task UnknownTemplateVersionOrSheetIsRejected(string kind)
    {
        var bytes = await CreateAsync(sheet =>
        {
            if (kind == "version") sheet.Cell(ProductImportTemplate.MetadataRow, ProductImportTemplate.VersionColumn).Value = "2";
            if (kind == "sheet") sheet.Name = "Other";
            if (kind == "extra_sheet") sheet.Workbook.AddWorksheet("Other");
        });
        Assert.Equal(ProductImportErrors.TemplateVersion, (await Assert.ThrowsAsync<ApplicationErrorException>(() => ReadAsync(bytes))).Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("InternalCode")]
    [InlineData("TenantId")]
    [InlineData("BusinessProductId")]
    [InlineData("GlobalProductId")]
    [InlineData("Stock")]
    [InlineData("Cost")]
    [InlineData("InventoryLot")]
    public async Task MissingDuplicateOrNonImportableHeaderIsRejected(string header)
    {
        var bytes = await CreateAsync(sheet => sheet.Cell(ProductImportTemplate.HeaderRow, 2).Value = header);
        Assert.Equal(ProductImportErrors.Headers, (await Assert.ThrowsAsync<ApplicationErrorException>(() => ReadAsync(bytes))).Error);
    }

    [Fact]
    public async Task FormulaIsRejectedBeforeAnyValueCanBeEvaluated()
    {
        var bytes = await CreateAsync(sheet =>
        {
            AddRetail(sheet, 4);
            sheet.Cell(4, Column("RetailPrice")).FormulaA1 = "WEBSERVICE(\"https://example.invalid/never-fetch\")";
        });
        Assert.Equal(ProductImportErrors.UnsafeWorkbook, (await Assert.ThrowsAsync<ApplicationErrorException>(() => ReadAsync(bytes))).Error);
    }

    [Fact]
    public async Task ExcelErrorsAreReturnedAsRowValidationErrorsWhileOtherRowsAreParsed()
    {
        var bytes = await CreateAsync(sheet =>
        {
            AddRetail(sheet, 4);
            AddRetail(sheet, 5);
            sheet.Cell(4, Column("RetailPrice")).Value = XLError.DivisionByZero;
        });
        var rows = (await ReadAsync(bytes)).Rows;
        Assert.Equal(2, rows.Count);
        Assert.Equal(ProductImportErrors.CellValue, rows[0].CellError);
        Assert.Null(rows[1].CellError);
    }

    [Fact]
    public async Task NumericXmlLiteralIsKeptExactlyInsteadOfGoingThroughBinaryFloatingPoint()
    {
        var bytes = await CreateAsync(sheet => AddRetail(sheet, 4));
        bytes = Rewrite(bytes, "xl/worksheets/sheet1.xml", text =>
        {
            var xml = XDocument.Parse(text);
            var cell = xml.Descendants(Spreadsheet + "c").Single(value => (string?)value.Attribute("r") == "F4");
            cell.SetAttributeValue("t", "n");
            cell.RemoveNodes();
            cell.Add(new XElement(Spreadsheet + "v", "99999999999999.9999"));
            return xml.ToString(XmlSaveOptions.DisableFormatting);
        });
        Assert.Equal("99999999999999.9999", Assert.Single((await ReadAsync(bytes)).Rows).RetailPrice);
    }

    [Theory]
    [InlineData("macro")]
    [InlineData("object")]
    [InlineData("external")]
    [InlineData("doctype")]
    public async Task ActivePackageContentOrExternalRelationshipsAreRejectedWithoutOpeningReferences(string kind)
    {
        var bytes = await CreateAsync(_ => { });
        if (kind is "macro" or "object")
            bytes = Rewrite(bytes, kind == "macro" ? "xl/vbaProject.bin" : "xl/embeddings/oleObject1.bin", _ => "ignored");
        else if (kind == "external")
            bytes = Rewrite(bytes, "xl/_rels/workbook.xml.rels", text =>
            {
                var xml = XDocument.Parse(text);
                xml.Root!.Add(new XElement(xml.Root.Name.Namespace + "Relationship", new XAttribute("Id", "external"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink"),
                    new XAttribute("TargetMode", "External"), new XAttribute("Target", "https://example.invalid/never-fetch")));
                return xml.ToString(XmlSaveOptions.DisableFormatting);
            });
        else
            bytes = Rewrite(bytes, "xl/worksheets/sheet1.xml", _ =>
                "<!DOCTYPE worksheet [<!ENTITY external SYSTEM 'https://example.invalid/never-fetch'>]><worksheet>&external;</worksheet>");
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => ReadAsync(bytes));
        Assert.Equal(kind == "doctype" ? ProductImportErrors.InvalidFile : ProductImportErrors.UnsafeWorkbook, error.Error);
    }

    [Fact]
    public async Task FileBytesExpansionRowsAndColumnsAreBoundedBeforeLoadingWorkbook()
    {
        Assert.Equal(ProductImportErrors.FileLimit, (await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            ReadAsync(new byte[ProductImportLimits.MaximumFileBytes + 1]))).Error);
        var rows = await CreateAsync(sheet => AddRetail(sheet, ProductImportLimits.MaximumWorksheetRow + 1));
        Assert.Equal(ProductImportErrors.FileLimit, (await Assert.ThrowsAsync<ApplicationErrorException>(() => ReadAsync(rows))).Error);
        var columns = await CreateAsync(sheet => sheet.Cell(4, ProductImportLimits.MaximumColumns + 1).Value = "extra");
        Assert.Equal(ProductImportErrors.FileLimit, (await Assert.ThrowsAsync<ApplicationErrorException>(() => ReadAsync(columns))).Error);
        var compressed = Rewrite(await CreateAsync(_ => { }), "docProps/custom.xml",
            _ => new string('x', ProductImportLimits.MaximumPartBytes + 1));
        Assert.True(compressed.Length < ProductImportLimits.MaximumFileBytes);
        Assert.Equal(ProductImportErrors.FileLimit, (await Assert.ThrowsAsync<ApplicationErrorException>(() => ReadAsync(compressed))).Error);
    }

    [Fact]
    public async Task EmptyUnsupportedAndCorruptFilesHaveStablePublicErrors()
    {
        Assert.Equal(ProductImportErrors.EmptyFile, (await Assert.ThrowsAsync<ApplicationErrorException>(() => ReadAsync([]))).Error);
        Assert.Equal(ProductImportErrors.InvalidFile, (await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            ReadAsync(Encoding.UTF8.GetBytes("not an xlsx")))).Error);
        using var source = new MemoryStream(await CreateAsync(_ => { }));
        Assert.Equal(ProductImportErrors.InvalidFile, (await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            new WorkbookAdapter().ReadAsync(source, "import.xlsm", TestContext.Current.CancellationToken))).Error);
        var corrupt = Rewrite(await CreateAsync(_ => { }), "xl/worksheets/sheet1.xml", _ => "<worksheet>");
        Assert.Equal(ProductImportErrors.InvalidFile, (await Assert.ThrowsAsync<ApplicationErrorException>(() => ReadAsync(corrupt))).Error);
    }

    private static async Task<byte[]> CreateAsync(Action<IXLWorksheet> change)
    {
        using var source = new MemoryStream(await new WorkbookAdapter().GenerateAsync(TestContext.Current.CancellationToken));
        using var workbook = new XLWorkbook(source, WorkbookAdapter.CreateLoadOptions());
        change(workbook.Worksheet(ProductImportTemplate.SheetName));
        using var result = new MemoryStream();
        workbook.SaveAs(result, false, false);
        return result.ToArray();
    }

    private static void AddRetail(IXLWorksheet sheet, int row)
    {
        Set(sheet, row, "InternalCode", "R-" + row);
        Set(sheet, row, "ProductType", "retail");
        Set(sheet, row, "Name", "Jabón");
        Set(sheet, row, "CategoryId", Category.ToString("D"));
        Set(sheet, row, "BrandOrLaboratory", "Marca");
        Set(sheet, row, "RetailPrice", "1.2500");
        Set(sheet, row, "BaseUnitName", "Unidad");
        Set(sheet, row, "Barcode", "001234");
    }
    private static int Column(string name) => ProductImportTemplate.Columns.ToList().IndexOf(name) + 1;
    private static void Set(IXLWorksheet sheet, int row, string name, string value) => sheet.Cell(row, Column(name)).Value = value;
    private static async Task<MediPOS.Application.Modules.Catalog.ProductImport.ProductImportWorkbook> ReadAsync(byte[] bytes)
    {
        using var source = new MemoryStream(bytes);
        return await new WorkbookAdapter().ReadAsync(source, "items.xlsx", TestContext.Current.CancellationToken);
    }
    private static byte[] Rewrite(byte[] bytes, string part, Func<string, string> transform)
    {
        using var result = new MemoryStream();
        result.Write(bytes);
        result.Position = 0;
        using (var zip = new ZipArchive(result, ZipArchiveMode.Update, true))
        {
            var entry = zip.GetEntry(part);
            var text = string.Empty;
            if (entry is not null)
            {
                using (var reader = new StreamReader(entry.Open())) text = reader.ReadToEnd();
                entry.Delete();
            }
            var rewritten = zip.CreateEntry(part, CompressionLevel.SmallestSize);
            using var writer = new StreamWriter(rewritten.Open(), Encoding.UTF8);
            writer.Write(transform(text));
        }
        return result.ToArray();
    }
}
