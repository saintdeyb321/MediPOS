namespace MediPOS.Application.Modules.OfflineSync;

public sealed record GetBranchCatalogSnapshotCommand(Guid TenantId, Guid DeviceId, string? Cursor = null, int PageSize = 100, Guid? BranchId = null);
public sealed record GetBranchCatalogChangesCommand(Guid TenantId, Guid DeviceId, string? Cursor, int PageSize = 100, Guid? BranchId = null);

public sealed record BranchCatalogUnit(Guid ProductUnitId, string Name, decimal ConversionToBase, bool IsBaseUnit, bool IsActive);
public sealed record BranchCatalogMedicineComponent(string Ingredient, string StrengthNormalized);
public sealed record BranchCatalogMedicine(IReadOnlyList<string> ActiveIngredients, string NormalizedStrength, string DosageForm,
    string? Route, string? SanitaryRegistration, string? CanonicalDosageForm, string? CanonicalRoute, string? EquivalenceKey,
    IReadOnlyList<BranchCatalogMedicineComponent> Components);
public sealed record BranchCatalogProduct(Guid BusinessProductId, string InternalCode, string Name, string ProductType,
    Guid CategoryId, string CategoryName, string BrandOrLaboratory, string? Barcode, decimal RetailPrice, decimal? WholesalePrice,
    bool IsActive, Guid? GlobalProductId, BranchCatalogMedicine? Medicine, IReadOnlyList<BranchCatalogUnit> Units);
public sealed record BranchCatalogStock(decimal PhysicalStockBase, decimal SellableStockBase, decimal ExpiredStockBase, DateTimeOffset StockAsOfUtc);
// Apply only newer versions per product. Upsert replaces the entire product and unit collection; absent units are removed.
// Tombstone removes the product and all its units. Never infer sellability from an offline cached quantity at checkout.
public sealed record BranchCatalogItem(Guid BusinessProductId, long Version, string Operation, BranchCatalogProduct? Product, BranchCatalogStock? Stock);
public sealed record BranchCatalogPage(int SchemaVersion, Guid BranchId, DateTimeOffset ServerGeneratedAtUtc,
    IReadOnlyList<BranchCatalogItem> Items, string? NextCursor, bool HasMore, bool IsFullSnapshot, bool RequiresFullResync, DateTimeOffset StockAsOfUtc)
{
    public string? ResultCode { get; init; }
    public bool StockIsAdvisory { get; } = true;
}

// Internal projection data, never an HTTP response: buckets contain quantities/dates, no lot IDs or financial ledger.
public sealed record BranchCatalogStockBucket(DateOnly? ExpirationDate, decimal QuantityBase);
public sealed record BranchCatalogProjection(BranchCatalogProduct Product, IReadOnlyList<BranchCatalogStockBucket> StockBuckets);
public sealed record BranchCatalogStoredItem(Guid EntityId, long Version, string Operation, BranchCatalogProjection? Projection);

public sealed record BranchCatalogCursor(int SchemaVersion, Guid TenantId, Guid BranchId, Guid DeviceId, string Kind,
    long AfterVersion, long ThroughVersion, Guid? AfterProductId, int PageSize, DateOnly BusinessDate,
    DateTimeOffset WindowAtUtc, DateTimeOffset ExpiresAtUtc);

public interface IBranchCatalogSyncReader
{
    // Short repeatable-read transaction per request. Dispose before revalidating current access/device.
    Task<IBranchCatalogReadSession> OpenAsync(Guid tenantId, Guid branchId, CancellationToken token);
}
public interface IBranchCatalogReadSession : IAsyncDisposable
{
    long CurrentVersion { get; }
    long MinimumRetainedVersion { get; }
    BranchCatalogCursor? DecodeCursor(string cursor);
    string EncodeCursor(BranchCatalogCursor cursor);
    Task<IReadOnlyList<BranchCatalogStoredItem>> SnapshotAsync(Guid? afterProductId, int take, CancellationToken token);
    Task<IReadOnlyList<BranchCatalogStoredItem>> ChangesAsync(long afterVersion, long throughVersion, int take, CancellationToken token);
}
