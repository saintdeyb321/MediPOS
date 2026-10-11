using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog.OfficialImport;
using MediPOS.Domain.Modules.Catalog.OfficialCuration;

namespace MediPOS.Application.Modules.Catalog.OfficialCuration;

public static class OfficialCurationAccess
{
    public static async Task<OfficialCatalogActor> RequireAsync(IOfficialCatalogCurationAdministration authority, bool publish, CancellationToken token)
    {
        var actor = publish ? await authority.GetAuthorizedPublisherAsync(token).ConfigureAwait(false) : await authority.GetAuthorizedReviewerAsync(token).ConfigureAwait(false);
        if (actor is null || actor.ActorId == Guid.Empty || actor.CorrelationId.Length != 32 || actor.CorrelationId.All(c => c == '0') || actor.CorrelationId.Any(c => !char.IsAsciiHexDigitLower(c)))
            throw OfficialCatalogErrors.Error("curation_platform_denied", ErrorCategory.Forbidden);
        return actor;
    }
    public static void Page(int offset, int limit)
    { if (offset < 0 || limit is < 1 or > 100) throw OfficialCatalogErrors.Error("curation_query_invalid"); }
    internal static async Task<T> AuditedAsync<T>(Func<Task<T>> operation, Func<string, CancellationToken, Task> failure)
    {
        try { return await operation().ConfigureAwait(false); }
        catch (Exception error)
        {
            var code = error switch
            {
                ApplicationErrorException controlled => controlled.Error.Code,
                OperationCanceledException => "official_catalog.curation_cancelled",
                _ => "official_catalog.curation_persistence_failed",
            };
            using var diagnostic = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await failure(code, diagnostic.Token).ConfigureAwait(false); }
            catch (Exception diagnosticError) when (diagnosticError is not OutOfMemoryException)
            { throw OfficialCatalogErrors.Error("curation_failure_audit_unavailable", ErrorCategory.Conflict); }
            if (error is ApplicationErrorException or OperationCanceledException) throw;
            throw OfficialCatalogErrors.Error("curation_persistence_failed", ErrorCategory.Conflict);
        }
    }
}
public sealed class PrepareOfficialCatalogCandidateHandler(IOfficialCatalogCurationAdministration authority, IOfficialCatalogCurationStore store, TimeProvider clock)
{
    public async Task<OfficialCatalogCuration> HandleAsync(PrepareOfficialCatalogCandidateCommand command, CancellationToken token)
    {
        var actor = await OfficialCurationAccess.RequireAsync(authority, false, token).ConfigureAwait(false);
        if (command.StagingRowId == Guid.Empty) throw OfficialCatalogErrors.Error("curation_query_invalid");
        return await OfficialCurationAccess.AuditedAsync(() => store.PrepareAsync(command.StagingRowId, actor, clock.GetUtcNow(), token),
            (code, cancellation) => store.RecordFailureAsync(command.StagingRowId, null, actor, "prepare_failed", code, clock.GetUtcNow(), cancellation)).ConfigureAwait(false);
    }
}
public sealed class ReviewOfficialCatalogCandidateHandler(IOfficialCatalogCurationAdministration authority, IOfficialCatalogCurationStore store, TimeProvider clock)
{
    public async Task<OfficialCatalogCuration> HandleAsync(ReviewOfficialCatalogCandidateCommand command, CancellationToken token)
    {
        var actor = await OfficialCurationAccess.RequireAsync(authority, false, token).ConfigureAwait(false);
        if (command.CandidateId == Guid.Empty || command.ExpectedVersion < 1) throw OfficialCatalogErrors.Error("curation_query_invalid");
        return await OfficialCurationAccess.AuditedAsync(() => store.ReviewAsync(command, actor, clock.GetUtcNow(), token),
            (code, cancellation) => store.RecordFailureAsync(command.CandidateId, command.ExpectedVersion, actor, "review_failed", code, clock.GetUtcNow(), cancellation)).ConfigureAwait(false);
    }
}
public sealed class PublishApprovedOfficialCatalogCandidateHandler(IOfficialCatalogCurationAdministration authority, IOfficialCatalogPublicationStore store, TimeProvider clock)
{
    public async Task<OfficialCatalogPublishResult> HandleAsync(PublishApprovedOfficialCatalogCandidateCommand command, CancellationToken token)
    {
        var actor = await OfficialCurationAccess.RequireAsync(authority, true, token).ConfigureAwait(false);
        if (command.CandidateId == Guid.Empty || command.ApprovedVersion < 1) throw OfficialCatalogErrors.Error("curation_query_invalid");
        return await OfficialCurationAccess.AuditedAsync(() => store.PublishAsync(command, actor, clock.GetUtcNow(), token),
            (code, cancellation) => store.RecordFailureAsync(command.CandidateId, command.ApprovedVersion, actor, "publish_failed", code, clock.GetUtcNow(), cancellation)).ConfigureAwait(false);
    }
}
public sealed class GetOfficialCatalogCandidateHandler(IOfficialCatalogCurationAdministration authority, IOfficialCatalogCurationStore store)
{
    public async Task<OfficialCatalogCandidateResult> HandleAsync(GetOfficialCatalogCandidateCommand command, CancellationToken token)
    {
        await OfficialCurationAccess.RequireAsync(authority, false, token).ConfigureAwait(false); OfficialCurationAccess.Page(command.RevisionOffset, command.RevisionLimit); OfficialCurationAccess.Page(command.AuditOffset, command.AuditLimit);
        var result = await store.GetAsync(command, token).ConfigureAwait(false);
        await OfficialCurationAccess.RequireAsync(authority, false, token).ConfigureAwait(false); return result;
    }
}
public sealed class GetOfficialCatalogCurationQueueHandler(IOfficialCatalogCurationAdministration authority, IOfficialCatalogCurationStore store)
{
    public async Task<OfficialCatalogCurationPage> HandleAsync(GetOfficialCatalogCurationQueueCommand command, CancellationToken token)
    {
        await OfficialCurationAccess.RequireAsync(authority, false, token).ConfigureAwait(false); OfficialCurationAccess.Page(command.Offset, command.Limit);
        if (command.Status is not null && !OfficialCurationStates.IsKnown(command.Status) || command.SourceCode?.Length > 64 || command.Error?.Length > 96)
            throw OfficialCatalogErrors.Error("curation_query_invalid");
        var result = await store.QueueAsync(command, token).ConfigureAwait(false);
        await OfficialCurationAccess.RequireAsync(authority, false, token).ConfigureAwait(false); return result;
    }
}
public sealed class GetOfficialCatalogDuplicateCandidatesHandler(IOfficialCatalogCurationAdministration authority, IOfficialCatalogCurationStore store)
{
    public async Task<OfficialCatalogDuplicatePage> HandleAsync(GetOfficialCatalogDuplicateCandidatesCommand command, CancellationToken token)
    {
        await OfficialCurationAccess.RequireAsync(authority, false, token).ConfigureAwait(false); OfficialCurationAccess.Page(command.Offset, command.Limit);
        var result = await store.DuplicatesAsync(command, token).ConfigureAwait(false);
        await OfficialCurationAccess.RequireAsync(authority, false, token).ConfigureAwait(false); return result;
    }
}
