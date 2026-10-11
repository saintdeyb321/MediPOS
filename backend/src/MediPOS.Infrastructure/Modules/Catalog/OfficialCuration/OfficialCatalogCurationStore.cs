using System.Data;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog.OfficialCuration;
using MediPOS.Application.Modules.Catalog.OfficialImport;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Catalog.OfficialCuration;
using MediPOS.Domain.Modules.Catalog.OfficialImport;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediPOS.Infrastructure.Modules.Catalog.OfficialCuration;

internal sealed class OfficialCatalogCurationStore(MediPosDbContext context, IOfficialCatalogCurationAdministration authority,
    bool publisher) : IOfficialCatalogCurationStore, IOfficialCatalogPublicationStore, IAsyncDisposable
{
    private async Task AccessAsync(CancellationToken token)
    {
        var capability = publisher ? "medipos_catalog_publisher" : "medipos_catalog_reviewer";
        var opposite = publisher ? "medipos_catalog_reviewer" : "medipos_catalog_publisher";
        var allowed = await context.Database.SqlQuery<bool>($"""
            SELECT NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication AND pg_has_role(current_user,{capability},'USAGE')
              AND NOT pg_has_role(current_user,'medipos_catalog_importer','USAGE')
              AND NOT has_schema_privilege(current_user,'public','CREATE')
              AND NOT has_table_privilege(current_user,'official_catalog_curations','DELETE,TRUNCATE,TRIGGER')
              AND NOT has_table_privilege(current_user,'official_catalog_revisions','UPDATE,DELETE,TRUNCATE,TRIGGER')
              AND NOT has_table_privilege(current_user,'official_catalog_publications','UPDATE,DELETE,TRUNCATE,TRIGGER')
              AND NOT has_table_privilege(current_user,'official_catalog_platform_audits','UPDATE,DELETE,TRUNCATE,TRIGGER')
              AND NOT has_any_column_privilege(current_user,'categories','INSERT,UPDATE')
              AND NOT pg_has_role(current_user,{opposite},'USAGE')
              AND NOT EXISTS (
                SELECT 1 FROM pg_class private_table JOIN pg_namespace ns ON ns.oid = private_table.relnamespace
                JOIN pg_attribute ownership ON ownership.attrelid = private_table.oid AND ownership.attname = 'tenant_id' AND NOT ownership.attisdropped
                WHERE ns.nspname = 'public' AND private_table.relkind IN ('r','p') AND
                  (has_table_privilege(current_user,private_table.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER') OR
                   has_any_column_privilege(current_user,private_table.oid,'SELECT,INSERT,UPDATE')))
              AND NOT has_table_privilege(current_user,'inventory_lots','SELECT')
              AND NOT has_table_privilege(current_user,'business_products','UPDATE')
              AND NOT has_table_privilege(current_user,'official_catalog_staging_rows','INSERT')
              AND NOT has_any_column_privilege(current_user,'global_products','UPDATE')
              AND NOT has_any_column_privilege(current_user,'medicine_profiles','UPDATE')
              AND (has_table_privilege(current_user,'global_products','INSERT') = {publisher})
              AND (has_table_privilege(current_user,'medicine_profiles','INSERT') = {publisher}) AS "Value"
            FROM pg_roles WHERE rolname = current_user
            """).SingleAsync(token).ConfigureAwait(false);
        if (!allowed) throw OfficialCatalogErrors.Error("curation_database_role_denied", ErrorCategory.Forbidden);
    }
    private async Task ReauthorizeAsync(OfficialCatalogActor initial, CancellationToken token)
    {
        var actor = await OfficialCurationAccess.RequireAsync(authority, publisher, token).ConfigureAwait(false);
        if (actor.ActorId != initial.ActorId) throw OfficialCatalogErrors.Error("curation_actor_changed", ErrorCategory.Forbidden);
    }
    private void ReviewOnly()
    { if (publisher) throw OfficialCatalogErrors.Error("curation_reviewer_required", ErrorCategory.Forbidden); }
    private static ApplicationErrorException DomainError(Exception error)
    {
        var code = error.Message switch
        {
            "batch_not_staged" or "candidate_terminal" or "already_approved" or "candidate_incomplete" or "approval_required" or
            "stale_review" or "duplicate_evidence_changed" or "identity_conflict" or "duplicate_conflict" or "duplicate_unresolved" or "link_not_verifiable" or
            "review_invalid" or "duplicate_resolution_invalid" or "correction_required" or "correction_requires_separate_review" or
            "review_action_invalid" or "proposal_invalid" or "curation_time_invalid" => error.Message,
            _ => "data_invalid",
        };
        return OfficialCatalogErrors.Error("curation_" + code, error is InvalidOperationException ? ErrorCategory.Conflict : ErrorCategory.Validation);
    }
    private async Task<OfficialCatalogCuration> FindAsync(Guid id, bool locked, CancellationToken token)
    {
        var query = locked ? context.OfficialCatalogCurations.FromSqlInterpolated($"SELECT * FROM official_catalog_curations WHERE id = {id} FOR UPDATE") : context.OfficialCatalogCurations;
        return await query.SingleOrDefaultAsync(candidate => candidate.Id == id, token).ConfigureAwait(false) ??
            throw OfficialCatalogErrors.Error("curation_not_found", ErrorCategory.NotFound);
    }
    private async Task<OfficialCatalogImportBatch> BatchAsync(OfficialCatalogCuration candidate, CancellationToken token)
    {
        var batch = await context.OfficialCatalogImportBatches.AsNoTracking().SingleAsync(batch => batch.Id == candidate.ImportBatchId, token).ConfigureAwait(false);
        if (batch.Status != OfficialCatalogStates.Staged) throw OfficialCatalogErrors.Error("curation_batch_not_staged", ErrorCategory.Conflict);
        return batch;
    }
    private async Task CategoryAsync(OfficialCatalogProposal proposal, CancellationToken token)
    {
        var category = proposal.CategoryId.HasValue ? await context.Categories.FromSqlInterpolated($"SELECT * FROM categories WHERE id = {proposal.CategoryId.Value} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(token).ConfigureAwait(false) : null;
        if (!proposal.HasValidCategory(category)) throw OfficialCatalogErrors.Error("curation_category_invalid");
    }
    private static void Identity(OfficialCatalogCuration candidate, OfficialCatalogProposal proposal)
    {
        if (!string.IsNullOrWhiteSpace(candidate.SourceRecordId) && candidate.SourceRecordId != proposal.OfficialIdentity)
            throw OfficialCatalogErrors.Error("curation_identity_incoherent", ErrorCategory.Conflict);
    }
    private void History(OfficialCatalogCuration candidate, long previous, string action, OfficialCatalogActor actor, string reason, DateTimeOffset now)
    {
        context.OfficialCatalogRevisions.Add(OfficialCatalogRevision.Capture(candidate, previous, action, actor.ActorId, actor.CorrelationId, reason, now));
        context.OfficialCatalogPlatformAudits.Add(OfficialCatalogPlatformAudit.Create(candidate.Id, candidate.Version, actor.ActorId, actor.CorrelationId, action, now));
    }
    public async Task<OfficialCatalogCuration> PrepareAsync(Guid rowId, OfficialCatalogActor actor, DateTimeOffset now, CancellationToken token)
    {
        ReviewOnly(); await AccessAsync(token).ConfigureAwait(false); context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(token).ConfigureAwait(false);
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"curation-row:" + rowId.ToString()},0))", token).ConfigureAwait(false);
        var existing = await context.OfficialCatalogCurations.SingleOrDefaultAsync(candidate => candidate.StagingRowId == rowId, token).ConfigureAwait(false);
        if (existing is not null) { await ReauthorizeAsync(actor, token).ConfigureAwait(false); await transaction.CommitAsync(token).ConfigureAwait(false); return existing; }
        var row = await context.OfficialCatalogStagingRows.AsNoTracking().SingleOrDefaultAsync(row => row.Id == rowId, token).ConfigureAwait(false) ?? throw OfficialCatalogErrors.Error("curation_staging_not_found", ErrorCategory.NotFound);
        var batch = await context.OfficialCatalogImportBatches.AsNoTracking().SingleAsync(batch => batch.Id == row.ImportBatchId, token).ConfigureAwait(false);
        var proposal = OfficialCatalogProposal.FromStaging(row);
        var matches = await MatchAsync(proposal, batch.SourceCode, 0, 101, token).ConfigureAwait(false);
        OfficialCatalogCuration candidate;
        try { candidate = OfficialCatalogCuration.Create(batch, row, proposal, matches, now); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { throw DomainError(error); }
        context.OfficialCatalogCurations.Add(candidate); await context.SaveChangesAsync(token).ConfigureAwait(false);
        History(candidate, 0, "normalized", actor, "Source text retained; clinical association and identity require explicit administrative verification.", now);
        await ReauthorizeAsync(actor, token).ConfigureAwait(false); await context.SaveChangesAsync(token).ConfigureAwait(false);
        await transaction.CommitAsync(token).ConfigureAwait(false); return candidate;
    }
    public async Task<OfficialCatalogCuration> ReviewAsync(ReviewOfficialCatalogCandidateCommand command, OfficialCatalogActor actor, DateTimeOffset now, CancellationToken token)
    {
        ReviewOnly(); await AccessAsync(token).ConfigureAwait(false); context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(token).ConfigureAwait(false);
        var candidate = await FindAsync(command.CandidateId, false, token).ConfigureAwait(false);
        try { candidate.RequireVersion(command.ExpectedVersion); }
        catch (InvalidOperationException error) { throw DomainError(error); }
        await BatchAsync(candidate, token).ConfigureAwait(false);
        var proposal = command.CorrectedProposal ?? candidate.Proposal; Identity(candidate, proposal);
        if (command.Action == "approve") await CategoryAsync(proposal, token).ConfigureAwait(false);
        var matches = await MatchAsync(proposal, candidate.SourceCode, 0, 101, token).ConfigureAwait(false);
        if (matches.Count > 100 && command.Action == "approve") throw OfficialCatalogErrors.Error("curation_duplicate_review_limit", ErrorCategory.Conflict);
        try
        {
            candidate.Review(command.ExpectedVersion, command.Action, command.CorrectedProposal, command.Reason, command.DuplicateResolution,
            command.ExistingGlobalProductId, actor.ActorId, now, matches);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { throw DomainError(error); }
        await ReauthorizeAsync(actor, token).ConfigureAwait(false);
        try
        {
            await context.SaveChangesAsync(token).ConfigureAwait(false);
            History(candidate, command.ExpectedVersion, command.Action, actor, command.Reason, now);
            await context.SaveChangesAsync(token).ConfigureAwait(false); await transaction.CommitAsync(token).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException) { throw OfficialCatalogErrors.Error("curation_stale_review", ErrorCategory.Conflict); }
        return candidate;
    }
    public async Task<OfficialCatalogPublishResult> PublishAsync(PublishApprovedOfficialCatalogCandidateCommand command, OfficialCatalogActor actor, DateTimeOffset now, CancellationToken token)
    {
        try { return await PublishCoreAsync(command, actor, now, token).ConfigureAwait(false); }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: "23505" })
        { throw OfficialCatalogErrors.Error("curation_identity_conflict", ErrorCategory.Conflict); }
    }
    private async Task<OfficialCatalogPublishResult> PublishCoreAsync(PublishApprovedOfficialCatalogCandidateCommand command, OfficialCatalogActor actor, DateTimeOffset now, CancellationToken token)
    {
        if (!publisher) throw OfficialCatalogErrors.Error("curation_publisher_required", ErrorCategory.Forbidden);
        await AccessAsync(token).ConfigureAwait(false); context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(token).ConfigureAwait(false);
        var candidate = await FindAsync(command.CandidateId, true, token).ConfigureAwait(false);
        if (candidate.Status == OfficialCurationStates.Published)
        {
            try { _ = candidate.PublishedProductFor(command.ApprovedVersion); }
            catch (InvalidOperationException error) { throw DomainError(error); }
            var saved = await context.OfficialCatalogPublications.SingleAsync(origin => origin.CurationId == candidate.Id && origin.GlobalProductId == candidate.PublishedGlobalProductId, token).ConfigureAwait(false);
            await ReauthorizeAsync(actor, token).ConfigureAwait(false); await transaction.CommitAsync(token).ConfigureAwait(false);
            return new(candidate.Id, saved.GlobalProductId, candidate.Version, true);
        }
        try { candidate.RequirePublishable(command.ApprovedVersion, candidate.DuplicateEvidence); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { throw DomainError(error); }
        await BatchAsync(candidate, token).ConfigureAwait(false); var proposal = candidate.Proposal; Identity(candidate, proposal);
        await CategoryAsync(proposal, token).ConfigureAwait(false);
        // Different candidates/versions/sources sharing an identity or exact approved attributes serialize before rechecking matches.
        foreach (var key in new[] { "f:" + proposal.Fingerprint(), "i:" + candidate.SourceCode + ":" + OfficialCatalogProposal.Hash(proposal.OfficialIdentity ?? string.Empty) }.Order(StringComparer.Ordinal))
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key},0))", token).ConfigureAwait(false);
        var matches = await MatchAsync(proposal, candidate.SourceCode, 0, 101, token).ConfigureAwait(false);
        if (matches.Count > 100) throw OfficialCatalogErrors.Error("curation_duplicate_review_limit", ErrorCategory.Conflict);
        try { candidate.RequirePublishable(command.ApprovedVersion, matches); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { throw DomainError(error); }
        Guid globalId;
        if (candidate.DuplicateResolution == "link_existing")
        {
            globalId = candidate.ExistingGlobalProductId!.Value;
            if (!await context.GlobalProducts.AnyAsync(product => product.Id == globalId && product.IsActive, token).ConfigureAwait(false)) throw OfficialCatalogErrors.Error("curation_global_product_inactive", ErrorCategory.Conflict);
        }
        else
        {
            var product = proposal.CreateGlobalProduct(now); globalId = product.Id;
            context.GlobalProducts.Add(product); await context.SaveChangesAsync(token).ConfigureAwait(false);
        }
        var origin = OfficialCatalogPublication.Create(candidate, globalId, actor.ActorId, now);
        context.OfficialCatalogPublications.Add(origin); await context.SaveChangesAsync(token).ConfigureAwait(false);
        candidate.Publish(command.ApprovedVersion, globalId, now, matches);
        await context.SaveChangesAsync(token).ConfigureAwait(false);
        History(candidate, command.ApprovedVersion, "published", actor, "Individually published from the current approved revision.", now);
        await ReauthorizeAsync(actor, token).ConfigureAwait(false);
        try { await context.SaveChangesAsync(token).ConfigureAwait(false); await transaction.CommitAsync(token).ConfigureAwait(false); }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: "23505" })
        { throw OfficialCatalogErrors.Error("curation_identity_conflict", ErrorCategory.Conflict); }
        return new(candidate.Id, globalId, candidate.Version, false);
    }
    private async Task<IReadOnlyList<OfficialDuplicateEvidence>> MatchAsync(OfficialCatalogProposal proposal, string source, int offset, int take, CancellationToken token)
    {
        OfficialCurationAccess.Page(offset, Math.Min(take, 100));
        if (take is < 1 or > 101) throw OfficialCatalogErrors.Error("curation_query_invalid");
        string? fingerprint = null, equivalence = null;
        try { fingerprint = proposal.Fingerprint(); equivalence = proposal.EquivalenceKey; }
        catch (ArgumentException) { /* Incomplete proposals still participate in name, registration and identity screening. */ }
        var page = await OfficialCatalogCurationQueries.Matches(context.GlobalProducts, context.MedicineProfiles,
            context.OfficialCatalogPublications, source, proposal.OfficialIdentity, OfficialCatalogProposal.TextKey(proposal.Name),
            OfficialCatalogProposal.TextKey(proposal.Registration ?? string.Empty), fingerprint, equivalence, offset, take).ToArrayAsync(token).ConfigureAwait(false);
        return page.Select(item => OfficialCatalogDuplicateClassifier.Classify(proposal, source, item.Product, item.Origin, item.Medicine?.Data))
            .Where(match => match is not null).Select(match => match!).ToArray();
    }
    public async Task<OfficialCatalogCandidateResult> GetAsync(GetOfficialCatalogCandidateCommand command, CancellationToken token)
    {
        await AccessAsync(token).ConfigureAwait(false); context.ChangeTracker.Clear(); OfficialCurationAccess.Page(command.RevisionOffset, command.RevisionLimit); OfficialCurationAccess.Page(command.AuditOffset, command.AuditLimit);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token).ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", token).ConfigureAwait(false);
        var candidate = await context.OfficialCatalogCurations.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == command.CandidateId, token).ConfigureAwait(false) ?? throw OfficialCatalogErrors.Error("curation_not_found", ErrorCategory.NotFound);
        var original = await context.OfficialCatalogStagingRows.AsNoTracking().SingleAsync(row => row.Id == candidate.StagingRowId, token).ConfigureAwait(false);
        var batch = await context.OfficialCatalogImportBatches.AsNoTracking().SingleAsync(batch => batch.Id == candidate.ImportBatchId, token).ConfigureAwait(false);
        var decisions = await context.OfficialCatalogRevisions.AsNoTracking().Where(revision => revision.CurationId == candidate.Id).OrderBy(revision => revision.Version)
            .Skip(command.RevisionOffset).Take(command.RevisionLimit + 1).ToArrayAsync(token).ConfigureAwait(false);
        var publication = await context.OfficialCatalogPublications.AsNoTracking().SingleOrDefaultAsync(origin => origin.CurationId == candidate.Id, token).ConfigureAwait(false);
        var audits = await context.OfficialCatalogPlatformAudits.AsNoTracking().Where(audit => audit.CandidateId == candidate.Id).OrderBy(audit => audit.AtUtc).ThenBy(audit => audit.Id)
            .Skip(command.AuditOffset).Take(command.AuditLimit + 1).ToArrayAsync(token).ConfigureAwait(false);
        await transaction.CommitAsync(token).ConfigureAwait(false);
        return new(candidate, original, batch, decisions.Take(command.RevisionLimit).ToArray(), publication, decisions.Length > command.RevisionLimit,
            audits.Take(command.AuditLimit).ToArray(), audits.Length > command.AuditLimit);
    }
    public async Task<OfficialCatalogCurationPage> QueueAsync(GetOfficialCatalogCurationQueueCommand command, CancellationToken token)
    {
        await AccessAsync(token).ConfigureAwait(false); OfficialCurationAccess.Page(command.Offset, command.Limit);
        var query = context.OfficialCatalogCurations.AsNoTracking();
        if (command.BatchId.HasValue) query = query.Where(candidate => candidate.ImportBatchId == command.BatchId);
        if (command.SourceCode is not null) query = query.Where(candidate => candidate.SourceCode == command.SourceCode);
        if (command.Status is not null) query = query.Where(candidate => candidate.Status == command.Status);
        if (command.Error is not null) query = query.Where(candidate => candidate.Errors.Contains(command.Error));
        if (command.HasPotentialMatch.HasValue) query = query.Where(candidate => candidate.HasPotentialMatches == command.HasPotentialMatch);
        var rows = await query.OrderBy(candidate => candidate.CreatedAtUtc).ThenBy(candidate => candidate.Id).Skip(command.Offset).Take(command.Limit + 1).ToArrayAsync(token).ConfigureAwait(false);
        return new(rows.Take(command.Limit).ToArray(), rows.Length > command.Limit);
    }
    public async Task<OfficialCatalogDuplicatePage> DuplicatesAsync(GetOfficialCatalogDuplicateCandidatesCommand command, CancellationToken token)
    {
        await AccessAsync(token).ConfigureAwait(false); OfficialCurationAccess.Page(command.Offset, command.Limit);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token).ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", token).ConfigureAwait(false);
        var candidate = await context.OfficialCatalogCurations.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == command.CandidateId, token).ConfigureAwait(false) ?? throw OfficialCatalogErrors.Error("curation_not_found", ErrorCategory.NotFound);
        var matches = await MatchAsync(candidate.Proposal, candidate.SourceCode, command.Offset, command.Limit + 1, token).ConfigureAwait(false);
        await transaction.CommitAsync(token).ConfigureAwait(false); return new(matches.Take(command.Limit).ToArray(), matches.Count > command.Limit);
    }
    public async Task RecordFailureAsync(Guid candidate, long? version, OfficialCatalogActor actor, string action, string code, DateTimeOffset now, CancellationToken token)
    {
        context.ChangeTracker.Clear(); await AccessAsync(token).ConfigureAwait(false);
        context.OfficialCatalogPlatformAudits.Add(OfficialCatalogPlatformAudit.Create(candidate, version, actor.ActorId, actor.CorrelationId, action, now, code));
        await context.SaveChangesAsync(token).ConfigureAwait(false);
    }
    public ValueTask DisposeAsync() => context.DisposeAsync();
}
