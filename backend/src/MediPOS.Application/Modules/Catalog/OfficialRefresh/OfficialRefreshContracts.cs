using MediPOS.Application.Modules.Catalog.OfficialImport;
using MediPOS.Domain.Modules.Catalog.OfficialCuration;
using MediPOS.Domain.Modules.Catalog.OfficialRefresh;

namespace MediPOS.Application.Modules.Catalog.OfficialRefresh;

// Publication identity and batch are independently verified server-side. Null curation records an evidenced absence.
public sealed record PrepareOfficialCatalogRefreshCommand(Guid PublicationId, Guid ImportBatchId, Guid? CurationId = null);
public sealed record ReviewOfficialCatalogRefreshCommand(Guid RefreshId, long ExpectedVersion, string Action, string Reason,
    OfficialCatalogProposal? CorrectedProposal = null, string? ReferredOfficialIdentity = null);
public sealed record ApplyApprovedOfficialCatalogRefreshCommand(Guid RefreshId, long ApprovedVersion);
public sealed record GetOfficialCatalogRefreshQueueCommand(string? SourceCode = null, string? SourceVersion = null,
    string? Status = null, string? Classification = null, Guid? GlobalProductId = null,
    DateTimeOffset? FromUtc = null, DateTimeOffset? ToUtc = null, int Offset = 0, int Limit = 100);
public sealed record GetOfficialCatalogRefreshHistoryCommand(Guid? RefreshId = null, Guid? GlobalProductId = null,
    string? SourceCode = null, string? SourceVersion = null, int Offset = 0, int Limit = 100);
public sealed record OfficialCatalogRefreshPage(IReadOnlyList<OfficialCatalogRefresh> Refreshes, bool HasMore);
public sealed record OfficialCatalogVersionHistoryPage(IReadOnlyList<OfficialCatalogVersionHistory> Entries, bool HasMore);
public sealed record OfficialCatalogRefreshApplyResult(Guid RefreshId, Guid GlobalProductId, long Version, long ProductVersion, bool AlreadyApplied);
public interface IOfficialCatalogRefreshStore
{
    Task<OfficialCatalogRefresh> PrepareAsync(PrepareOfficialCatalogRefreshCommand command, OfficialCatalogActor actor, DateTimeOffset now, CancellationToken token);
    Task<OfficialCatalogRefresh> ReviewAsync(ReviewOfficialCatalogRefreshCommand command, OfficialCatalogActor actor, DateTimeOffset now, CancellationToken token);
    Task<OfficialCatalogRefreshPage> QueueAsync(GetOfficialCatalogRefreshQueueCommand command, CancellationToken token);
    Task<OfficialCatalogVersionHistoryPage> HistoryAsync(GetOfficialCatalogRefreshHistoryCommand command, CancellationToken token);
    Task RecordFailureAsync(Guid target, long? version, OfficialCatalogActor actor, string action, string code, DateTimeOffset now, CancellationToken token);
}
public interface IOfficialCatalogRefreshApplicationStore
{
    Task<OfficialCatalogRefreshApplyResult> ApplyAsync(ApplyApprovedOfficialCatalogRefreshCommand command, OfficialCatalogActor actor, DateTimeOffset now, CancellationToken token);
    Task RecordFailureAsync(Guid target, long? version, OfficialCatalogActor actor, string action, string code, DateTimeOffset now, CancellationToken token);
}
