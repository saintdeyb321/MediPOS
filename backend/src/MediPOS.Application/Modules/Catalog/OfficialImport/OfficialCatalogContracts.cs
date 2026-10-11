using MediPOS.Domain.Modules.Catalog.OfficialImport;

namespace MediPOS.Application.Modules.Catalog.OfficialImport;

// This adapter must authenticate AND authorize an internal platform administrator on the server.
// Tenant roles, request ActorId values and a commercial database credential cannot implement this authority.
public sealed record OfficialCatalogActor(Guid ActorId, string CorrelationId);
public interface IOfficialCatalogAdministration
{ Task<OfficialCatalogActor?> GetAuthorizedActorAsync(CancellationToken cancellationToken); }
public interface IOfficialCatalogSourceRegistry
{ OfficialCatalogSource? FindAuthorizedSource(string sourceCode); }

public sealed record StageOfficialCatalogImportCommand(string SourceCode, string SourceVersion, DateTimeOffset? PublishedAtUtc,
    string FileName, string InputFormat, Stream InputStream);
public sealed record GetOfficialCatalogImportBatchCommand(Guid BatchId);
public sealed record GetOfficialCatalogStagingRowsCommand(Guid BatchId, string? Status = null, string? ErrorCode = null, int Offset = 0, int Limit = 100);
public sealed record OfficialCatalogBatchResult(Guid Id, string SourceCode, string SourceVersion, string SourceFileName,
    string SourceChecksumSha256, DateTimeOffset? SourcePublishedAtUtc, string InputFormat, string SourceInstitution,
    string OfficialReference, string ReuseAuthorization, string Coverage, string RecordIdentifierDescription,
    DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc, string Status, int TotalRows, int ValidRows, int InvalidRows,
    int DuplicateRows, Guid StartedByActorId, int AttemptCount, string? FailureCode, IReadOnlyList<OfficialCatalogAttemptFailure> Failures);
public sealed record OfficialCatalogStagingRowResult(Guid Id, int RowNumber, string? SourceRecordId, string OriginalName,
    string? OriginalLaboratory, string? OriginalRegistration, string? OriginalPresentation, string? OriginalIngredientsText,
    string? OriginalStrengthText, string? OriginalDosageFormText, string? OriginalRouteText, IReadOnlyDictionary<string, string> RawFields,
    string ValidationStatus, IReadOnlyList<string> ValidationErrorCodes, IReadOnlyList<string> DuplicateSignals, int? DuplicateOfRowNumber, DateTimeOffset CreatedAtUtc);
public sealed record OfficialCatalogStagingPage(OfficialCatalogBatchResult Batch, IReadOnlyList<OfficialCatalogStagingRowResult> Rows, int Offset, int Limit, bool HasMore);

public sealed record OfficialCatalogImportOptions
{
    public long MaximumFileBytes { get; init; } = 16 * 1024 * 1024;
    public int MaximumRows { get; init; } = 100000;
    public int WritePageSize { get; init; } = 200;
    public TimeSpan ProcessingLease { get; init; } = TimeSpan.FromMinutes(2);
    public string TemporaryDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, ".official-catalog-spool");
    public void Validate()
    {
        if (MaximumFileBytes is < 1 or > 128 * 1024 * 1024 || MaximumRows is < 1 or > 1000000 || WritePageSize is < 1 or > 500 ||
            ProcessingLease < TimeSpan.FromSeconds(10) || ProcessingLease > TimeSpan.FromMinutes(15) || !Path.IsPathFullyQualified(TemporaryDirectory))
            throw new ArgumentException("Invalid bounded import configuration.");
    }
}
public interface IOfficialCatalogPreparedFile : IAsyncDisposable
{ string ChecksumSha256 { get; } Stream Content { get; } }
public interface IOfficialCatalogFilePreparer
{ Task<IOfficialCatalogPreparedFile> PrepareAsync(Stream input, OfficialCatalogImportOptions options, CancellationToken token); }
public sealed record OfficialCatalogSourceRow(int RowNumber, IReadOnlyDictionary<string, string> Fields);
public interface IOfficialCatalogFileReader
{ IAsyncEnumerable<OfficialCatalogSourceRow> ReadAsync(Stream input, OfficialCatalogImportOptions options, CancellationToken token); }
public sealed record OfficialCatalogClaim(OfficialCatalogImportBatch Batch, Guid? LeaseId);
public sealed record OfficialCatalogStoredPage(OfficialCatalogImportBatch? Batch, IReadOnlyList<OfficialCatalogStagingRow> Rows);
public interface IOfficialCatalogImportStore
{
    Task<OfficialCatalogClaim> ClaimAsync(OfficialCatalogImportBatch candidate, OfficialCatalogActor actor, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken token);
    Task AppendAsync(Guid batch, Guid lease, IReadOnlyList<OfficialCatalogStagingRow> rows, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken token);
    Task<OfficialCatalogImportBatch> CompleteAsync(Guid batch, Guid lease, int parsedRows, DateTimeOffset now, CancellationToken token);
    Task<bool> FailAsync(Guid batch, Guid lease, string failureCode, DateTimeOffset now, CancellationToken token);
    Task<OfficialCatalogImportBatch?> FindAsync(Guid batch, CancellationToken token);
    Task<OfficialCatalogStoredPage> ReadPageAsync(GetOfficialCatalogStagingRowsCommand query, int take, CancellationToken token);
}
public sealed record OfficialCatalogImportObservation(Guid? ImportBatchId, string SourceCode, string SourceVersion, int Rows, long DurationMilliseconds, string Result, string? FailureCode);
public interface IOfficialCatalogImportObserver
{ void Record(OfficialCatalogImportObservation observation); }
