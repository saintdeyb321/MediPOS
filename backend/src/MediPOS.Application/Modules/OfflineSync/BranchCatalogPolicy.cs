using MediPOS.Domain.Modules.Inventory;

namespace MediPOS.Application.Modules.OfflineSync;

public static class BranchCatalogPolicy
{
    public const int SchemaVersion = 1;
    public const int MaximumPageSize = 500;
    public const string ResyncCode = "sync.catalog_resync_required";
    public const bool RetainsAllChanges = true;
    private static readonly TimeZoneInfo Lima = TimeZoneInfo.FindSystemTimeZoneById("America/Lima");
    public static DateOnly BusinessDate(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Lima).DateTime);
    public static bool IsValid(BranchCatalogCursor cursor, Guid tenant, Guid branch, Guid device, int pageSize,
        long currentVersion, long minimumRetained, DateTimeOffset now) =>
        cursor.SchemaVersion == SchemaVersion && cursor.TenantId == tenant && cursor.BranchId == branch && cursor.DeviceId == device &&
        cursor.PageSize == pageSize && cursor.Kind is "snapshot" or "checkpoint" or "delta" &&
        cursor.AfterVersion >= minimumRetained && cursor.AfterVersion >= 0 && cursor.ThroughVersion >= cursor.AfterVersion && cursor.ThroughVersion <= currentVersion &&
        cursor.BusinessDate == BusinessDate(now) && cursor.WindowAtUtc.Offset == TimeSpan.Zero && cursor.WindowAtUtc != default && cursor.WindowAtUtc <= now &&
        cursor.BusinessDate == BusinessDate(cursor.WindowAtUtc) && cursor.ExpiresAtUtc.Offset == TimeSpan.Zero && cursor.ExpiresAtUtc > now &&
        cursor.ExpiresAtUtc == cursor.WindowAtUtc.AddDays(7) &&
        (cursor.Kind == "snapshot" ? cursor.AfterVersion == 0 && cursor.AfterProductId.HasValue && cursor.AfterProductId != Guid.Empty : !cursor.AfterProductId.HasValue) &&
        (cursor.Kind != "checkpoint" || cursor.AfterVersion == cursor.ThroughVersion);

    public static BranchCatalogItem Project(BranchCatalogStoredItem item, DateOnly today, DateTimeOffset asOf)
    {
        if (item.EntityId == Guid.Empty || item.Version < 0 || item.Operation is not ("upsert" or "tombstone")) throw new InvalidOperationException("Invalid catalog history.");
        if (item.Operation == "tombstone") return new(item.EntityId, item.Version, item.Operation, null, null);
        var projection = item.Projection ?? throw new InvalidOperationException("An upsert requires its complete projection.");
        if (projection.Product.BusinessProductId != item.EntityId || projection.Product.ProductType is not ("medicine" or "retail"))
            throw new InvalidOperationException("Projection identity/type is inconsistent.");
        var physical = 0m; var sellable = 0m; var expired = 0m;
        foreach (var bucket in projection.StockBuckets)
        {
            if (bucket.QuantityBase < 0) throw new InvalidOperationException("Stock projection cannot be negative.");
            physical = StockQuantity.Add(physical, bucket.QuantityBase);
            if (projection.Product.ProductType == "retail" || bucket.ExpirationDate >= today) sellable = StockQuantity.Add(sellable, bucket.QuantityBase);
            else if (bucket.ExpirationDate < today) expired = StockQuantity.Add(expired, bucket.QuantityBase);
        }
        return new(item.EntityId, item.Version, item.Operation, projection.Product, new(physical, sellable, expired, asOf));
    }
}
