namespace MediPOS.Domain.Modules.Catalog.OfficialCuration;

// Append-only snapshot at every version. The approved clinical data can be reconstructed independently of later corrections.
public sealed class OfficialCatalogRevision
{
    private OfficialCatalogRevision() { }
    public Guid Id { get; private set; }
    public Guid CurationId { get; private set; }
    public long Version { get; private set; }
    public long ReviewedVersion { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public string CandidateJson { get; private set; } = string.Empty;
    public string DuplicateEvidenceJson { get; private set; } = string.Empty;
    public string DuplicateResolution { get; private set; } = string.Empty;
    public Guid? ExistingGlobalProductId { get; private set; }
    public Guid ActorId { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public DateTimeOffset AtUtc { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public static OfficialCatalogRevision Capture(OfficialCatalogCuration candidate, long reviewedVersion, string action,
        Guid actor, string correlation, string reason, DateTimeOffset now) => new()
        {
            Id = Guid.CreateVersion7(),
            CurationId = candidate.Id,
            Version = candidate.Version,
            ReviewedVersion = reviewedVersion,
            Action = action,
            Status = candidate.Status,
            CandidateJson = candidate.NormalizedCandidateJson,
            DuplicateEvidenceJson = candidate.DuplicateEvidenceJson,
            DuplicateResolution = candidate.DuplicateResolution,
            ExistingGlobalProductId = candidate.ExistingGlobalProductId,
            ActorId = actor,
            CorrelationId = correlation,
            AtUtc = now,
            Reason = reason,
        };
}
public sealed class OfficialCatalogPublication
{
    private OfficialCatalogPublication() { }
    public Guid Id { get; private set; }
    public string SourceCode { get; private set; } = string.Empty;
    public string OfficialIdentity { get; private set; } = string.Empty;
    public string IdentityHash { get; private set; } = string.Empty;
    public Guid CurationId { get; private set; }
    public Guid StagingRowId { get; private set; }
    public Guid ImportBatchId { get; private set; }
    public Guid GlobalProductId { get; private set; }
    public long ApprovedVersion { get; private set; }
    public string CandidateFingerprint { get; private set; } = string.Empty;
    public string Presentation { get; private set; } = string.Empty;
    public Guid PublishedByActorId { get; private set; }
    public DateTimeOffset PublishedAtUtc { get; private set; }
    public static OfficialCatalogPublication Create(OfficialCatalogCuration candidate, Guid product, Guid actor, DateTimeOffset now)
    {
        if (candidate.Status != OfficialCurationStates.Approved || candidate.ApprovedVersion != candidate.Version || product == Guid.Empty || actor == Guid.Empty) throw new InvalidOperationException("approval_required");
        var proposal = candidate.Proposal;
        return new()
        {
            Id = Guid.CreateVersion7(),
            SourceCode = candidate.SourceCode,
            OfficialIdentity = proposal.OfficialIdentity!,
            IdentityHash = OfficialCatalogProposal.Hash(proposal.OfficialIdentity!),
            CurationId = candidate.Id,
            StagingRowId = candidate.StagingRowId,
            ImportBatchId = candidate.ImportBatchId,
            GlobalProductId = product,
            ApprovedVersion = candidate.Version,
            CandidateFingerprint = proposal.Fingerprint(),
            Presentation = proposal.Presentation,
            PublishedByActorId = actor,
            PublishedAtUtc = now,
        };
    }
}
public sealed class OfficialCatalogPlatformAudit
{
    private OfficialCatalogPlatformAudit() { }
    public Guid Id { get; private set; }
    public Guid CandidateId { get; private set; }
    public long? Version { get; private set; }
    public Guid ActorId { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string? FailureCode { get; private set; }
    public DateTimeOffset AtUtc { get; private set; }
    public static OfficialCatalogPlatformAudit Create(Guid candidate, long? version, Guid actor, string correlation,
        string action, DateTimeOffset now, string? failure = null) => new()
        {
            Id = Guid.CreateVersion7(),
            CandidateId = candidate,
            Version = version,
            ActorId = actor,
            CorrelationId = correlation,
            Action = action,
            AtUtc = now,
            FailureCode = failure,
        };
}
