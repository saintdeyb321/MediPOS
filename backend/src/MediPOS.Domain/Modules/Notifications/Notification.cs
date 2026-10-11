using System.Globalization;

namespace MediPOS.Domain.Modules.Notifications;

public sealed class Notification
{
    public const string LotExpiring30Days = "lot_expiring_30d";
    private Notification() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public Guid InventoryLotId { get; private set; }
    public Guid BusinessProductId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public DateOnly ExpirationDate { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // The scanner supplies validated lot references. Product names are returned separately as plain data:
    // arbitrary product content never becomes notification markup, links or a historical message.
    public static Notification ExpiringLot(Guid tenantId, Guid branchId, Guid lotId, Guid productId,
        DateOnly expirationDate, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty || branchId == Guid.Empty || lotId == Guid.Empty || productId == Guid.Empty)
            throw new ArgumentException("Notification references are required.");
        return new Notification
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            BranchId = branchId,
            InventoryLotId = lotId,
            BusinessProductId = productId,
            Type = LotExpiring30Days,
            Title = "Medicamento próximo a vencer",
            Message = $"Un lote de medicamento vence el {expirationDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}.",
            ExpirationDate = expirationDate,
            CreatedAt = now.ToUniversalTime(),
        };
    }
}
