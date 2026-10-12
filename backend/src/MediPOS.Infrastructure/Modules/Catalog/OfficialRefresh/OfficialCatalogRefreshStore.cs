using System.Data;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog.OfficialCuration;
using MediPOS.Application.Modules.Catalog.OfficialImport;
using MediPOS.Application.Modules.Catalog.OfficialRefresh;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Catalog.OfficialCuration;
using MediPOS.Domain.Modules.Catalog.OfficialImport;
using MediPOS.Domain.Modules.Catalog.OfficialRefresh;
using MediPOS.Infrastructure.Modules.Catalog.OfficialCuration;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediPOS.Infrastructure.Modules.Catalog.OfficialRefresh;

internal sealed class OfficialCatalogRefreshStore(MediPosDbContext context, IOfficialCatalogCurationAdministration authority,
    bool publisher) : IOfficialCatalogRefreshStore, IOfficialCatalogRefreshApplicationStore, IAsyncDisposable
{
    private Task AccessAsync(CancellationToken token) => OfficialCatalogDatabaseAccess.RequireAsync(context, publisher, token);
    private void Reviewer()
    { if (publisher) throw OfficialCatalogErrors.Error("refresh_reviewer_required", ErrorCategory.Forbidden); }
    private async Task ReauthorizeAsync(OfficialCatalogActor initial, CancellationToken token)
    {
        var current = await OfficialCurationAccess.RequireAsync(authority, publisher, token).ConfigureAwait(false);
        if (current.ActorId != initial.ActorId) throw OfficialCatalogErrors.Error("refresh_actor_changed", ErrorCategory.Forbidden);
    }
    private static ApplicationErrorException DomainError(Exception error) => OfficialCatalogErrors.Error(
        error.Message.StartsWith("refresh_", StringComparison.Ordinal) ? error.Message : "refresh_data_invalid",
        error is InvalidOperationException ? ErrorCategory.Conflict : ErrorCategory.Validation);
    private async Task<OfficialCatalogPublication> OriginAsync(Guid id, CancellationToken token) =>
        await context.OfficialCatalogPublications.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, token).ConfigureAwait(false) ??
            throw OfficialCatalogErrors.Error("refresh_publication_not_found", ErrorCategory.NotFound);
    private async Task<OfficialCatalogProposal> OriginalAsync(OfficialCatalogPublication origin, CancellationToken token)
    {
        var candidate = await context.OfficialCatalogCurations.AsNoTracking().SingleAsync(value => value.Id == origin.CurationId, token).ConfigureAwait(false);
        if (candidate.Status != "published" || candidate.PublishedGlobalProductId != origin.GlobalProductId) throw OfficialCatalogErrors.Error("refresh_identity_invalid", ErrorCategory.Conflict);
        return candidate.Proposal;
    }
    private async Task LocksAsync(OfficialCatalogPublication origin, CancellationToken token)
    {
        foreach (var key in new[] { "refresh-global:" + origin.GlobalProductId.ToString(), "i:" + origin.SourceCode + ":" + origin.IdentityHash }.Order(StringComparer.Ordinal))
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key},0))", token).ConfigureAwait(false);
    }
    private async Task<GlobalProduct> ProductAsync(Guid id, bool locked, CancellationToken token)
    {
        var query = locked ? context.GlobalProducts.FromSqlInterpolated($"SELECT * FROM global_products WHERE id = {id} FOR UPDATE") : context.GlobalProducts;
        return await query.Include(value => value.MedicineProfile).SingleAsync(value => value.Id == id, token).ConfigureAwait(false);
    }
    private async Task<long> ProductVersionAsync(Guid id, CancellationToken token) =>
        await context.OfficialCatalogRefreshes.Where(value => value.GlobalProductId == id && value.AppliedProductVersion != null)
            .MaxAsync(value => value.AppliedProductVersion, token).ConfigureAwait(false) ?? 0;
    private async Task RequireSourceAdvanceAsync(OfficialCatalogPublication origin, OfficialCatalogImportBatch batch, CancellationToken token)
    {
        var latest = await OfficialCatalogRefreshQueries.LatestAppliedBatch(context.OfficialCatalogVersionHistories.AsNoTracking(),
            context.OfficialCatalogImportBatches.AsNoTracking(), origin.SourceCode, origin.OfficialIdentity)
            .FirstOrDefaultAsync(token).ConfigureAwait(false) ??
            await context.OfficialCatalogImportBatches.AsNoTracking().SingleAsync(value => value.Id == origin.ImportBatchId, token).ConfigureAwait(false);
        try { OfficialCatalogSourceOrder.RequireAdvance(batch, latest); }
        catch (InvalidOperationException error) { throw DomainError(error); }
    }
    private async Task<OfficialCatalogRefresh> RefreshAsync(Guid id, CancellationToken token) =>
        await context.OfficialCatalogRefreshes.FromSqlInterpolated($"SELECT * FROM official_catalog_refreshes WHERE id = {id} FOR UPDATE")
            .SingleOrDefaultAsync(token).ConfigureAwait(false) ?? throw OfficialCatalogErrors.Error("refresh_not_found", ErrorCategory.NotFound);
    private void History(OfficialCatalogRefresh refresh, string action, string reason, OfficialCatalogActor actor, DateTimeOffset now)
    {
        context.OfficialCatalogVersionHistories.Add(OfficialCatalogVersionHistory.Capture(refresh, action, reason, actor.ActorId, actor.CorrelationId, now));
        context.OfficialCatalogPlatformAudits.Add(OfficialCatalogPlatformAudit.Create(refresh.Id, refresh.Version, actor.ActorId, actor.CorrelationId, "refresh_" + action, now));
    }
    public async Task<OfficialCatalogRefresh> PrepareAsync(PrepareOfficialCatalogRefreshCommand command, OfficialCatalogActor actor, DateTimeOffset now, CancellationToken token)
    {
        Reviewer(); await AccessAsync(token).ConfigureAwait(false); context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(token).ConfigureAwait(false);
        var origin = await OriginAsync(command.PublicationId, token).ConfigureAwait(false);
        await LocksAsync(origin, token).ConfigureAwait(false);
        var batch = await context.OfficialCatalogImportBatches.AsNoTracking().SingleOrDefaultAsync(value => value.Id == command.ImportBatchId, token).ConfigureAwait(false) ??
            throw OfficialCatalogErrors.Error("refresh_batch_not_found", ErrorCategory.NotFound);
        if (batch.SourceCode != origin.SourceCode || batch.Status != OfficialCatalogStates.Staged)
            throw OfficialCatalogErrors.Error("refresh_batch_invalid", ErrorCategory.Conflict);
        OfficialCatalogCuration? candidate = null; OfficialCatalogStagingRow? row = null;
        if (command.CurationId.HasValue)
        {
            candidate = await context.OfficialCatalogCurations.AsNoTracking().SingleOrDefaultAsync(value => value.Id == command.CurationId, token).ConfigureAwait(false) ??
                throw OfficialCatalogErrors.Error("refresh_curation_not_found", ErrorCategory.NotFound);
            row = await context.OfficialCatalogStagingRows.AsNoTracking().SingleAsync(value => value.Id == candidate.StagingRowId, token).ConfigureAwait(false);
            if (row.ImportBatchId != batch.Id || row.SourceRecordId != origin.OfficialIdentity || candidate.SourceCode != origin.SourceCode)
                throw OfficialCatalogErrors.Error("refresh_identity_invalid", ErrorCategory.Conflict);
        }
        else if (await context.OfficialCatalogStagingRows.AnyAsync(value => value.ImportBatchId == batch.Id && value.SourceRecordIdFingerprint == origin.IdentityHash && value.SourceRecordId == origin.OfficialIdentity, token).ConfigureAwait(false))
            throw OfficialCatalogErrors.Error("refresh_curation_required", ErrorCategory.Conflict);
        var existing = await context.OfficialCatalogRefreshes.SingleOrDefaultAsync(value => value.SourceCode == origin.SourceCode &&
            value.SourceVersion == batch.SourceVersion && value.OfficialIdentity == origin.OfficialIdentity, token).ConfigureAwait(false);
        if (existing is not null)
        {
            try { existing.RequireSameInput(batch, row); }
            catch (InvalidOperationException error) { throw DomainError(error); }
            // An applied retry returns history without reapplying older metadata or asserting chronological order.
            if (existing.Status != "applied") await RequireSourceAdvanceAsync(origin, batch, token).ConfigureAwait(false);
            await ReauthorizeAsync(actor, token).ConfigureAwait(false); await transaction.CommitAsync(token).ConfigureAwait(false); return existing;
        }
        await RequireSourceAdvanceAsync(origin, batch, token).ConfigureAwait(false);
        var product = await ProductAsync(origin.GlobalProductId, false, token).ConfigureAwait(false);
        OfficialCatalogRefresh refresh;
        try
        {
            refresh = OfficialCatalogRefresh.Create(origin, batch, candidate, row,
                OfficialCatalogProductSnapshot.Capture(product, await OriginalAsync(origin, token).ConfigureAwait(false)),
                await ProductVersionAsync(product.Id, token).ConfigureAwait(false), actor.ActorId, now);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { throw DomainError(error); }
        context.OfficialCatalogRefreshes.Add(refresh); await context.SaveChangesAsync(token).ConfigureAwait(false);
        History(refresh, "prepared", row is null ? "Absent in this staged source version; no withdrawal or deactivation inferred." : "Structured source associations require explicit administrative review.", actor, now);
        await ReauthorizeAsync(actor, token).ConfigureAwait(false); await context.SaveChangesAsync(token).ConfigureAwait(false);
        await transaction.CommitAsync(token).ConfigureAwait(false); return refresh;
    }
    public async Task<OfficialCatalogRefresh> ReviewAsync(ReviewOfficialCatalogRefreshCommand command, OfficialCatalogActor actor, DateTimeOffset now, CancellationToken token)
    {
        Reviewer(); await AccessAsync(token).ConfigureAwait(false); context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(token).ConfigureAwait(false);
        var refresh = await RefreshAsync(command.RefreshId, token).ConfigureAwait(false);
        var origin = await OriginAsync(refresh.PublicationId, token).ConfigureAwait(false); await LocksAsync(origin, token).ConfigureAwait(false);
        if (command.Action is "save_correction" or "approve" or "request_review")
            await RequireSourceAdvanceAsync(origin, await context.OfficialCatalogImportBatches.AsNoTracking()
                .SingleAsync(value => value.Id == refresh.ImportBatchId, token).ConfigureAwait(false), token).ConfigureAwait(false);
        var product = await ProductAsync(refresh.GlobalProductId, false, token).ConfigureAwait(false);
        try
        {
            refresh.Review(command.ExpectedVersion, command.Action, command.CorrectedProposal, command.Reason, command.ReferredOfficialIdentity,
                OfficialCatalogProductSnapshot.Capture(product, await OriginalAsync(origin, token).ConfigureAwait(false)),
                await ProductVersionAsync(product.Id, token).ConfigureAwait(false), actor.ActorId, now);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { throw DomainError(error); }
        if (refresh.Status == "approved" && !await OfficialCatalogDatabaseAccess.LockCategoryAsync(context, refresh.Proposed!.Proposal.CategoryId, token).ConfigureAwait(false))
            throw OfficialCatalogErrors.Error("refresh_category_invalid");
        await context.SaveChangesAsync(token).ConfigureAwait(false); History(refresh, command.Action, refresh.Reason!, actor, now);
        await ReauthorizeAsync(actor, token).ConfigureAwait(false); await context.SaveChangesAsync(token).ConfigureAwait(false);
        await transaction.CommitAsync(token).ConfigureAwait(false); return refresh;
    }
    public async Task<OfficialCatalogRefreshApplyResult> ApplyAsync(ApplyApprovedOfficialCatalogRefreshCommand command, OfficialCatalogActor actor, DateTimeOffset now, CancellationToken token)
    {
        if (!publisher) throw OfficialCatalogErrors.Error("refresh_publisher_required", ErrorCategory.Forbidden);
        await AccessAsync(token).ConfigureAwait(false); context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(token).ConfigureAwait(false);
        var refresh = await RefreshAsync(command.RefreshId, token).ConfigureAwait(false);
        try
        {
            if (refresh.AlreadyApplied(command.ApprovedVersion))
            {
                await ReauthorizeAsync(actor, token).ConfigureAwait(false); await transaction.CommitAsync(token).ConfigureAwait(false);
                return new(refresh.Id, refresh.GlobalProductId, refresh.Version, refresh.AppliedProductVersion!.Value, true);
            }
        }
        catch (InvalidOperationException error) { throw DomainError(error); }
        var origin = await OriginAsync(refresh.PublicationId, token).ConfigureAwait(false); await LocksAsync(origin, token).ConfigureAwait(false);
        await RequireSourceAdvanceAsync(origin, await context.OfficialCatalogImportBatches.AsNoTracking()
            .SingleAsync(value => value.Id == refresh.ImportBatchId, token).ConfigureAwait(false), token).ConfigureAwait(false);
        var product = await ProductAsync(refresh.GlobalProductId, true, token).ConfigureAwait(false);
        var current = OfficialCatalogProductSnapshot.Capture(product, await OriginalAsync(origin, token).ConfigureAwait(false));
        var version = await ProductVersionAsync(product.Id, token).ConfigureAwait(false);
        try { refresh.RequireApplicable(command.ApprovedVersion, current, version, actor.ActorId, now); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { throw DomainError(error); }
        var proposal = refresh.Proposed!.Proposal;
        if (!await OfficialCatalogDatabaseAccess.LockCategoryAsync(context, proposal.CategoryId, token).ConfigureAwait(false)) throw OfficialCatalogErrors.Error("refresh_category_invalid");
        // The transaction-local selector is not authorization; database guards independently require the approved
        // immutable revision, matching baseline, permitted columns, current product version and paired history/audit.
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('medipos.refresh_id',{refresh.Id.ToString()},true)", token).ConfigureAwait(false);
        product.CorrectOfficialMetadata(proposal.Name, proposal.Laboratory, proposal.CategoryId!.Value);
        await context.SaveChangesAsync(token).ConfigureAwait(false);
        refresh.Apply(command.ApprovedVersion, product, current, version, actor.ActorId, now);
        await context.SaveChangesAsync(token).ConfigureAwait(false);
        History(refresh, "applied", "Applied the exact approved administrative metadata; clinical and commercial data preserved.", actor, now);
        await ReauthorizeAsync(actor, token).ConfigureAwait(false); await context.SaveChangesAsync(token).ConfigureAwait(false);
        try { await transaction.CommitAsync(token).ConfigureAwait(false); }
        catch (PostgresException error) when (error.SqlState == "23505") { throw OfficialCatalogErrors.Error("refresh_stale_product", ErrorCategory.Conflict); }
        return new(refresh.Id, refresh.GlobalProductId, refresh.Version, refresh.AppliedProductVersion!.Value, false);
    }
    public async Task<OfficialCatalogRefreshPage> QueueAsync(GetOfficialCatalogRefreshQueueCommand command, CancellationToken token)
    {
        Reviewer(); await AccessAsync(token).ConfigureAwait(false);
        var rows = await OfficialCatalogRefreshQueries.Queue(context.OfficialCatalogRefreshes.AsNoTracking(), command).ToArrayAsync(token).ConfigureAwait(false);
        return new(rows.Take(command.Limit).ToArray(), rows.Length > command.Limit);
    }
    public async Task<OfficialCatalogVersionHistoryPage> HistoryAsync(GetOfficialCatalogRefreshHistoryCommand command, CancellationToken token)
    {
        Reviewer(); await AccessAsync(token).ConfigureAwait(false);
        var rows = await OfficialCatalogRefreshQueries.History(context.OfficialCatalogVersionHistories.AsNoTracking(), command).ToArrayAsync(token).ConfigureAwait(false);
        return new(rows.Take(command.Limit).ToArray(), rows.Length > command.Limit);
    }
    public async Task RecordFailureAsync(Guid target, long? version, OfficialCatalogActor actor, string action, string code, DateTimeOffset now, CancellationToken token)
    {
        context.ChangeTracker.Clear(); await AccessAsync(token).ConfigureAwait(false);
        context.OfficialCatalogPlatformAudits.Add(OfficialCatalogPlatformAudit.Create(target, version, actor.ActorId, actor.CorrelationId, action, now, code));
        await context.SaveChangesAsync(token).ConfigureAwait(false);
    }
    public ValueTask DisposeAsync() => context.DisposeAsync();
}
