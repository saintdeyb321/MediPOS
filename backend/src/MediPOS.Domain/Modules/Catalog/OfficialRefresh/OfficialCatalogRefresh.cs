using System.Text.Json;
using MediPOS.Domain.Modules.Catalog.OfficialCuration;
using MediPOS.Domain.Modules.Catalog.OfficialImport;

namespace MediPOS.Domain.Modules.Catalog.OfficialRefresh;

public sealed class OfficialCatalogRefresh
{
    private OfficialCatalogRefresh() { }
    public Guid Id { get; private set; }
    public Guid PublicationId { get; private set; }
    public Guid GlobalProductId { get; private set; }
    public string SourceCode { get; private set; } = string.Empty;
    public string SourceVersion { get; private set; } = string.Empty;
    public string OfficialIdentity { get; private set; } = string.Empty;
    public Guid ImportBatchId { get; private set; }
    public Guid? StagingRowId { get; private set; }
    public Guid? CurationId { get; private set; }
    public string SourceChecksumSha256 { get; private set; } = string.Empty;
    public string? RowChecksumSha256 { get; private set; }
    public string PreviousSnapshotJson { get; private set; } = string.Empty;
    public string? ProposedSnapshotJson { get; private set; }
    public string ChangedFieldsJson { get; private set; } = "[]";
    public string ChangeClassification { get; private set; } = string.Empty;
    public string ProductFingerprint { get; private set; } = string.Empty;
    public long BaseProductVersion { get; private set; }
    public long? AppliedProductVersion { get; private set; }
    public string Status { get; private set; } = "pending_review";
    public long Version { get; private set; }
    public long? ApprovedVersion { get; private set; }
    public Guid CreatedByActorId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Guid? ReviewedByActorId { get; private set; }
    public DateTimeOffset? ReviewedAtUtc { get; private set; }
    public string? Reason { get; private set; }
    public string? ReferredOfficialIdentity { get; private set; }
    public Guid? AppliedByActorId { get; private set; }
    public DateTimeOffset? AppliedAtUtc { get; private set; }
    public OfficialCatalogProductSnapshot Previous => OfficialCatalogProductSnapshot.Read(PreviousSnapshotJson);
    public OfficialCatalogProductSnapshot? Proposed => ProposedSnapshotJson is null ? null : OfficialCatalogProductSnapshot.Read(ProposedSnapshotJson);
    public static bool IsStatus(string value) => value is "pending_review" or "requires_review" or "approved" or "rejected" or "kept_previous" or "referred" or "applied";
    public static OfficialCatalogRefresh Create(OfficialCatalogPublication publication, OfficialCatalogImportBatch batch,
        OfficialCatalogCuration? curation, OfficialCatalogStagingRow? row, OfficialCatalogProductSnapshot previous,
        long productVersion, Guid actor, DateTimeOffset now)
    {
        if (actor == Guid.Empty || productVersion < 0 || previous.GlobalProductId != publication.GlobalProductId ||
            previous.Proposal.OfficialIdentity != publication.OfficialIdentity || !previous.IsActive) throw new InvalidOperationException("refresh_identity_invalid");
        if (batch.Status != OfficialCatalogStates.Staged || batch.SourceCode != publication.SourceCode || batch.Id == publication.ImportBatchId)
            throw new InvalidOperationException("refresh_batch_invalid");
        if ((curation is null) != (row is null) || row is not null &&
            (row.ImportBatchId != batch.Id || row.SourceRecordId != publication.OfficialIdentity || curation!.StagingRowId != row.Id || curation.ImportBatchId != batch.Id ||
             curation.Status is "published" or "rejected")) throw new InvalidOperationException("refresh_identity_invalid");
        OfficialCatalogCuration.Time(now, batch.CompletedAtUtc ?? batch.StartedAtUtc);
        var result = new OfficialCatalogRefresh
        {
            Id = Guid.CreateVersion7(),
            PublicationId = publication.Id,
            GlobalProductId = publication.GlobalProductId,
            SourceCode = batch.SourceCode,
            SourceVersion = batch.SourceVersion,
            OfficialIdentity = publication.OfficialIdentity,
            ImportBatchId = batch.Id,
            StagingRowId = row?.Id,
            CurationId = curation?.Id,
            SourceChecksumSha256 = batch.SourceChecksumSha256,
            RowChecksumSha256 = row?.RowChecksumSha256,
            PreviousSnapshotJson = previous.Json(),
            ProductFingerprint = previous.Fingerprint(),
            BaseProductVersion = productVersion,
            CreatedByActorId = actor,
            CreatedAtUtc = now,
            Version = 1,
        };
        result.Screen(curation?.Proposal); return result;
    }
    public void RequireSameInput(OfficialCatalogImportBatch batch, OfficialCatalogStagingRow? row)
    {
        if (batch.SourceCode != SourceCode || batch.SourceVersion != SourceVersion || batch.SourceChecksumSha256 != SourceChecksumSha256 ||
            row?.RowChecksumSha256 != RowChecksumSha256) throw new InvalidOperationException("refresh_source_version_conflict");
    }
    public void Review(long expectedVersion, string action, OfficialCatalogProposal? correction, string reason,
        string? referredIdentity, OfficialCatalogProductSnapshot current, long currentProductVersion, Guid actor, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        if (Status is "applied" or "rejected" or "kept_previous" or "referred") throw new InvalidOperationException("refresh_terminal");
        if (actor == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Length > 2048 || reason.Any(char.IsControl)) throw new ArgumentException("refresh_review_invalid");
        OfficialCatalogCuration.Time(now, ReviewedAtUtc ?? CreatedAtUtc);
        if (action == "refer_new_identity")
        {
            if (string.IsNullOrWhiteSpace(referredIdentity) || referredIdentity.Length > 256 || referredIdentity == OfficialIdentity || referredIdentity.Any(char.IsControl))
                throw new ArgumentException("refresh_referral_invalid");
        }
        else if (referredIdentity is not null) throw new ArgumentException("refresh_referral_invalid");
        if (action != "save_correction" && correction is not null) throw new ArgumentException("refresh_correction_separate");
        var candidate = Proposed?.Proposal;
        var previous = Previous;
        if (action == "save_correction")
        {
            if (StagingRowId is null || correction is null) throw new ArgumentException("refresh_correction_required");
            ValidateProposal(correction); candidate = correction;
            // Explicit new correction/review rebases an obsolete proposal; its old baseline remains in immutable history.
            previous = current;
        }
        var comparison = OfficialCatalogRefreshComparison.Compare(previous.Proposal, candidate);
        var state = action switch
        {
            "save_correction" => "pending_review",
            "request_review" => "requires_review",
            "reject" => "rejected",
            "keep_previous" => "kept_previous",
            "refer_new_identity" when comparison.Classification is "clinically_significant_change" or "identity_conflict" => "referred",
            "approve" when Status != "approved" && candidate is not null && candidate.Name == candidate.Name.Trim() && candidate.Laboratory == candidate.Laboratory.Trim() &&
                comparison.Classification is "unchanged" or "metadata_changed" => "approved",
            "approve" => throw new InvalidOperationException("refresh_approval_blocked"),
            _ => throw new ArgumentException("refresh_action_invalid"),
        };
        if (state == "approved") RequireCurrent(current, currentProductVersion);
        if (action == "save_correction")
        {
            PreviousSnapshotJson = previous.Json(); ProductFingerprint = previous.Fingerprint(); BaseProductVersion = currentProductVersion;
        }
        Screen(candidate); Status = state; Reason = reason.Trim(); ReferredOfficialIdentity = referredIdentity;
        ReviewedByActorId = actor; ReviewedAtUtc = now; Version = checked(Version + 1);
        ApprovedVersion = state == "approved" ? Version : null;
    }
    public void Apply(long approvedVersion, GlobalProduct product, OfficialCatalogProductSnapshot current, long currentProductVersion, Guid actor, DateTimeOffset now)
    {
        RequireApplicable(approvedVersion, current, currentProductVersion, actor, now);
        if (product.Id != GlobalProductId) throw new InvalidOperationException("refresh_identity_invalid");
        var proposal = Proposed!.Proposal;
        product.CorrectOfficialMetadata(proposal.Name, proposal.Laboratory, proposal.CategoryId!.Value);
        Status = "applied"; AppliedByActorId = actor; AppliedAtUtc = now; AppliedProductVersion = checked(BaseProductVersion + 1); Version = checked(Version + 1);
    }
    public void RequireApplicable(long approvedVersion, OfficialCatalogProductSnapshot current, long currentProductVersion, Guid actor, DateTimeOffset now)
    {
        RequireVersion(approvedVersion);
        if (Status != "approved" || ApprovedVersion != Version || actor == Guid.Empty || Proposed is null ||
            ChangeClassification is not ("unchanged" or "metadata_changed")) throw new InvalidOperationException("refresh_approval_required");
        RequireCurrent(current, currentProductVersion); OfficialCatalogCuration.Time(now, ReviewedAtUtc!.Value);
        var proposal = Proposed.Proposal;
        if (proposal.Errors().Count != 0 ||
            OfficialCatalogRefreshComparison.Compare(current.Proposal, proposal).Classification is not ("unchanged" or "metadata_changed"))
            throw new InvalidOperationException("refresh_clinical_change_blocked");
    }
    public bool AlreadyApplied(long approvedVersion)
    {
        if (Status != "applied") return false;
        if (ApprovedVersion != approvedVersion) throw new InvalidOperationException("refresh_stale_review");
        return true;
    }
    public void RequireVersion(long version)
    { if (version != Version) throw new InvalidOperationException("refresh_stale_review"); }
    private void RequireCurrent(OfficialCatalogProductSnapshot current, long version)
    {
        if (current.GlobalProductId != GlobalProductId || current.Fingerprint() != ProductFingerprint || version != BaseProductVersion)
            throw new InvalidOperationException("refresh_stale_product");
    }
    private void Screen(OfficialCatalogProposal? proposal)
    {
        if (proposal is not null) ValidateProposal(proposal);
        ProposedSnapshotJson = proposal is null ? null : (Previous with { Proposal = proposal }).Json();
        var comparison = OfficialCatalogRefreshComparison.Compare(Previous.Proposal, proposal);
        ChangeClassification = comparison.Classification; ChangedFieldsJson = JsonSerializer.Serialize(comparison.ChangedFields);
    }
    private static void ValidateProposal(OfficialCatalogProposal proposal)
    {
        if (proposal.Components is null || proposal.Components.Count > 32 || proposal.Components.Any(item => item is null || item.Ingredient is null || item.Strength is null) ||
            proposal.Name is null || proposal.Laboratory is null || proposal.Presentation is null || proposal.DosageForm is null || JsonSerializer.Serialize(proposal).Length > 262000)
            throw new ArgumentException("refresh_proposal_invalid");
    }
}
