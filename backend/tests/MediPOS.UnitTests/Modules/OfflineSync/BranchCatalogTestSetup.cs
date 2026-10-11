using System.Security.Cryptography;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.OfflineSync;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.UnitTests.Modules.OfflineSync;

// Pure application ports only; MVCC, commit order and RLS are covered by external PostgreSQL tests.
internal sealed class BranchCatalogTestSetup
{
    internal BranchCatalogTestSetup(int products = 3, TenantRole role = TenantRole.Owner)
    {
        Sync = new(role); Reader = new(products);
        Queries = new(new ResolveAccessContextHandler(Sync.Identity, Sync.Access, new TenantDataContext()), Sync.Data, Reader, Sync.Clock);
        Snapshot = new(Queries); Changes = new(Queries);
    }
    internal OfflineSyncBatchTestSetup Sync { get; }
    internal MemoryReader Reader { get; }
    internal BranchCatalogSyncQueries Queries { get; }
    internal GetBranchCatalogSnapshotHandler Snapshot { get; }
    internal GetBranchCatalogChangesHandler Changes { get; }
    internal Task<BranchCatalogPage> ReadSnapshot(string? cursor = null, int size = 100, Guid? branch = null) =>
        Snapshot.HandleAsync(new(Sync.Member.TenantId, Sync.Device.Id, cursor, size, branch), TestContext.Current.CancellationToken);
    internal Task<BranchCatalogPage> ReadChanges(string? cursor, int size = 100) =>
        Changes.HandleAsync(new(Sync.Member.TenantId, Sync.Device.Id, cursor, size), TestContext.Current.CancellationToken);
    internal sealed class MemoryReader : IBranchCatalogSyncReader
    {
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
        internal MemoryReader(int count)
        {
            Products = Enumerable.Range(0, count).Select(_ =>
            {
                var id = Guid.CreateVersion7(); var product = new BranchCatalogProduct(id, "SKU", "Producto", "retail", Guid.NewGuid(), "Categoría", "Marca",
                    null, 2.1254m, null, true, null, null, [new(Guid.CreateVersion7(), "Unidad", 1m, true, true), new(Guid.CreateVersion7(), "Caja", 10m, false, false)]);
                return new BranchCatalogStoredItem(id, 0, "upsert", new(product, []));
            }).OrderBy(row => row.EntityId).ToList();
        }
        internal List<BranchCatalogStoredItem> Products { get; }
        internal List<BranchCatalogStoredItem> Log { get; } = [];
        internal long Version { get; set; }
        internal long MinimumRetained { get; set; }
        internal int Opens { get; private set; }
        internal int Active { get; private set; }
        internal int Takes { get; private set; }
        internal Action? DuringRead { get; set; }
        internal string Encode(BranchCatalogCursor cursor) { using var codec = new BranchCatalogCursorCodec(_key); return codec.Encode(cursor); }
        internal BranchCatalogCursor Decode(string cursor) { using var codec = new BranchCatalogCursorCodec(_key); return codec.Decode(cursor)!; }
        internal void Append(BranchCatalogStoredItem row) { Version++; Log.Add(row with { Version = Version }); }
        public Task<IBranchCatalogReadSession> OpenAsync(Guid tenant, Guid branch, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Opens++; Active++; return Task.FromResult<IBranchCatalogReadSession>(new Session(this, Version, MinimumRetained, Products.ToArray())); }
        private sealed class Session(MemoryReader reader, long version, long minimum, BranchCatalogStoredItem[] products) : IBranchCatalogReadSession
        {
            public long CurrentVersion => version;
            public long MinimumRetainedVersion => minimum;
            public BranchCatalogCursor? DecodeCursor(string cursor) { using var codec = new BranchCatalogCursorCodec(reader._key); return codec.Decode(cursor); }
            public string EncodeCursor(BranchCatalogCursor cursor) => reader.Encode(cursor);
            public Task<IReadOnlyList<BranchCatalogStoredItem>> SnapshotAsync(Guid? after, int take, CancellationToken token)
            { token.ThrowIfCancellationRequested(); reader.Takes = take; reader.DuringRead?.Invoke(); return Task.FromResult<IReadOnlyList<BranchCatalogStoredItem>>(products.Where(row => !after.HasValue || row.EntityId.CompareTo(after.Value) > 0).Take(take).ToArray()); }
            public Task<IReadOnlyList<BranchCatalogStoredItem>> ChangesAsync(long after, long through, int take, CancellationToken token)
            { token.ThrowIfCancellationRequested(); reader.Takes = take; reader.DuringRead?.Invoke(); return Task.FromResult<IReadOnlyList<BranchCatalogStoredItem>>(reader.Log.Where(row => row.Version > after && row.Version <= through).OrderBy(row => row.Version).Take(take).ToArray()); }
            public ValueTask DisposeAsync() { reader.Active--; return ValueTask.CompletedTask; }
        }
    }
}
