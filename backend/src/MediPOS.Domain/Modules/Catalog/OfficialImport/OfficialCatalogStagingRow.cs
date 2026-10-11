using System.Text.Json;

namespace MediPOS.Domain.Modules.Catalog.OfficialImport;

public sealed class OfficialCatalogStagingRow
{
    private OfficialCatalogStagingRow() { }
    public Guid Id { get; private set; }
    public Guid ImportBatchId { get; private set; }
    public int RowNumber { get; private set; }
    public string? SourceRecordId { get; private set; }
    public string? SourceRecordIdFingerprint { get; private set; }
    public string OriginalName { get; private set; } = string.Empty;
    public string? OriginalLaboratory { get; private set; }
    public string? OriginalRegistration { get; private set; }
    public string? OriginalPresentation { get; private set; }
    public string? OriginalIngredientsText { get; private set; }
    public string? OriginalStrengthText { get; private set; }
    public string? OriginalDosageFormText { get; private set; }
    public string? OriginalRouteText { get; private set; }
    public string RawFieldsJson { get; private set; } = string.Empty;
    public string RowChecksumSha256 { get; private set; } = string.Empty;
    public string PotentialDuplicateFingerprint { get; private set; } = string.Empty;
    public string ValidationStatus { get; private set; } = OfficialCatalogStates.Valid;
    public string[] ValidationErrorCodes { get; private set; } = [];
    public string[] DuplicateSignals { get; private set; } = [];
    public int? DuplicateOfRowNumber { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static OfficialCatalogStagingRow Create(Guid batch, int rowNumber, IReadOnlyDictionary<string, string> fields,
        string rawFieldsJson, string checksum, string potentialFingerprint, string? sourceIdFingerprint, IEnumerable<string> errors, DateTimeOffset now)
    {
        if (batch == Guid.Empty || rowNumber < 2 || now.Offset != TimeSpan.Zero || now == default) throw new ArgumentException("Invalid staging identity/time.");
        ArgumentNullException.ThrowIfNull(fields); ArgumentNullException.ThrowIfNull(errors);
        if (fields.Count > 32 || fields.Any(field => field.Key.Length is < 1 or > 128 || field.Value.Length > 8192 || field.Value.Contains('\0', StringComparison.Ordinal)) ||
            rawFieldsJson.Length > 262144) throw new ArgumentException("Source fields exceed the storage envelope.");
        using var document = JsonDocument.Parse(rawFieldsJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.EnumerateObject().Count() != fields.Count ||
            document.RootElement.EnumerateObject().Any(property => property.Value.ValueKind != JsonValueKind.String || !fields.TryGetValue(property.Name, out var value) || value != property.Value.GetString()))
            throw new ArgumentException("Raw fields must preserve every parsed source value.");
        OfficialCatalogImportBatch.RequireHash(checksum); OfficialCatalogImportBatch.RequireHash(potentialFingerprint);
        if (sourceIdFingerprint is not null) OfficialCatalogImportBatch.RequireHash(sourceIdFingerprint);
        var codes = errors.Distinct(StringComparer.Ordinal).ToArray(); RequireCodes(codes);
        string? Field(string key) => fields.TryGetValue(key, out var value) && value.Length != 0 ? value : null;
        return new()
        {
            Id = Guid.CreateVersion7(),
            ImportBatchId = batch,
            RowNumber = rowNumber,
            SourceRecordId = Field("SourceRecordId"),
            SourceRecordIdFingerprint = sourceIdFingerprint,
            OriginalName = Field("Name") ?? string.Empty,
            OriginalLaboratory = Field("Laboratory"),
            OriginalRegistration = Field("Registration"),
            OriginalPresentation = Field("Presentation"),
            OriginalIngredientsText = Field("Ingredients"),
            OriginalStrengthText = Field("Strength"),
            OriginalDosageFormText = Field("DosageForm"),
            OriginalRouteText = Field("Route"),
            RawFieldsJson = rawFieldsJson,
            RowChecksumSha256 = checksum,
            PotentialDuplicateFingerprint = potentialFingerprint,
            ValidationErrorCodes = codes,
            ValidationStatus = codes.Length == 0 ? OfficialCatalogStates.Valid : OfficialCatalogStates.Invalid,
            CreatedAtUtc = now,
        };
    }
    // Only while assembling a new row, before persistence. Source text and existing rows never change.
    public void MarkDuplicate(int originalRowNumber, IEnumerable<string> signals)
    {
        if (originalRowNumber < 2 || originalRowNumber >= RowNumber || DuplicateSignals.Length != 0) throw new ArgumentException("Invalid duplicate anchor.");
        var supplied = signals.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (supplied.Length == 0 || supplied.Any(value => value is not ("source_id" or "exact_row" or "potential"))) throw new ArgumentException("Unknown duplicate signal.");
        DuplicateSignals = supplied; DuplicateOfRowNumber = originalRowNumber;
        ValidationErrorCodes = ValidationErrorCodes.Concat(supplied.Where(value => value != "potential").Select(value => "official_catalog.duplicate_" + value)).Distinct(StringComparer.Ordinal).ToArray();
        RequireCodes(ValidationErrorCodes); ValidationStatus = ValidationErrorCodes.Length == 0 ? OfficialCatalogStates.Valid : OfficialCatalogStates.Invalid;
    }
    private static void RequireCodes(string[] codes)
    { if (codes.Length > 16 || codes.Any(code => code.Length is < 1 or > 96 || code.Any(c => !char.IsAsciiLetterLower(c) && c is not ('_' or '.')))) throw new ArgumentException("Invalid validation codes."); }
}
