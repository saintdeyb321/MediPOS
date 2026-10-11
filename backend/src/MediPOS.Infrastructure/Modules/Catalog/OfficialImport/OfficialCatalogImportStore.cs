using System.Data;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog.OfficialImport;
using MediPOS.Domain.Modules.Catalog.OfficialImport;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace MediPOS.Infrastructure.Modules.Catalog.OfficialImport;

internal sealed class OfficialCatalogImportStore(MediPosDbContext context) : IOfficialCatalogImportStore, IAsyncDisposable
{
    private bool _checkedRole;
    private async Task AccessAsync(CancellationToken token)
    {
        if (_checkedRole) return;
        var allowed = await context.Database.SqlQueryRaw<bool>("""
            SELECT NOT rolsuper AND NOT rolbypassrls AND pg_has_role(current_user,'medipos_catalog_importer','USAGE')
              AND NOT has_table_privilege(current_user,'inventory_lots','SELECT')
              AND NOT has_table_privilege(current_user,'business_products','UPDATE')
              AND NOT has_table_privilege(current_user,'global_products','INSERT')
              AND NOT has_table_privilege(current_user,'medicine_profiles','INSERT') AS "Value"
            FROM pg_roles WHERE rolname = current_user
            """).SingleAsync(token).ConfigureAwait(false);
        if (!allowed) throw OfficialCatalogErrors.Error("database_role_denied", ErrorCategory.Forbidden);
        _checkedRole = true;
    }
    public async Task<OfficialCatalogClaim> ClaimAsync(OfficialCatalogImportBatch candidate, OfficialCatalogActor actor, DateTimeOffset now, TimeSpan duration, CancellationToken token)
    {
        await AccessAsync(token).ConfigureAwait(false); context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(token).ConfigureAwait(false);
        var publication = new NpgsqlParameter("source_publication", NpgsqlDbType.TimestampTz) { Value = candidate.SourcePublishedAtUtc is { } published ? published : DBNull.Value };
        // ON CONFLICT waits for an uncommitted competing reservation, then locks the canonical batch.
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO official_catalog_import_batches (id,source_code,source_version,source_institution,official_reference,reuse_authorization,coverage,record_identifier_description,
                source_file_name,source_checksum_sha256,input_format,source_published_at_utc,started_at_utc,status,total_rows,valid_rows,invalid_rows,duplicate_rows,started_by_actor_id,started_correlation_id,attempt_count,failure_history)
            VALUES ({candidate.Id},{candidate.SourceCode},{candidate.SourceVersion},{candidate.SourceInstitution},{candidate.OfficialReference},{candidate.ReuseAuthorization},{candidate.Coverage},{candidate.RecordIdentifierDescription},
                {candidate.SourceFileName},{candidate.SourceChecksumSha256},{candidate.InputFormat},{publication},{candidate.StartedAtUtc},'pending',0,0,0,0,{candidate.StartedByActorId},{candidate.StartedCorrelationId},0,'[]'::jsonb)
            ON CONFLICT (source_code,source_version) DO NOTHING
            """, token).ConfigureAwait(false);
        var batch = await context.OfficialCatalogImportBatches.FromSqlInterpolated($"SELECT * FROM official_catalog_import_batches WHERE source_code = {candidate.SourceCode} AND source_version = {candidate.SourceVersion} FOR UPDATE").SingleAsync(token).ConfigureAwait(false);
        if (batch.SourceChecksumSha256 != candidate.SourceChecksumSha256) throw OfficialCatalogErrors.Error("source_version_conflict", ErrorCategory.Conflict);
        if (batch.SourcePublishedAtUtc != candidate.SourcePublishedAtUtc || batch.InputFormat != candidate.InputFormat) throw OfficialCatalogErrors.Error("source_metadata_conflict", ErrorCategory.Conflict);
        Guid? lease = null;
        if (batch.Status == OfficialCatalogStates.Processing && batch.ProcessingLeaseExpiresAtUtc <= now)
            batch.Fail(batch.ProcessingLeaseId!.Value, "official_catalog.processing_interrupted", now);
        if (batch.Status == OfficialCatalogStates.Pending || batch.Status == OfficialCatalogStates.Failed && OfficialCatalogErrors.CanRetry(batch.FailureCode) && batch.AttemptCount < OfficialCatalogImportBatch.MaximumAttempts)
        {
            // Persist the expired-attempt diagnosis before the separate failed -> processing transition.
            if (context.ChangeTracker.HasChanges()) await context.SaveChangesAsync(token).ConfigureAwait(false);
            lease = Guid.CreateVersion7(); batch.Start(lease.Value, actor.ActorId, actor.CorrelationId, now, now.Add(duration));
            await context.SaveChangesAsync(token).ConfigureAwait(false);
        }
        else if (context.ChangeTracker.HasChanges()) await context.SaveChangesAsync(token).ConfigureAwait(false);
        await transaction.CommitAsync(token).ConfigureAwait(false); context.ChangeTracker.Clear(); return new(batch, lease);
    }
    public async Task AppendAsync(Guid batchId, Guid lease, IReadOnlyList<OfficialCatalogStagingRow> rows, DateTimeOffset now, TimeSpan duration, CancellationToken token)
    {
        await AccessAsync(token).ConfigureAwait(false); context.ChangeTracker.Clear();
        if (rows.Count is < 1 or > 500) throw OfficialCatalogErrors.Error("write_page_invalid");
        await using var transaction = await context.Database.BeginTransactionAsync(token).ConfigureAwait(false);
        var batch = await LockAsync(batchId, token).ConfigureAwait(false); RequireLease(batch, lease, now);
        var expected = batch.TotalRows + 2;
        foreach (var row in rows) if (row.ImportBatchId != batchId || row.RowNumber != expected++ || row.CreatedAtUtc < batch.StartedAtUtc) throw OfficialCatalogErrors.Error("row_sequence_invalid");
        var anchors = new OfficialCatalogDuplicateAnchors();
        var ids = rows.Where(row => row.SourceRecordIdFingerprint is not null).Select(row => row.SourceRecordIdFingerprint!).Distinct(StringComparer.Ordinal).ToArray();
        var exact = rows.Select(row => row.RowChecksumSha256).Distinct(StringComparer.Ordinal).ToArray();
        var potential = rows.Select(row => row.PotentialDuplicateFingerprint).Distinct(StringComparer.Ordinal).ToArray();
        var previous = context.OfficialCatalogStagingRows.AsNoTracking().Where(row => row.ImportBatchId == batchId);
        foreach (var item in await previous.Where(row => row.SourceRecordIdFingerprint != null && ids.Contains(row.SourceRecordIdFingerprint)).GroupBy(row => row.SourceRecordIdFingerprint)
            .Select(group => new { Key = group.Key!, Row = group.Min(row => row.RowNumber) }).ToArrayAsync(token).ConfigureAwait(false)) anchors.SourceIds.Add(item.Key, item.Row);
        foreach (var item in await previous.Where(row => exact.Contains(row.RowChecksumSha256)).GroupBy(row => row.RowChecksumSha256)
            .Select(group => new { Key = group.Key, Row = group.Min(row => row.RowNumber) }).ToArrayAsync(token).ConfigureAwait(false)) anchors.ExactRows.Add(item.Key, item.Row);
        foreach (var item in await previous.Where(row => potential.Contains(row.PotentialDuplicateFingerprint)).GroupBy(row => row.PotentialDuplicateFingerprint)
            .Select(group => new { Key = group.Key, Row = group.Min(row => row.RowNumber) }).ToArrayAsync(token).ConfigureAwait(false)) anchors.PotentialRows.Add(item.Key, item.Row);
        foreach (var row in rows) anchors.Observe(row);
        context.OfficialCatalogStagingRows.AddRange(rows);
        // Persist rows before progress so the database can verify the count at completion. Both commits are one transaction.
        await context.SaveChangesAsync(token).ConfigureAwait(false);
        batch.Progress(lease, rows.Count(row => row.ValidationStatus == OfficialCatalogStates.Valid), rows.Count(row => row.ValidationStatus == OfficialCatalogStates.Invalid),
            rows.Count(row => row.DuplicateSignals.Length != 0), now, now.Add(duration));
        await context.SaveChangesAsync(token).ConfigureAwait(false); await transaction.CommitAsync(token).ConfigureAwait(false); context.ChangeTracker.Clear();
    }
    public async Task<OfficialCatalogImportBatch> CompleteAsync(Guid batchId, Guid lease, int parsedRows, DateTimeOffset now, CancellationToken token)
    {
        await AccessAsync(token).ConfigureAwait(false); context.ChangeTracker.Clear(); await using var transaction = await context.Database.BeginTransactionAsync(token).ConfigureAwait(false);
        var batch = await LockAsync(batchId, token).ConfigureAwait(false); RequireLease(batch, lease, now); batch.Complete(lease, parsedRows, now);
        await context.SaveChangesAsync(token).ConfigureAwait(false); await transaction.CommitAsync(token).ConfigureAwait(false); context.ChangeTracker.Clear(); return batch;
    }
    public async Task<bool> FailAsync(Guid batchId, Guid lease, string code, DateTimeOffset now, CancellationToken token)
    {
        await AccessAsync(token).ConfigureAwait(false); context.ChangeTracker.Clear(); await using var transaction = await context.Database.BeginTransactionAsync(token).ConfigureAwait(false);
        var batch = await LockAsync(batchId, token).ConfigureAwait(false);
        if (batch.Status != OfficialCatalogStates.Processing || batch.ProcessingLeaseId != lease) return false;
        batch.Fail(lease, code, now); await context.SaveChangesAsync(token).ConfigureAwait(false); await transaction.CommitAsync(token).ConfigureAwait(false); context.ChangeTracker.Clear(); return true;
    }
    public async Task<OfficialCatalogImportBatch?> FindAsync(Guid batch, CancellationToken token)
    { await AccessAsync(token).ConfigureAwait(false); return await context.OfficialCatalogImportBatches.AsNoTracking().SingleOrDefaultAsync(value => value.Id == batch, token).ConfigureAwait(false); }
    public async Task<OfficialCatalogStoredPage> ReadPageAsync(GetOfficialCatalogStagingRowsCommand query, int take, CancellationToken token)
    {
        await AccessAsync(token).ConfigureAwait(false);
        if (take is < 2 or > 101) throw OfficialCatalogErrors.Error("query_invalid");
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token).ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", token).ConfigureAwait(false);
        var batch = await FindAsync(query.BatchId, token).ConfigureAwait(false);
        var rows = context.OfficialCatalogStagingRows.AsNoTracking().Where(row => row.ImportBatchId == query.BatchId);
        if (query.Status is not null) rows = rows.Where(row => row.ValidationStatus == query.Status);
        if (query.ErrorCode is not null) rows = rows.Where(row => row.ValidationErrorCodes.Contains(query.ErrorCode));
        var page = await rows.OrderBy(row => row.RowNumber).ThenBy(row => row.Id).Skip(query.Offset).Take(take).ToArrayAsync(token).ConfigureAwait(false);
        return new(batch, page);
    }
    private Task<OfficialCatalogImportBatch> LockAsync(Guid id, CancellationToken token) => context.OfficialCatalogImportBatches
        .FromSqlInterpolated($"SELECT * FROM official_catalog_import_batches WHERE id = {id} FOR UPDATE").SingleAsync(token);
    private static void RequireLease(OfficialCatalogImportBatch batch, Guid lease, DateTimeOffset now)
    { if (!batch.OwnsLiveLease(lease, now)) throw OfficialCatalogErrors.Error("processing_lease_lost", ErrorCategory.Conflict); }
    public ValueTask DisposeAsync() => context.DisposeAsync();
}
