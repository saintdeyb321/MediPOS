using System.Text.Json;
using MediPOS.Domain.Modules.Catalog.OfficialImport;

namespace MediPOS.Domain.Modules.Catalog.OfficialCuration;

public static class OfficialCurationStates
{
    public const string PendingReview = "pending_review", NeedsCorrection = "needs_correction", Approved = "approved", Rejected = "rejected", Published = "published";
    public static bool IsKnown(string state) => state is PendingReview or NeedsCorrection or Approved or Rejected or Published;
}
public sealed record OfficialDuplicateEvidence(Guid GlobalProductId, string Kind, IReadOnlyList<string> Reasons,
    string Name, string? Presentation, string? SourceCode, string? OfficialIdentity);

public sealed class OfficialCatalogCuration
{
    private OfficialCatalogCuration() { }
    public Guid Id { get; private set; }
    public Guid ImportBatchId { get; private set; }
    public Guid StagingRowId { get; private set; }
    public string SourceCode { get; private set; } = string.Empty;
    public string? SourceRecordId { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public string NormalizedCandidateJson { get; private set; } = string.Empty;
    public string[] Errors { get; private set; } = [];
    public string DuplicateEvidenceJson { get; private set; } = "[]";
    public bool HasPotentialMatches { get; private set; }
    public string? ReviewNotes { get; private set; }
    public Guid? ReviewedByActorId { get; private set; }
    public DateTimeOffset? ReviewedAtUtc { get; private set; }
    public long? ApprovedVersion { get; private set; }
    public string DuplicateResolution { get; private set; } = "unresolved";
    public Guid? ExistingGlobalProductId { get; private set; }
    public Guid? PublishedGlobalProductId { get; private set; }
    public DateTimeOffset? PublishedAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public long Version { get; private set; }
    public OfficialCatalogProposal Proposal => JsonSerializer.Deserialize<OfficialCatalogProposal>(NormalizedCandidateJson)!;
    public IReadOnlyList<OfficialDuplicateEvidence> DuplicateEvidence => JsonSerializer.Deserialize<OfficialDuplicateEvidence[]>(DuplicateEvidenceJson)!;

    public static OfficialCatalogCuration Create(OfficialCatalogImportBatch batch, OfficialCatalogStagingRow row,
        OfficialCatalogProposal proposal, IReadOnlyList<OfficialDuplicateEvidence> matches, DateTimeOffset now)
    {
        if (batch.Status != OfficialCatalogStates.Staged || row.ImportBatchId != batch.Id) throw new InvalidOperationException("batch_not_staged");
        Time(now, row.CreatedAtUtc); var data = Snapshot(proposal);
        var result = new OfficialCatalogCuration
        {
            Id = Guid.CreateVersion7(),
            ImportBatchId = batch.Id,
            StagingRowId = row.Id,
            SourceCode = batch.SourceCode,
            SourceRecordId = row.SourceRecordId,
            NormalizedCandidateJson = data,
            CreatedAtUtc = now,
            Version = 1,
        };
        result.Screen(proposal, matches); result.Status = result.Errors.Length == 0 ? OfficialCurationStates.PendingReview : OfficialCurationStates.NeedsCorrection;
        return result;
    }
    public void Review(long expectedVersion, string action, OfficialCatalogProposal? correction, string reason,
        string duplicateResolution, Guid? existingProduct, Guid actor, DateTimeOffset now, IReadOnlyList<OfficialDuplicateEvidence> matches)
    {
        RequireVersion(expectedVersion);
        if (Status is OfficialCurationStates.Rejected or OfficialCurationStates.Published) throw new InvalidOperationException("candidate_terminal");
        if (Status == OfficialCurationStates.Approved && action == "approve") throw new InvalidOperationException("already_approved");
        if (actor == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Length > 2048 || reason.Any(char.IsControl)) throw new ArgumentException("review_invalid");
        Time(now, ReviewedAtUtc ?? CreatedAtUtc);
        if (duplicateResolution is not ("unresolved" or "distinct" or "link_existing") ||
            (duplicateResolution == "link_existing") != (existingProduct.HasValue && existingProduct != Guid.Empty)) throw new ArgumentException("duplicate_resolution_invalid");
        var nextJson = NormalizedCandidateJson;
        if (action == "save_correction")
        {
            if (correction is null) throw new ArgumentException("correction_required");
            nextJson = Snapshot(correction);
        }
        else if (correction is not null) throw new ArgumentException("correction_requires_separate_review");
        var proposal = JsonSerializer.Deserialize<OfficialCatalogProposal>(nextJson)!;
        var errors = proposal.Errors();
        var nextStatus = action switch
        {
            "save_correction" => errors.Count == 0 ? OfficialCurationStates.PendingReview : OfficialCurationStates.NeedsCorrection,
            "request_correction" => OfficialCurationStates.NeedsCorrection,
            "reject" => OfficialCurationStates.Rejected,
            "approve" when errors.Count == 0 => OfficialCurationStates.Approved,
            "approve" => throw new InvalidOperationException("candidate_incomplete"),
            _ => throw new ArgumentException("review_action_invalid"),
        };
        if (nextStatus == OfficialCurationStates.Approved) RequireDuplicateResolution(matches, duplicateResolution, existingProduct);
        NormalizedCandidateJson = nextJson; Status = nextStatus; Screen(proposal, matches);
        ReviewNotes = reason.Trim(); ReviewedByActorId = actor; ReviewedAtUtc = now;
        DuplicateResolution = duplicateResolution; ExistingGlobalProductId = existingProduct;
        Version = checked(Version + 1); ApprovedVersion = Status == OfficialCurationStates.Approved ? Version : null;
    }
    public Guid? PublishedProductFor(long approvalVersion)
    {
        if (Status != OfficialCurationStates.Published) return null;
        if (ApprovedVersion != approvalVersion) throw new InvalidOperationException("stale_review");
        return PublishedGlobalProductId ?? throw new InvalidOperationException("published_product_required");
    }
    public void RequirePublishable(long expectedVersion, IReadOnlyList<OfficialDuplicateEvidence> matches)
    {
        RequireVersion(expectedVersion);
        if (Status != OfficialCurationStates.Approved || ApprovedVersion != Version || !ReviewedByActorId.HasValue || Proposal.Errors().Count != 0)
            throw new InvalidOperationException("approval_required");
        RequireDuplicateResolution(matches, DuplicateResolution, ExistingGlobalProductId);
        var reviewed = DuplicateEvidence;
        if (matches.Any(match => match.Kind != "pharmaceutically_similar" && !reviewed.Any(previous =>
            previous.GlobalProductId == match.GlobalProductId && previous.Kind == match.Kind &&
            previous.Reasons.Order(StringComparer.Ordinal).SequenceEqual(match.Reasons.Order(StringComparer.Ordinal)))))
            throw new InvalidOperationException("duplicate_evidence_changed");
    }
    public void Publish(long expectedVersion, Guid globalProductId, DateTimeOffset now, IReadOnlyList<OfficialDuplicateEvidence> matches)
    {
        RequirePublishable(expectedVersion, matches);
        if (globalProductId == Guid.Empty) throw new ArgumentException("published_product_required");
        Time(now, ReviewedAtUtc!.Value); PublishedGlobalProductId = globalProductId; PublishedAtUtc = now;
        Status = OfficialCurationStates.Published; Version = checked(Version + 1);
    }
    public void RequireVersion(long version)
    { if (version != Version) throw new InvalidOperationException("stale_review"); }
    public static void RequireDuplicateResolution(IReadOnlyList<OfficialDuplicateEvidence> matches, string resolution, Guid? existing)
    {
        if (matches.Any(match => match.Kind == "official_identity")) throw new InvalidOperationException("identity_conflict");
        if (matches.Any(match => match.Kind == "exact") && resolution != "link_existing") throw new InvalidOperationException("duplicate_conflict");
        if (matches.Any(match => match.Kind == "potential") && resolution == "unresolved") throw new InvalidOperationException("duplicate_unresolved");
        if (resolution == "link_existing" && !matches.Any(match => match.Kind == "exact" && match.GlobalProductId == existing)) throw new InvalidOperationException("link_not_verifiable");
    }
    private void Screen(OfficialCatalogProposal proposal, IReadOnlyList<OfficialDuplicateEvidence> matches)
    {
        Errors = proposal.Errors().ToArray(); DuplicateEvidenceJson = JsonSerializer.Serialize(matches);
        HasPotentialMatches = matches.Count != 0;
    }
    private static string Snapshot(OfficialCatalogProposal data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Name is null || data.Laboratory is null || data.Presentation is null || data.DosageForm is null ||
            data.Components is null || data.Components.Count > 32 || data.Components.Any(component => component is null || component.Ingredient is null || component.Strength is null) ||
            JsonSerializer.Serialize(data).Length > 262000) throw new ArgumentException("proposal_invalid");
        return JsonSerializer.Serialize(data);
    }
    internal static void Time(DateTimeOffset now, DateTimeOffset minimum)
    { if (now == default || now.Offset != TimeSpan.Zero || now < minimum) throw new ArgumentException("curation_time_invalid"); }
}
