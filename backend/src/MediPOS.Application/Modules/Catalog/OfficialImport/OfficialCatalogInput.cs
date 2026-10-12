using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediPOS.Application.Errors;
using MediPOS.Domain.Modules.Catalog.OfficialImport;

namespace MediPOS.Application.Modules.Catalog.OfficialImport;

public static class OfficialCatalogInput
{
    public const string Format = "csv_utf8_v1";
    public const int MaximumColumns = 32;
    public const int MaximumCellCharacters = 8192;
    public const int MaximumRecordCharacters = 32768;
    // A synthetic adapter contract, NOT a claim about a DIGEMID export schema. All source values stay verbatim.
    // CSV header is record 1; data RowNumber is a record ordinal, not a physical multiline CSV line number.
    public static IReadOnlyList<string> RequiredColumns { get; } = Array.AsReadOnly(new[]
    { "SourceRecordId", "Name", "Laboratory", "Registration", "Presentation", "Ingredients", "Strength", "DosageForm", "Route" });
    // Same lexical microgram policy as curation: mcg, U+00B5 and U+03BC, without dose/unit conversion.
    private static readonly Regex StrengthShape = new(@"^\s*\d+(?:[.,]\d+)?\s*(?:mcg|(?-i:[\u00B5\u03BC])g|mg|g|mL|L|IU|UI|%)(?:\s*/\s*\d*(?:[.,]\d+)?\s*(?:mcg|(?-i:[\u00B5\u03BC])g|mg|g|mL|L))?(?:\s*\+\s*\d+(?:[.,]\d+)?\s*(?:mcg|(?-i:[\u00B5\u03BC])g|mg|g|mL|L|IU|UI|%)(?:\s*/\s*\d*(?:[.,]\d+)?\s*(?:mcg|(?-i:[\u00B5\u03BC])g|mg|g|mL|L))?)*\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1));
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1));
    private static readonly string[] ClinicalColumns = ["Ingredients", "Strength", "DosageForm", "Presentation"];
    public static OfficialCatalogStagingRow Validate(OfficialCatalogSourceRow row, Guid batch, DateTimeOffset now)
    {
        var fields = row.Fields; var errors = new List<string>();
        string Field(string name) => fields.TryGetValue(name, out var value) ? value : string.Empty;
        if (string.IsNullOrWhiteSpace(Field("SourceRecordId"))) errors.Add("official_catalog.record_id_missing");
        if (string.IsNullOrWhiteSpace(Field("Name"))) errors.Add("official_catalog.name_missing");
        foreach (var field in fields)
        {
            var maximum = field.Key switch
            {
                "SourceRecordId" or "Registration" or "DosageForm" or "Route" => 256,
                "Name" or "Laboratory" or "Strength" => 512,
                "Presentation" => 1024,
                "Ingredients" => 4096,
                _ => MaximumCellCharacters
            };
            if (field.Value.Length > maximum) errors.Add("official_catalog.field_too_long");
            if (field.Value.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'))) errors.Add("official_catalog.control_character");
        }
        if (ClinicalColumns.Any(name => string.IsNullOrWhiteSpace(Field(name))))
            errors.Add("official_catalog.pharmaceutical_data_incomplete");
        // This is a conservative shape check only. No number parsing, concentration conversion, clinical
        // equivalence, ingredient association, presentation factor or inferred route is produced in B6.1.
        if (!string.IsNullOrWhiteSpace(Field("Strength")) && !StrengthShape.IsMatch(Field("Strength"))) errors.Add("official_catalog.strength_ambiguous");
        if (Field("Presentation").Contains('?', StringComparison.Ordinal)) errors.Add("official_catalog.presentation_ambiguous");
        var ordered = fields.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var raw = JsonSerializer.Serialize(ordered);
        var potential = JsonSerializer.Serialize(RequiredColumns.Where(name => name != "SourceRecordId")
            .Select(name => Whitespace.Replace(Field(name).Trim(), " ").ToUpperInvariant()).ToArray());
        var id = Field("SourceRecordId");
        return OfficialCatalogStagingRow.Create(batch, row.RowNumber, fields, raw, Hash(raw), Hash(potential), id.Length == 0 ? null : Hash(id), errors, now);
    }
    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

public sealed class OfficialCatalogDuplicateAnchors
{
    public Dictionary<string, int> SourceIds { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> ExactRows { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> PotentialRows { get; } = new(StringComparer.Ordinal);
    public void Observe(OfficialCatalogStagingRow row)
    {
        var signals = new List<string>(); var first = row.RowNumber;
        void Check(Dictionary<string, int> anchors, string? key, string signal)
        {
            if (key is null) return;
            if (anchors.TryGetValue(key, out var number)) { first = Math.Min(first, number); signals.Add(signal); }
            else anchors.Add(key, row.RowNumber);
        }
        Check(SourceIds, row.SourceRecordIdFingerprint, "source_id"); Check(ExactRows, row.RowChecksumSha256, "exact_row");
        Check(PotentialRows, row.PotentialDuplicateFingerprint, "potential");
        if (signals.Count > 0) row.MarkDuplicate(first, signals);
    }
}

public static class OfficialCatalogErrors
{
    public static ApplicationErrorException Error(string code, ErrorCategory category = ErrorCategory.Validation) =>
        new(new("official_catalog." + code, category, "The official catalog operation could not be completed (" + code + ")."));
    public static bool CanRetry(string? code) => code is "official_catalog.processing_cancelled" or "official_catalog.persistence_failed" or
        "official_catalog.processing_interrupted" or "official_catalog.file_unreadable" or "official_catalog.platform_denied";
    public static async Task<OfficialCatalogActor> RequireActorAsync(IOfficialCatalogAdministration administration, CancellationToken token)
    {
        var actor = await administration.GetAuthorizedActorAsync(token).ConfigureAwait(false);
        if (actor is null || actor.ActorId == Guid.Empty || actor.CorrelationId.Length != 32 || actor.CorrelationId.All(c => c == '0') || actor.CorrelationId.Any(c => !char.IsAsciiHexDigitLower(c)))
            throw Error("platform_denied", ErrorCategory.Forbidden);
        return actor;
    }
}
