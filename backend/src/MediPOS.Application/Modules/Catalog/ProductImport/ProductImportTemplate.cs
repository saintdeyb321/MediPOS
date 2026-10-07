using MediPOS.Application.Errors;
using MediPOS.Domain.Modules.Catalog.ProductImport;

namespace MediPOS.Application.Modules.Catalog.ProductImport;

public static class ProductImportTemplate
{
    public const string Name = "MediPOS Product Import v1";
    public const string Version = "1";
    public const string SheetName = "Products";
    public const string FileName = "medipos-product-import-v1.xlsx";
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const int MetadataRow = 1;
    public const int VersionColumn = 2;
    public const int InstructionsRow = 2;
    public const int HeaderRow = 3;
    public const int FirstDataRow = 4;
    public const char ComponentSeparator = '|';
    public const string Instructions = "Datos desde fila 4. ProductType: retail/medicine; CategoryId: UUID de categoría activa; precios decimales con punto; IsActive: true/false (vacío=true). Medicine: listas ActiveIngredients y Strengths separadas por |, en el mismo orden. Retail: campos farmacéuticos vacíos. BaseUnitName crea una única unidad base activa.";
    public static IReadOnlyList<string> Columns { get; } = Array.AsReadOnly(new[]
    {
        nameof(ProductImportRow.InternalCode), nameof(ProductImportRow.ProductType), nameof(ProductImportRow.Name),
        nameof(ProductImportRow.CategoryId), nameof(ProductImportRow.BrandOrLaboratory), nameof(ProductImportRow.RetailPrice),
        nameof(ProductImportRow.BaseUnitName), nameof(ProductImportRow.Barcode), nameof(ProductImportRow.WholesalePrice),
        nameof(ProductImportRow.IsActive), nameof(ProductImportRow.ActiveIngredients), nameof(ProductImportRow.Strengths),
        nameof(ProductImportRow.DosageForm), nameof(ProductImportRow.Route), nameof(ProductImportRow.SanitaryRegistration),
    });
}

public static class ProductImportLimits
{
    public const int MaximumFileBytes = 5 * 1024 * 1024;
    public const int MaximumExpandedBytes = 32 * 1024 * 1024;
    public const int MaximumPartBytes = 16 * 1024 * 1024;
    public const int MaximumPackageEntries = 64;
    public const int MaximumRows = ImportJob.MaximumRows;
    public const int MaximumWorksheetRow = ProductImportTemplate.FirstDataRow + MaximumRows - 1;
    public const int MaximumColumns = 15;
    public const int MaximumCellCharacters = 4096;
    public const int MaximumStyles = 256;
    public const int MaximumXmlNodes = 1_000_000;
    public const int DefaultResultLimit = 100;
    public const int MaximumResultLimit = 200;
}

public sealed record ProductImportRow(int RowNumber, string InternalCode, string ProductType, string Name,
    string CategoryId, string BrandOrLaboratory, string RetailPrice, string BaseUnitName, string Barcode,
    string WholesalePrice, string IsActive, string ActiveIngredients, string Strengths, string DosageForm,
    string Route, string SanitaryRegistration, ApplicationError? CellError = null);

public sealed record ProductImportWorkbook(string TemplateVersion, IReadOnlyList<ProductImportRow> Rows);
public interface IProductImportWorkbookReader
{
    Task<ProductImportWorkbook> ReadAsync(Stream stream, string fileName, CancellationToken cancellationToken);
}

public interface IProductImportTemplateWriter
{
    Task<byte[]> GenerateAsync(CancellationToken cancellationToken);
}

public sealed record ProductImportTemplateFile(string FileName, string ContentType, byte[] Content);
public sealed class GenerateProductImportTemplateHandler(IProductImportTemplateWriter writer)
{
    public async Task<ProductImportTemplateFile> HandleAsync(CancellationToken cancellationToken) =>
        new(ProductImportTemplate.FileName, ProductImportTemplate.ContentType,
            await writer.GenerateAsync(cancellationToken).ConfigureAwait(false));
}
