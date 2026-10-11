namespace MediPOS.Domain.Modules.Inventory;

public sealed class BranchProductStockThreshold
{
    public const decimal MaximumMinimumStock = 9999999999999999.999999999999m; // numeric(28,12).
    private BranchProductStockThreshold() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid BusinessProductId { get; private set; }
    public decimal MinimumStockBase { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid UpdatedByActorId { get; private set; }

    public static bool IsValidMinimum(decimal value) => value >= 0 && value <= MaximumMinimumStock && decimal.Round(value, 12) == value;

    public static BranchProductStockThreshold Create(Guid tenantId, Guid branchId, Guid productId, decimal minimum, Guid actorId, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty || branchId == Guid.Empty || productId == Guid.Empty || actorId == Guid.Empty)
            throw new ArgumentException("Tenant, branch, product and authenticated actor are required.");
        if (!IsValidMinimum(minimum)) throw new ArgumentOutOfRangeException(nameof(minimum), "Minimum stock must fit numeric(28,12) exactly.");
        return new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            BranchId = branchId,
            BusinessProductId = productId,
            MinimumStockBase = minimum,
            UpdatedAt = now.ToUniversalTime(),
            UpdatedByActorId = actorId,
        };
    }

    public bool SetMinimum(decimal minimum, Guid actorId, DateTimeOffset now)
    {
        Validate();
        if (!IsValidMinimum(minimum)) throw new ArgumentOutOfRangeException(nameof(minimum));
        if (actorId == Guid.Empty) throw new ArgumentException("An authenticated actor is required.", nameof(actorId));
        var at = now.ToUniversalTime();
        if (at < UpdatedAt) throw new ArgumentOutOfRangeException(nameof(now));
        if (minimum == MinimumStockBase) return false;
        MinimumStockBase = minimum; UpdatedAt = at; UpdatedByActorId = actorId;
        return true;
    }

    public void Validate()
    {
        if (Id == Guid.Empty || Id.Version != 7 || TenantId == Guid.Empty || BranchId == Guid.Empty || BusinessProductId == Guid.Empty ||
            UpdatedByActorId == Guid.Empty || UpdatedAt.Offset != TimeSpan.Zero || !IsValidMinimum(MinimumStockBase))
            throw new ArgumentException("Invalid branch/product stock threshold.");
    }
}
