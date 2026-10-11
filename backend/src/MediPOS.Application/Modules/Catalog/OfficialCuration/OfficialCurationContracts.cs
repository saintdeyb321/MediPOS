using MediPOS.Application.Modules.Catalog.OfficialImport;
using MediPOS.Domain.Modules.Catalog.OfficialCuration;
using MediPOS.Domain.Modules.Catalog.OfficialImport;

namespace MediPOS.Application.Modules.Catalog.OfficialCuration;

// The host supplies independent review and publication authorization. Tenant roles never imply either permission.
public interface IOfficialCatalogCurationAdministration : IOfficialCatalogAdministration
{
    Task<OfficialCatalogActor?> GetAuthorizedReviewerAsync(CancellationToken token);
    Task<OfficialCatalogActor?> GetAuthorizedPublisherAsync(CancellationToken token);
}
public sealed record PrepareOfficialCatalogCandidateCommand(Guid StagingRowId);
public sealed record ReviewOfficialCatalogCandidateCommand(Guid CandidateId, long ExpectedVersion, string Action, string Reason,
    OfficialCatalogProposal? CorrectedProposal = null, string DuplicateResolution = "unresolved", Guid? ExistingGlobalProductId = null);
public sealed record PublishApprovedOfficialCatalogCandidateCommand(Guid CandidateId, long ApprovedVersion);
public sealed record GetOfficialCatalogCurationQueueCommand(Guid? BatchId = null, string? SourceCode = null,
    string? Status = null, string? Error = null, bool? HasPotentialMatch = null, int Offset = 0, int Limit = 100);
public sealed record GetOfficialCatalogCandidateCommand(Guid CandidateId, int RevisionOffset = 0, int RevisionLimit = 100, int AuditOffset = 0, int AuditLimit = 100);
public sealed record GetOfficialCatalogDuplicateCandidatesCommand(Guid CandidateId, int Offset = 0, int Limit = 100);
public sealed record OfficialCatalogCandidateResult(OfficialCatalogCuration Curation, OfficialCatalogStagingRow Original,
    OfficialCatalogImportBatch Batch, IReadOnlyList<OfficialCatalogRevision> Decisions, OfficialCatalogPublication? Publication,
    bool HasMoreDecisions, IReadOnlyList<OfficialCatalogPlatformAudit> Audits, bool HasMoreAudits);
public sealed record OfficialCatalogCurationPage(IReadOnlyList<OfficialCatalogCuration> Candidates, bool HasMore);
public sealed record OfficialCatalogDuplicatePage(IReadOnlyList<OfficialDuplicateEvidence> Matches, bool HasMore);
public sealed record OfficialCatalogPublishResult(Guid CandidateId, Guid GlobalProductId, long Version, bool AlreadyPublished);
public interface IOfficialCatalogCurationStore
{
    Task<OfficialCatalogCuration> PrepareAsync(Guid stagingRowId, OfficialCatalogActor actor, DateTimeOffset now, CancellationToken token);
    Task<OfficialCatalogCuration> ReviewAsync(ReviewOfficialCatalogCandidateCommand command, OfficialCatalogActor actor, DateTimeOffset now, CancellationToken token);
    Task<OfficialCatalogCandidateResult> GetAsync(GetOfficialCatalogCandidateCommand command, CancellationToken token);
    Task<OfficialCatalogCurationPage> QueueAsync(GetOfficialCatalogCurationQueueCommand command, CancellationToken token);
    Task<OfficialCatalogDuplicatePage> DuplicatesAsync(GetOfficialCatalogDuplicateCandidatesCommand command, CancellationToken token);
    Task RecordFailureAsync(Guid candidate, long? version, OfficialCatalogActor actor, string action, string code, DateTimeOffset now, CancellationToken token);
}
public interface IOfficialCatalogPublicationStore
{
    Task<OfficialCatalogPublishResult> PublishAsync(PublishApprovedOfficialCatalogCandidateCommand command, OfficialCatalogActor actor, DateTimeOffset now, CancellationToken token);
    Task RecordFailureAsync(Guid candidate, long? version, OfficialCatalogActor actor, string action, string code, DateTimeOffset now, CancellationToken token);
}
