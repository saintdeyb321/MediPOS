using MediPOS.Application.Errors;

namespace MediPOS.Application.Modules.Catalog.ProductImport;

public static class ProductImportErrors
{
    public static readonly ApplicationError EmptyFile = New("empty_file", "The workbook has no data to import.");
    public static readonly ApplicationError InvalidFile = New("invalid_file", "A valid XLSX workbook is required.");
    public static readonly ApplicationError FileLimit = New("file_limit", "The workbook exceeds the supported file, row or cell limits.");
    public static readonly ApplicationError UnsafeWorkbook = New("unsafe_workbook", "Only literal values are accepted; formulas, external links and embedded content are not supported.");
    public static readonly ApplicationError TemplateVersion = New("template_version", "The workbook template or version is not supported.");
    public static readonly ApplicationError Headers = New("invalid_headers", "The workbook headers must contain each v1 column exactly once.");
    public static readonly ApplicationError MissingCode = New("missing_internal_code", "InternalCode is required.");
    public static readonly ApplicationError ProductType = New("invalid_product_type", "ProductType must be medicine or retail.");
    public static readonly ApplicationError Category = New("category_not_found", "CategoryId must identify an existing active category.");
    public static readonly ApplicationError DuplicateCode = New("duplicate_internal_code", "InternalCode conflicts with another row or an existing tenant product.");
    public static readonly ApplicationError Price = New("invalid_price", "Prices must be exact nonnegative decimals that fit the catalog price limits.");
    public static readonly ApplicationError Active = New("invalid_is_active", "IsActive must be true or false, or empty for true.");
    public static readonly ApplicationError Components = New("medicine_components_mismatch", "Medicine requires matching nonempty ingredient and strength lists separated by |.");
    public static readonly ApplicationError Medicine = New("invalid_medicine", "Medicine metadata does not satisfy the catalog rules.");
    public static readonly ApplicationError RetailMedicine = New("retail_medicine_fields", "Pharmaceutical fields must be empty for retail products.");
    public static readonly ApplicationError Name = New("invalid_name", "Name is required and must fit the catalog limit.");
    public static readonly ApplicationError Brand = New("invalid_brand", "BrandOrLaboratory is required and must fit the catalog limit.");
    public static readonly ApplicationError Barcode = New("invalid_barcode", "Barcode exceeds the catalog limit.");
    public static readonly ApplicationError Code = New("invalid_internal_code", "InternalCode exceeds the catalog limit.");
    public static readonly ApplicationError BaseUnit = New("invalid_base_unit", "BaseUnitName is required and must fit the catalog unit limit.");
    public static readonly ApplicationError Row = New("invalid_row", "The row does not satisfy the catalog rules.");
    public static readonly ApplicationError CellValue = New("invalid_cell_value", "Data cells must contain literal text, numbers or booleans, without Excel errors or dates.");
    public static readonly ApplicationError JobNotFound = new("import.job_not_found", ErrorCategory.NotFound, "Import job was not found for the tenant.");
    public static readonly ApplicationError ProcessingFailed = new("import.processing_failed", ErrorCategory.Conflict, "The import could not finish; previously committed rows remain traceable.");
    private static ApplicationError New(string code, string message) => new("import." + code, ErrorCategory.Validation, message);
}
