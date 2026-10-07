namespace MediPOS.Domain.Modules.Catalog.ProductImport;

public sealed class ImportRowResult
{
    private ImportRowResult() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ImportJobId { get; private set; }
    public int RowNumber { get; private set; }
    public ImportRowStatus Status { get; private set; }
    public Guid? BusinessProductId { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    public static ImportRowResult Imported(ImportJob job, int rowNumber, BusinessProduct product)
    {
        ArgumentNullException.ThrowIfNull(product);
        var row = Create(job, rowNumber);
        if (product.TenantId != job.TenantId) throw new ArgumentException("Imported product belongs to a different tenant.", nameof(product));
        row.Status = ImportRowStatus.Imported;
        row.BusinessProductId = product.Id;
        return row;
    }

    public static ImportRowResult Invalid(ImportJob job, int rowNumber, string errorCode, string errorMessage)
    {
        var row = Create(job, rowNumber);
        row.Status = ImportRowStatus.Invalid;
        row.ErrorCode = CatalogFields.Required(errorCode, 64, nameof(errorCode));
        row.ErrorMessage = CatalogFields.Required(errorMessage, 256, nameof(errorMessage));
        return row;
    }

    private static ImportRowResult Create(ImportJob job, int rowNumber)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rowNumber);
        if (job.Status != ImportJobStatus.Processing) throw new ArgumentException("Only a processing job accepts rows.", nameof(job));
        return new() { Id = Guid.CreateVersion7(), TenantId = job.TenantId, ImportJobId = job.Id, RowNumber = rowNumber };
    }
}
