using System.Diagnostics;
using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Domain.Modules.Catalog.OfficialImport;

namespace MediPOS.Application.Modules.Catalog.OfficialImport;

public sealed class StageOfficialCatalogImportHandler(IOfficialCatalogAdministration administration, IOfficialCatalogSourceRegistry sources,
    IOfficialCatalogFilePreparer preparer, IOfficialCatalogFileReader reader, IOfficialCatalogImportStore store,
    OfficialCatalogImportOptions options, TimeProvider clock, IOfficialCatalogImportObserver? observer = null)
{
    public async Task<OfficialCatalogBatchResult> HandleAsync(StageOfficialCatalogImportCommand command, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(command); token.ThrowIfCancellationRequested(); options.Validate();
        var actor = await OfficialCatalogErrors.RequireActorAsync(administration, token).ConfigureAwait(false);
        var source = sources.FindAuthorizedSource(command.SourceCode) ?? throw OfficialCatalogErrors.Error("source_not_authorized", ErrorCategory.Forbidden);
        if (source.Code != command.SourceCode) throw OfficialCatalogErrors.Error("source_not_authorized", ErrorCategory.Forbidden);
        if (command.InputFormat != OfficialCatalogInput.Format) throw OfficialCatalogErrors.Error("schema_unsupported");
        if (command.InputStream is null || !command.InputStream.CanRead) throw OfficialCatalogErrors.Error("file_unreadable");
        var timer = Stopwatch.GetTimestamp(); OfficialCatalogImportBatch? batch = null; Guid? lease = null; string? failure = null;
        try
        {
            await using var prepared = await preparer.PrepareAsync(command.InputStream, options, token).ConfigureAwait(false);
            OfficialCatalogImportBatch candidate;
            try
            {
                candidate = OfficialCatalogImportBatch.Create(new(source, command.SourceVersion, command.FileName, prepared.ChecksumSha256,
                command.PublishedAtUtc, command.InputFormat, actor.ActorId, actor.CorrelationId), clock.GetUtcNow());
            }
            catch (ArgumentException) { throw OfficialCatalogErrors.Error("metadata_invalid"); }
            actor = await OfficialCatalogErrors.RequireActorAsync(administration, token).ConfigureAwait(false);
            var claim = await store.ClaimAsync(candidate, actor, clock.GetUtcNow(), options.ProcessingLease, token).ConfigureAwait(false);
            batch = claim.Batch; lease = claim.LeaseId;
            if (!lease.HasValue) return OfficialCatalogMapping.Batch(batch);
            var resumedRows = batch.TotalRows; var count = 0; var page = new List<OfficialCatalogStagingRow>(options.WritePageSize);
            await foreach (var row in reader.ReadAsync(prepared.Content, options, token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested(); count++;
                if (row.RowNumber != count + 1) throw OfficialCatalogErrors.Error("row_sequence_invalid");
                if (count <= resumedRows) continue; // Same checksum and schema; previously committed source rows stay immutable.
                page.Add(OfficialCatalogInput.Validate(row, batch.Id, clock.GetUtcNow()));
                if (page.Count == options.WritePageSize)
                {
                    await OfficialCatalogErrors.RequireActorAsync(administration, token).ConfigureAwait(false);
                    await store.AppendAsync(batch.Id, lease.Value, page, clock.GetUtcNow(), options.ProcessingLease, token).ConfigureAwait(false); page.Clear();
                }
            }
            if (count == 0) throw OfficialCatalogErrors.Error("file_empty");
            await OfficialCatalogErrors.RequireActorAsync(administration, token).ConfigureAwait(false);
            if (page.Count != 0) await store.AppendAsync(batch.Id, lease.Value, page, clock.GetUtcNow(), options.ProcessingLease, token).ConfigureAwait(false);
            batch = await store.CompleteAsync(batch.Id, lease.Value, count, clock.GetUtcNow(), token).ConfigureAwait(false);
            return OfficialCatalogMapping.Batch(batch);
        }
        catch (OperationCanceledException)
        { failure = "official_catalog.processing_cancelled"; await DiagnoseAsync().ConfigureAwait(false); throw; }
        catch (ApplicationErrorException error)
        {
            failure = error.Error.Code;
            if (!await DiagnoseAsync().ConfigureAwait(false) || error.Error.Category is ErrorCategory.Forbidden or ErrorCategory.Conflict) throw;
            return OfficialCatalogMapping.Batch(batch!);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            failure = error is IOException ? "official_catalog.file_unreadable" : "official_catalog.persistence_failed";
            if (!await DiagnoseAsync().ConfigureAwait(false)) throw OfficialCatalogErrors.Error(failure["official_catalog.".Length..], ErrorCategory.Conflict);
            return OfficialCatalogMapping.Batch(batch!);
        }
        finally
        {
            var observedVersion = command.SourceVersion is { Length: <= 128 } version && !version.Any(char.IsControl) ? version : "invalid";
            observer?.Record(new(batch?.Id, source.Code, batch?.SourceVersion ?? observedVersion, batch?.TotalRows ?? 0,
                (long)Stopwatch.GetElapsedTime(timer).TotalMilliseconds, batch?.Status ?? "rejected", failure ?? batch?.FailureCode));
        }
        async Task<bool> DiagnoseAsync()
        {
            if (batch is null || lease is null) return false;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                if (!await store.FailAsync(batch.Id, lease.Value, failure!, clock.GetUtcNow(), timeout.Token).ConfigureAwait(false)) return false;
                batch = await store.FindAsync(batch.Id, timeout.Token).ConfigureAwait(false); return batch is not null;
            }
            catch (Exception error) when (error is not OutOfMemoryException) { return false; }
        }
    }
}

public sealed class GetOfficialCatalogImportBatchHandler(IOfficialCatalogAdministration administration, IOfficialCatalogImportStore store)
{
    public async Task<OfficialCatalogBatchResult> HandleAsync(GetOfficialCatalogImportBatchCommand command, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(command); await OfficialCatalogErrors.RequireActorAsync(administration, token).ConfigureAwait(false);
        if (command.BatchId == Guid.Empty) throw OfficialCatalogErrors.Error("batch_invalid");
        var batch = await store.FindAsync(command.BatchId, token).ConfigureAwait(false) ?? throw OfficialCatalogErrors.Error("batch_not_found", ErrorCategory.NotFound);
        await OfficialCatalogErrors.RequireActorAsync(administration, token).ConfigureAwait(false); return OfficialCatalogMapping.Batch(batch);
    }
}
public sealed class GetOfficialCatalogStagingRowsHandler(IOfficialCatalogAdministration administration, IOfficialCatalogImportStore store)
{
    public async Task<OfficialCatalogStagingPage> HandleAsync(GetOfficialCatalogStagingRowsCommand command, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(command); await OfficialCatalogErrors.RequireActorAsync(administration, token).ConfigureAwait(false);
        if (command.BatchId == Guid.Empty || command.Offset < 0 || command.Limit is < 1 or > 100 || command.Status is not (null or OfficialCatalogStates.Valid or OfficialCatalogStates.Invalid) ||
            command.ErrorCode is { } error && (error.Length is < 1 or > 96 || error.Any(c => !char.IsAsciiLetterLower(c) && c is not ('_' or '.'))))
            throw OfficialCatalogErrors.Error("query_invalid");
        var page = await store.ReadPageAsync(command, command.Limit + 1, token).ConfigureAwait(false);
        if (page.Batch is null) throw OfficialCatalogErrors.Error("batch_not_found", ErrorCategory.NotFound);
        await OfficialCatalogErrors.RequireActorAsync(administration, token).ConfigureAwait(false);
        return new(OfficialCatalogMapping.Batch(page.Batch), page.Rows.Take(command.Limit).Select(OfficialCatalogMapping.Row).ToArray(), command.Offset, command.Limit, page.Rows.Count > command.Limit);
    }
}
internal static class OfficialCatalogMapping
{
    internal static OfficialCatalogBatchResult Batch(OfficialCatalogImportBatch batch) => new(batch.Id, batch.SourceCode, batch.SourceVersion, batch.SourceFileName,
        batch.SourceChecksumSha256, batch.SourcePublishedAtUtc, batch.InputFormat, batch.SourceInstitution, batch.OfficialReference, batch.ReuseAuthorization, batch.Coverage,
        batch.RecordIdentifierDescription, batch.StartedAtUtc, batch.CompletedAtUtc, batch.Status, batch.TotalRows, batch.ValidRows, batch.InvalidRows, batch.DuplicateRows,
        batch.StartedByActorId, batch.AttemptCount, batch.FailureCode, batch.Failures);
    internal static OfficialCatalogStagingRowResult Row(OfficialCatalogStagingRow row) => new(row.Id, row.RowNumber, row.SourceRecordId, row.OriginalName,
        row.OriginalLaboratory, row.OriginalRegistration, row.OriginalPresentation, row.OriginalIngredientsText, row.OriginalStrengthText, row.OriginalDosageFormText,
        row.OriginalRouteText, JsonSerializer.Deserialize<Dictionary<string, string>>(row.RawFieldsJson)!, row.ValidationStatus, row.ValidationErrorCodes,
        row.DuplicateSignals, row.DuplicateOfRowNumber, row.CreatedAtUtc);
}
