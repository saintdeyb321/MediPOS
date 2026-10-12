namespace MediPOS.Domain.Modules.Catalog.OfficialRefresh;

// One immutable administrative event per refresh version, including preparation, decisions and application.
public sealed class OfficialCatalogVersionHistory
{
    private OfficialCatalogVersionHistory() { }
    public Guid Id { get; private set; }
    public Guid RefreshId { get; private set; }
    public Guid GlobalProductId { get; private set; }
    public string SourceCode { get; private set; } = string.Empty;
    public string SourceVersion { get; private set; } = string.Empty;
    public string OfficialIdentity { get; private set; } = string.Empty;
    public Guid ImportBatchId { get; private set; }
    public Guid? StagingRowId { get; private set; }
    public Guid? CurationId { get; private set; }
    public string PreviousSnapshotJson { get; private set; } = string.Empty;
    public string? ProposedSnapshotJson { get; private set; }
    public string? ApprovedSnapshotJson { get; private set; }
    public string ChangedFieldsJson { get; private set; } = "[]";
    public string ChangeClassification { get; private set; } = string.Empty;
    public Guid? ReviewedByActorId { get; private set; }
    public DateTimeOffset? ReviewedAtUtc { get; private set; }
    public Guid ActorId { get; private set; }
    public DateTimeOffset AtUtc { get; private set; }
    public Guid? AppliedByActorId { get; private set; }
    public DateTimeOffset? AppliedAtUtc { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public long Version { get; private set; }
    public long BaseProductVersion { get; private set; }
    public long? AppliedProductVersion { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public string CorrelationId { get; private set; } = string.Empty;
    public string? ReferredOfficialIdentity { get; private set; }
    public static OfficialCatalogVersionHistory Capture(OfficialCatalogRefresh refresh, string action, string reason, Guid actor, string trace, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        RefreshId = refresh.Id,
        GlobalProductId = refresh.GlobalProductId,
        SourceCode = refresh.SourceCode,
        SourceVersion = refresh.SourceVersion,
        OfficialIdentity = refresh.OfficialIdentity,
        ImportBatchId = refresh.ImportBatchId,
        StagingRowId = refresh.StagingRowId,
        CurationId = refresh.CurationId,
        PreviousSnapshotJson = refresh.PreviousSnapshotJson,
        ProposedSnapshotJson = refresh.ProposedSnapshotJson,
        ApprovedSnapshotJson = refresh.Status is "approved" or "applied" ? refresh.ProposedSnapshotJson : null,
        ChangedFieldsJson = refresh.ChangedFieldsJson,
        ChangeClassification = refresh.ChangeClassification,
        ReviewedByActorId = refresh.ReviewedByActorId,
        ReviewedAtUtc = refresh.ReviewedAtUtc,
        ActorId = actor,
        AtUtc = now,
        AppliedByActorId = refresh.AppliedByActorId,
        AppliedAtUtc = refresh.AppliedAtUtc,
        Status = refresh.Status,
        Version = refresh.Version,
        BaseProductVersion = refresh.BaseProductVersion,
        AppliedProductVersion = refresh.AppliedProductVersion,
        Action = action,
        Reason = reason,
        CorrelationId = trace,
        ReferredOfficialIdentity = refresh.ReferredOfficialIdentity,
    };
}
