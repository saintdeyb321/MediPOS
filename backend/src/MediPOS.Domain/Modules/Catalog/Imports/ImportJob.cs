namespace MediPOS.Domain.Modules.Catalog.ProductImport;

public enum ImportJobStatus { Processing, Completed, Failed }
public enum ImportRowStatus { Imported, Invalid }

public static class ProductImportCodes
{
    public static string ToCode(ImportJobStatus status) => status switch
    {
        ImportJobStatus.Processing => "processing",
        ImportJobStatus.Completed => "completed",
        ImportJobStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static ImportJobStatus JobFromCode(string code) => code switch
    {
        "processing" => ImportJobStatus.Processing,
        "completed" => ImportJobStatus.Completed,
        "failed" => ImportJobStatus.Failed,
        _ => throw new InvalidOperationException("Unknown persisted import job status."),
    };

    public static string ToCode(ImportRowStatus status) => status switch
    {
        ImportRowStatus.Imported => "imported",
        ImportRowStatus.Invalid => "invalid",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static ImportRowStatus RowFromCode(string code) => code switch
    {
        "imported" => ImportRowStatus.Imported,
        "invalid" => ImportRowStatus.Invalid,
        _ => throw new InvalidOperationException("Unknown persisted import row status."),
    };
}

public sealed class ImportJob
{
    public const int MaximumRows = 5000;
    public const int MaximumFileNameLength = 256;
    private ImportJob() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public ImportJobStatus Status { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string TemplateVersion { get; private set; } = string.Empty;
    public int TotalRows { get; private set; }
    public int ValidRows { get; private set; }
    public int InvalidRows { get; private set; }
    public int ImportedRows { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public Guid ActorId { get; private set; }

    public static ImportJob Create(Guid tenantId, Guid actorId, string fileName, string version, int totalRows, DateTimeOffset at)
    {
        if (tenantId == Guid.Empty || actorId == Guid.Empty) throw new ArgumentException("A tenant and server actor are required.");
        if (totalRows < 1 || totalRows > MaximumRows) throw new ArgumentOutOfRangeException(nameof(totalRows));
        var name = CatalogFields.Required(fileName, MaximumFileNameLength, nameof(fileName));
        if (name.Any(value => value is '/' or '\\' || char.IsControl(value))) throw new ArgumentException("Only a file name is stored.", nameof(fileName));
        return new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            ActorId = actorId,
            FileName = name,
            TemplateVersion = CatalogFields.Required(version, 32, nameof(version)),
            TotalRows = totalRows,
            Status = ImportJobStatus.Processing,
            CreatedAt = at.ToUniversalTime(),
        };
    }

    public void Complete(int imported, int invalid, DateTimeOffset at) => Finish(imported, invalid, at, ImportJobStatus.Completed);
    public void Fail(int imported, int invalid, DateTimeOffset at) => Finish(imported, invalid, at, ImportJobStatus.Failed);

    private void Finish(int imported, int invalid, DateTimeOffset at, ImportJobStatus status)
    {
        if (Status != ImportJobStatus.Processing) throw new InvalidOperationException("An import job is terminal once finished.");
        if (imported < 0 || invalid < 0 || imported > TotalRows - invalid ||
            (status == ImportJobStatus.Completed && imported != TotalRows - invalid))
            throw new ArgumentOutOfRangeException(nameof(imported), "Counts must match persisted row outcomes.");
        ArgumentOutOfRangeException.ThrowIfLessThan(at, CreatedAt);
        // Final valid rows are those actually imported, including the outcome of database uniqueness races.
        ValidRows = ImportedRows = imported;
        InvalidRows = invalid;
        Status = status;
        CompletedAt = at.ToUniversalTime();
    }
}
