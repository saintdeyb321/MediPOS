using MediPOS.Application.Modules.Catalog.OfficialCuration;
using MediPOS.Application.Modules.Catalog.OfficialImport;
using MediPOS.Domain.Modules.Catalog.OfficialRefresh;

namespace MediPOS.Application.Modules.Catalog.OfficialRefresh;

public sealed class PrepareOfficialCatalogRefreshHandler(IOfficialCatalogCurationAdministration authority, IOfficialCatalogRefreshStore store, TimeProvider clock)
{
    public async Task<OfficialCatalogRefresh> HandleAsync(PrepareOfficialCatalogRefreshCommand command, CancellationToken token)
    {
        var actor = await OfficialCurationAccess.RequireAsync(authority, false, token).ConfigureAwait(false);
        if (command.PublicationId == Guid.Empty || command.ImportBatchId == Guid.Empty || command.CurationId == Guid.Empty) throw OfficialCatalogErrors.Error("refresh_query_invalid");
        return await OfficialCurationAccess.AuditedAsync(() => store.PrepareAsync(command, actor, clock.GetUtcNow(), token),
            (code, diagnostic) => store.RecordFailureAsync(command.PublicationId, null, actor, "refresh_prepare_failed", code, clock.GetUtcNow(), diagnostic)).ConfigureAwait(false);
    }
}
public sealed class ReviewOfficialCatalogRefreshHandler(IOfficialCatalogCurationAdministration authority, IOfficialCatalogRefreshStore store, TimeProvider clock)
{
    public async Task<OfficialCatalogRefresh> HandleAsync(ReviewOfficialCatalogRefreshCommand command, CancellationToken token)
    {
        var actor = await OfficialCurationAccess.RequireAsync(authority, false, token).ConfigureAwait(false);
        if (command.RefreshId == Guid.Empty || command.ExpectedVersion < 1) throw OfficialCatalogErrors.Error("refresh_query_invalid");
        return await OfficialCurationAccess.AuditedAsync(() => store.ReviewAsync(command, actor, clock.GetUtcNow(), token),
            (code, diagnostic) => store.RecordFailureAsync(command.RefreshId, command.ExpectedVersion, actor, "refresh_review_failed", code, clock.GetUtcNow(), diagnostic)).ConfigureAwait(false);
    }
}
public sealed class ApplyApprovedOfficialCatalogRefreshHandler(IOfficialCatalogCurationAdministration authority, IOfficialCatalogRefreshApplicationStore store, TimeProvider clock)
{
    public async Task<OfficialCatalogRefreshApplyResult> HandleAsync(ApplyApprovedOfficialCatalogRefreshCommand command, CancellationToken token)
    {
        var actor = await OfficialCurationAccess.RequireAsync(authority, true, token).ConfigureAwait(false);
        if (command.RefreshId == Guid.Empty || command.ApprovedVersion < 1) throw OfficialCatalogErrors.Error("refresh_query_invalid");
        return await OfficialCurationAccess.AuditedAsync(() => store.ApplyAsync(command, actor, clock.GetUtcNow(), token),
            (code, diagnostic) => store.RecordFailureAsync(command.RefreshId, command.ApprovedVersion, actor, "refresh_apply_failed", code, clock.GetUtcNow(), diagnostic)).ConfigureAwait(false);
    }
}
public sealed class GetOfficialCatalogRefreshQueueHandler(IOfficialCatalogCurationAdministration authority, IOfficialCatalogRefreshStore store)
{
    public async Task<OfficialCatalogRefreshPage> HandleAsync(GetOfficialCatalogRefreshQueueCommand command, CancellationToken token)
    {
        await OfficialCurationAccess.RequireAsync(authority, false, token).ConfigureAwait(false);
        OfficialRefreshQuery.Validate(command.SourceCode, command.SourceVersion, command.Offset, command.Limit);
        if (command.Status is not null && !OfficialCatalogRefresh.IsStatus(command.Status) ||
            command.Classification is not null && !OfficialCatalogRefreshComparison.IsKnown(command.Classification) || command.GlobalProductId == Guid.Empty ||
            command.FromUtc.HasValue && command.FromUtc.Value.Offset != TimeSpan.Zero || command.ToUtc.HasValue && command.ToUtc.Value.Offset != TimeSpan.Zero || command.FromUtc > command.ToUtc)
            throw OfficialCatalogErrors.Error("refresh_query_invalid");
        var result = await store.QueueAsync(command, token).ConfigureAwait(false);
        await OfficialCurationAccess.RequireAsync(authority, false, token).ConfigureAwait(false); return result;
    }
}
public sealed class GetOfficialCatalogRefreshHistoryHandler(IOfficialCatalogCurationAdministration authority, IOfficialCatalogRefreshStore store)
{
    public async Task<OfficialCatalogVersionHistoryPage> HandleAsync(GetOfficialCatalogRefreshHistoryCommand command, CancellationToken token)
    {
        await OfficialCurationAccess.RequireAsync(authority, false, token).ConfigureAwait(false);
        OfficialRefreshQuery.Validate(command.SourceCode, command.SourceVersion, command.Offset, command.Limit);
        if (command.RefreshId == Guid.Empty || command.GlobalProductId == Guid.Empty) throw OfficialCatalogErrors.Error("refresh_query_invalid");
        var result = await store.HistoryAsync(command, token).ConfigureAwait(false);
        await OfficialCurationAccess.RequireAsync(authority, false, token).ConfigureAwait(false); return result;
    }
}
internal static class OfficialRefreshQuery
{
    internal static void Validate(string? source, string? version, int offset, int limit)
    {
        if (offset < 0 || limit is < 1 or > 100 || source is not null && (string.IsNullOrWhiteSpace(source) || source.Length > 64) ||
            version is not null && (string.IsNullOrWhiteSpace(version) || version.Length > 128)) throw OfficialCatalogErrors.Error("refresh_query_invalid");
    }
}
