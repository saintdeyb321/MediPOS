using System.Text;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog.OfficialImport;
using MediPOS.Domain.Modules.Catalog.OfficialImport;
using MediPOS.Infrastructure.Modules.Catalog.OfficialImport;

namespace MediPOS.UnitTests.Modules.Catalog.OfficialImport;

// Synthetic data and pure application ports. No EF substitute, transaction, RLS or database concurrency claim.
internal sealed class OfficialCatalogTestSetup
{
    internal static OfficialCatalogSource Source { get; } = new("synthetic", "Synthetic fixture institution", "fixture://synthetic-csv-v1",
        "Synthetic tests only", "Representative fields, no real official coverage", "Opaque textual fixture identifier");
    internal static string Header => string.Join(',', OfficialCatalogInput.RequiredColumns);
    internal Clock Time { get; } = new();
    internal Authority Auth { get; } = new();
    internal SourceRegistry Sources { get; } = new();
    internal MemoryStore Store { get; } = new();
    internal Observations Observer { get; } = new();
    internal OfficialCatalogImportOptions Options { get; set; } = new() { WritePageSize = 2 };
    internal StageOfficialCatalogImportHandler Handler => new(Auth, Sources, new OfficialCatalogFilePreparer(), new OfficialCatalogCsvReader(), Store, Options, Time, Observer);
    internal async Task<OfficialCatalogBatchResult> Run(string csv, string version = "v1", CancellationToken? token = null)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        return await Handler.HandleAsync(new(Source.Code, version, null, "synthetic.csv", OfficialCatalogInput.Format, input), token ?? TestContext.Current.CancellationToken);
    }
    internal static Dictionary<string, string> Fields(string id = "0001", string name = "Producto") => new(StringComparer.Ordinal)
    {
        ["SourceRecordId"] = id,
        ["Name"] = name,
        ["Laboratory"] = "Laboratorio",
        ["Registration"] = "REG-A",
        ["Presentation"] = "Frasco 60 mL",
        ["Ingredients"] = "Ingrediente",
        ["Strength"] = "500 mg/5 mL",
        ["DosageForm"] = "Solución",
        ["Route"] = "Oral",
    };
    internal static string Csv(params Dictionary<string, string>[] rows) => Header + "\r\n" + string.Join("\r\n", rows.Select(row => string.Join(',', OfficialCatalogInput.RequiredColumns.Select(column => Quote(row[column]))))) + "\r\n";
    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    internal sealed class Clock : TimeProvider
    { internal DateTimeOffset Now { get; set; } = new(2026, 10, 12, 14, 0, 0, TimeSpan.Zero); public override DateTimeOffset GetUtcNow() => Now; }
    internal sealed class Authority : IOfficialCatalogAdministration
    {
        internal Guid Actor { get; } = Guid.NewGuid(); internal bool Allowed { get; set; } = true;
        public Task<OfficialCatalogActor?> GetAuthorizedActorAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult<OfficialCatalogActor?>(Allowed ? new(Actor, "123456789abcdef0123456789abcdef0") : null); }
    }
    internal sealed class SourceRegistry : IOfficialCatalogSourceRegistry
    { internal bool Allowed { get; set; } = true; public OfficialCatalogSource? FindAuthorizedSource(string code) => Allowed && code == Source.Code ? Source : null; }
    internal sealed class Observations : IOfficialCatalogImportObserver
    { internal List<OfficialCatalogImportObservation> Items { get; } = []; public void Record(OfficialCatalogImportObservation observation) => Items.Add(observation); }
    internal sealed class MemoryStore : IOfficialCatalogImportStore
    {
        internal Dictionary<Guid, OfficialCatalogImportBatch> Batches { get; } = [];
        internal List<OfficialCatalogStagingRow> Rows { get; } = [];
        internal int AppendCalls { get; private set; }
        internal int ThrowOnAppend { get; set; }
        internal Action? AfterAppend { get; set; }
        public Task<OfficialCatalogClaim> ClaimAsync(OfficialCatalogImportBatch candidate, OfficialCatalogActor actor, DateTimeOffset now, TimeSpan duration, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); var batch = Batches.Values.SingleOrDefault(value => value.SourceCode == candidate.SourceCode && value.SourceVersion == candidate.SourceVersion);
            if (batch is null) { batch = candidate; Batches.Add(batch.Id, batch); }
            if (batch.SourceChecksumSha256 != candidate.SourceChecksumSha256) throw OfficialCatalogErrors.Error("source_version_conflict", ErrorCategory.Conflict);
            if (batch.SourcePublishedAtUtc != candidate.SourcePublishedAtUtc) throw OfficialCatalogErrors.Error("source_metadata_conflict", ErrorCategory.Conflict);
            if (batch.Status == "processing" && batch.ProcessingLeaseExpiresAtUtc <= now) batch.Fail(batch.ProcessingLeaseId!.Value, "official_catalog.processing_interrupted", now);
            Guid? lease = null;
            if (batch.Status == "pending" || batch.Status == "failed" && OfficialCatalogErrors.CanRetry(batch.FailureCode) && batch.AttemptCount < OfficialCatalogImportBatch.MaximumAttempts)
            { lease = Guid.CreateVersion7(); batch.Start(lease.Value, actor.ActorId, actor.CorrelationId, now, now.Add(duration)); }
            return Task.FromResult(new OfficialCatalogClaim(batch, lease));
        }
        public Task AppendAsync(Guid id, Guid lease, IReadOnlyList<OfficialCatalogStagingRow> page, DateTimeOffset now, TimeSpan duration, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); AppendCalls++; if (AppendCalls == ThrowOnAppend) throw new IOException("Synthetic interrupted write.");
            var batch = Batches[id]; if (!batch.OwnsLiveLease(lease, now)) throw OfficialCatalogErrors.Error("processing_lease_lost", ErrorCategory.Conflict);
            var anchors = new OfficialCatalogDuplicateAnchors();
            foreach (var row in Rows.Where(row => row.ImportBatchId == id).OrderBy(row => row.RowNumber))
            {
                if (row.SourceRecordIdFingerprint is not null) anchors.SourceIds.TryAdd(row.SourceRecordIdFingerprint, row.RowNumber);
                anchors.ExactRows.TryAdd(row.RowChecksumSha256, row.RowNumber); anchors.PotentialRows.TryAdd(row.PotentialDuplicateFingerprint, row.RowNumber);
            }
            foreach (var row in page) anchors.Observe(row);
            Rows.AddRange(page); batch.Progress(lease, page.Count(row => row.ValidationStatus == "valid"), page.Count(row => row.ValidationStatus == "invalid"), page.Count(row => row.DuplicateSignals.Length != 0), now, now.Add(duration));
            AfterAppend?.Invoke(); return Task.CompletedTask;
        }
        public Task<OfficialCatalogImportBatch> CompleteAsync(Guid id, Guid lease, int count, DateTimeOffset now, CancellationToken token)
        { token.ThrowIfCancellationRequested(); var batch = Batches[id]; batch.Complete(lease, count, now); return Task.FromResult(batch); }
        public Task<bool> FailAsync(Guid id, Guid lease, string code, DateTimeOffset now, CancellationToken token)
        {
            var batch = Batches[id]; if (batch.Status != "processing" || batch.ProcessingLeaseId != lease) return Task.FromResult(false);
            batch.Fail(lease, code, now); return Task.FromResult(true);
        }
        public Task<OfficialCatalogImportBatch?> FindAsync(Guid id, CancellationToken token) => Task.FromResult(Batches.GetValueOrDefault(id));
        public Task<OfficialCatalogStoredPage> ReadPageAsync(GetOfficialCatalogStagingRowsCommand query, int take, CancellationToken token) => Task.FromResult(new OfficialCatalogStoredPage(Batches.GetValueOrDefault(query.BatchId),
            Rows.Where(row => row.ImportBatchId == query.BatchId && (query.Status is null || row.ValidationStatus == query.Status) && (query.ErrorCode is null || row.ValidationErrorCodes.Contains(query.ErrorCode, StringComparer.Ordinal)))
                .OrderBy(row => row.RowNumber).ThenBy(row => row.Id).Skip(query.Offset).Take(take).ToArray()));
    }
}
