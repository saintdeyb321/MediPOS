using System.Text.Json;

namespace MediPOS.Domain.Modules.Catalog.OfficialImport;

public static class OfficialCatalogStates
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Staged = "staged";
    public const string Failed = "failed";
    public const string Valid = "valid";
    public const string Invalid = "invalid";
    public static bool IsBatch(string value) => value is Pending or Processing or Staged or Failed;
}

public sealed record OfficialCatalogSource(string Code, string Institution, string OfficialReference,
    string ReuseAuthorization, string Coverage, string RecordIdentifierDescription);
public sealed record OfficialCatalogBatchMetadata(OfficialCatalogSource Source, string Version, string FileName,
    string ChecksumSha256, DateTimeOffset? PublishedAtUtc, string InputFormat, Guid ActorId, string CorrelationId);
public sealed record OfficialCatalogAttemptFailure(int Attempt, string Code, DateTimeOffset AtUtc, Guid ActorId, string CorrelationId);

// Platform history: no tenant, inventory reference or future GlobalProduct identifier is fabricated.
public sealed class OfficialCatalogImportBatch
{
    public const int MaximumAttempts = 5;
    private OfficialCatalogImportBatch() { }
    public Guid Id { get; private set; }
    public string SourceCode { get; private set; } = string.Empty;
    public string SourceVersion { get; private set; } = string.Empty;
    public string SourceInstitution { get; private set; } = string.Empty;
    public string OfficialReference { get; private set; } = string.Empty;
    public string ReuseAuthorization { get; private set; } = string.Empty;
    public string Coverage { get; private set; } = string.Empty;
    public string RecordIdentifierDescription { get; private set; } = string.Empty;
    public string SourceFileName { get; private set; } = string.Empty;
    public string SourceChecksumSha256 { get; private set; } = string.Empty;
    public string InputFormat { get; private set; } = string.Empty;
    public DateTimeOffset? SourcePublishedAtUtc { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public string Status { get; private set; } = OfficialCatalogStates.Pending;
    public int TotalRows { get; private set; }
    public int ValidRows { get; private set; }
    public int InvalidRows { get; private set; }
    // DuplicateRows overlaps valid/invalid; it is not a third partition of TotalRows.
    public int DuplicateRows { get; private set; }
    public Guid StartedByActorId { get; private set; }
    public string StartedCorrelationId { get; private set; } = string.Empty;
    public string? FailureCode { get; private set; }
    public string FailureHistoryJson { get; private set; } = "[]";
    public int AttemptCount { get; private set; }
    public Guid? ProcessingLeaseId { get; private set; }
    public DateTimeOffset? ProcessingLeaseExpiresAtUtc { get; private set; }
    public Guid? LastAttemptByActorId { get; private set; }
    public string? LastAttemptCorrelationId { get; private set; }
    public DateTimeOffset? LastAttemptStartedAtUtc { get; private set; }
    public IReadOnlyList<OfficialCatalogAttemptFailure> Failures => JsonSerializer.Deserialize<OfficialCatalogAttemptFailure[]>(FailureHistoryJson)!;

    public static OfficialCatalogImportBatch Create(OfficialCatalogBatchMetadata metadata, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(metadata); ArgumentNullException.ThrowIfNull(metadata.Source);
        RequireActor(metadata.ActorId, metadata.CorrelationId);
        if (at.Offset != TimeSpan.Zero || at == default || metadata.PublishedAtUtc?.Offset != TimeSpan.Zero && metadata.PublishedAtUtc.HasValue)
            throw new ArgumentException("Catalog timestamps must be UTC.");
        var source = metadata.Source;
        var code = Text(source.Code, 64);
        if (!char.IsAsciiLetterLower(code[0]) || code.Any(c => !char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c is not ('_' or '-' or '.')))
            throw new ArgumentException("Source codes have a stable lowercase ASCII form.");
        var file = Text(metadata.FileName, 256);
        if (file.Any(c => c is '/' or '\\' or ':')) throw new ArgumentException("Store only the source file name, never a path.");
        RequireHash(metadata.ChecksumSha256);
        if (metadata.InputFormat != "csv_utf8_v1") throw new ArgumentException("Unsupported input schema.");
        return new()
        {
            Id = Guid.CreateVersion7(),
            SourceCode = code,
            SourceVersion = Text(metadata.Version, 128),
            SourceInstitution = Text(source.Institution, 256),
            OfficialReference = Text(source.OfficialReference, 1024),
            ReuseAuthorization = Text(source.ReuseAuthorization, 1024),
            Coverage = Text(source.Coverage, 1024),
            RecordIdentifierDescription = Text(source.RecordIdentifierDescription, 1024),
            SourceFileName = file,
            SourceChecksumSha256 = metadata.ChecksumSha256,
            InputFormat = metadata.InputFormat,
            SourcePublishedAtUtc = metadata.PublishedAtUtc,
            StartedAtUtc = at,
            StartedByActorId = metadata.ActorId,
            StartedCorrelationId = metadata.CorrelationId,
        };
    }
    public void Start(Guid lease, Guid actor, string correlation, DateTimeOffset now, DateTimeOffset expires)
    {
        RequireActor(actor, correlation); RequireTime(now);
        if (lease == Guid.Empty || expires.Offset != TimeSpan.Zero || expires <= now ||
            Status is not (OfficialCatalogStates.Pending or OfficialCatalogStates.Failed) || AttemptCount >= MaximumAttempts)
            throw new InvalidOperationException("The batch cannot start another attempt.");
        Status = OfficialCatalogStates.Processing; FailureCode = null; CompletedAtUtc = null; AttemptCount++;
        ProcessingLeaseId = lease; ProcessingLeaseExpiresAtUtc = expires; LastAttemptByActorId = actor;
        LastAttemptCorrelationId = correlation; LastAttemptStartedAtUtc = now;
    }
    public bool OwnsLiveLease(Guid lease, DateTimeOffset now) => Status == OfficialCatalogStates.Processing && ProcessingLeaseId == lease && ProcessingLeaseExpiresAtUtc > now;
    public void Progress(Guid lease, int valid, int invalid, int duplicates, DateTimeOffset now, DateTimeOffset expires)
    {
        RequireLease(lease, now);
        if (valid < 0 || invalid < 0 || valid + invalid < 1 || duplicates < 0 || duplicates > valid + invalid || expires <= now || expires.Offset != TimeSpan.Zero)
            throw new ArgumentException("Invalid staging progress.");
        TotalRows = checked(TotalRows + valid + invalid); ValidRows = checked(ValidRows + valid);
        InvalidRows = checked(InvalidRows + invalid); DuplicateRows = checked(DuplicateRows + duplicates);
        ProcessingLeaseExpiresAtUtc = expires;
    }
    public void Complete(Guid lease, int parsedRows, DateTimeOffset now)
    {
        RequireLease(lease, now);
        if (parsedRows < 1 || parsedRows != TotalRows || ValidRows + InvalidRows != parsedRows)
            throw new InvalidOperationException("A partial batch cannot be staged.");
        Finish(OfficialCatalogStates.Staged, now);
    }
    public void Fail(Guid lease, string code, DateTimeOffset now)
    {
        RequireTime(now);
        if (Status != OfficialCatalogStates.Processing || ProcessingLeaseId != lease) throw new InvalidOperationException("An obsolete attempt cannot alter the batch.");
        FailureCode = Text(code, 96);
        FailureHistoryJson = JsonSerializer.Serialize(Failures.Append(new(AttemptCount, FailureCode, now, LastAttemptByActorId!.Value, LastAttemptCorrelationId!)));
        Finish(OfficialCatalogStates.Failed, now);
    }
    private void Finish(string state, DateTimeOffset now)
    { Status = state; CompletedAtUtc = now; ProcessingLeaseId = null; ProcessingLeaseExpiresAtUtc = null; }
    private void RequireLease(Guid lease, DateTimeOffset now)
    { RequireTime(now); if (!OwnsLiveLease(lease, now)) throw new InvalidOperationException("A live processing lease is required."); }
    private void RequireTime(DateTimeOffset now)
    { if (now.Offset != TimeSpan.Zero || now < StartedAtUtc || now < LastAttemptStartedAtUtc || now < CompletedAtUtc) throw new ArgumentException("Invalid server time."); }
    internal static string Text(string value, int maximum)
    { if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value != value.Trim() || value.Any(char.IsControl)) throw new ArgumentException("Invalid bounded metadata."); return value; }
    internal static void RequireHash(string value)
    { if (value.Length != 64 || value.Any(c => !char.IsAsciiHexDigitLower(c))) throw new ArgumentException("A lowercase SHA-256 is required."); }
    internal static void RequireActor(Guid actor, string correlation)
    { if (actor == Guid.Empty || correlation.Length != 32 || correlation.All(c => c == '0') || correlation.Any(c => !char.IsAsciiHexDigitLower(c))) throw new ArgumentException("A verified server actor and trace are required."); }
}
