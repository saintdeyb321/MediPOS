using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediPOS.Application.Modules.OfflineSync;
using MediPOS.Domain.Modules.OfflineSync;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MediPOS.Infrastructure.Modules.OfflineSync.Persistence;

internal sealed class BranchCatalogSyncReader(MediPosDbContext context) : IBranchCatalogSyncReader
{
    public async Task<IBranchCatalogReadSession> OpenAsync(Guid tenantId, Guid branchId, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges()) throw new InvalidOperationException("Catalog reads require a clean independent scope.");
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token).ConfigureAwait(false);
        try
        {
            await context.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", token).ConfigureAwait(false);
            var state = await context.Set<BranchCatalogSyncState>().AsNoTracking().SingleAsync(value => value.TenantId == tenantId && value.BranchId == branchId, token).ConfigureAwait(false);
            return new Session(context, transaction, state);
        }
        catch { await transaction.DisposeAsync().ConfigureAwait(false); throw; }
    }
    private sealed class Session(MediPosDbContext context, IDbContextTransaction transaction, BranchCatalogSyncState state) : IBranchCatalogReadSession
    {
        private readonly BranchCatalogCursorCodec _codec = new(state.CursorKey);
        public long CurrentVersion => state.CurrentVersion;
        public long MinimumRetainedVersion => state.MinimumRetainedVersion;
        public BranchCatalogCursor? DecodeCursor(string cursor) => _codec.Decode(cursor);
        public string EncodeCursor(BranchCatalogCursor cursor) => _codec.Encode(cursor);
        public async Task<IReadOnlyList<BranchCatalogStoredItem>> SnapshotAsync(Guid? afterProductId, int take, CancellationToken token)
        {
            var after = afterProductId ?? Guid.Empty;
            // Keyset uses PostgreSQL's UUID ordering consistently for both predicates and sorting.
            var ids = await context.Database.SqlQuery<Guid>($"""
                SELECT id AS "Value" FROM business_products WHERE tenant_id = {state.TenantId} AND id > {after}
                ORDER BY id LIMIT {take}
                """).ToArrayAsync(token).ConfigureAwait(false);
            if (ids.Length == 0) return [];
            var rows = await context.Database.SqlQuery<ProjectionRow>($"""
                SELECT entity_id AS "EntityId", projection::text AS "Projection"
                FROM branch_catalog_projections({state.TenantId}, {state.BranchId}, {ids}) ORDER BY entity_id
                """).ToArrayAsync(token).ConfigureAwait(false);
            return rows.Select(row => new BranchCatalogStoredItem(row.EntityId, state.CurrentVersion, "upsert", ReadProjection(row.Projection))).ToArray();
        }
        public async Task<IReadOnlyList<BranchCatalogStoredItem>> ChangesAsync(long afterVersion, long throughVersion, int take, CancellationToken token)
        {
            var rows = await context.Set<BranchCatalogChange>().AsNoTracking().Where(value => value.TenantId == state.TenantId && value.BranchId == state.BranchId &&
                value.Version > afterVersion && value.Version <= throughVersion).OrderBy(value => value.Version).Take(take)
                .Select(value => new { value.EntityId, value.Version, value.Operation, value.Projection }).ToArrayAsync(token).ConfigureAwait(false);
            return rows.Select(row => new BranchCatalogStoredItem(row.EntityId, row.Version, row.Operation, row.Projection is null ? null : ReadProjection(row.Projection))).ToArray();
        }
        public async ValueTask DisposeAsync()
        {
            try { await transaction.DisposeAsync().ConfigureAwait(false); }
            finally { _codec.Dispose(); System.Security.Cryptography.CryptographicOperations.ZeroMemory(state.CursorKey); }
        }
    }
    private sealed class ProjectionRow
    {
        public Guid EntityId { get; set; }
        public string Projection { get; set; } = string.Empty;
    }
    private static readonly JsonSerializerOptions ProjectionOptions = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, NumberHandling = JsonNumberHandling.Strict };
    private static BranchCatalogProjection ReadProjection(string json)
    {
        using var document = JsonDocument.Parse(json); RequireExactNumbers(document.RootElement);
        return JsonSerializer.Deserialize<BranchCatalogProjection>(json, ProjectionOptions) ?? throw new InvalidOperationException("Missing catalog projection.");
    }
    private static void RequireExactNumbers(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            var number = element.GetDecimal();
            if (SyncPayload.Canonicalize("{\"n\":" + element.GetRawText() + "}").Json != SyncPayload.Canonicalize("{\"n\":" + JsonSerializer.Serialize(number) + "}").Json)
                throw new InvalidOperationException("Catalog quantities must be exactly representable as decimal.");
        }
        else if (element.ValueKind == JsonValueKind.Object) foreach (var property in element.EnumerateObject()) RequireExactNumbers(property.Value);
        else if (element.ValueKind == JsonValueKind.Array) foreach (var value in element.EnumerateArray()) RequireExactNumbers(value);
    }
}
