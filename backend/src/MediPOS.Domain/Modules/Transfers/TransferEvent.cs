namespace MediPOS.Domain.Modules.Transfers;

public enum TransferEventType { Requested, Approved, Dispatched, Received, Cancelled }
public static class TransferEventCodes
{
    public static string ToCode(TransferEventType type) => type switch
    {
        TransferEventType.Requested => "requested",
        TransferEventType.Approved => "approved",
        TransferEventType.Dispatched => "dispatched",
        TransferEventType.Received => "received",
        TransferEventType.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
    public static TransferEventType FromCode(string code) => code switch
    {
        "requested" => TransferEventType.Requested,
        "approved" => TransferEventType.Approved,
        "dispatched" => TransferEventType.Dispatched,
        "received" => TransferEventType.Received,
        "cancelled" => TransferEventType.Cancelled,
        _ => throw new InvalidOperationException("Unknown transfer event."),
    };
    public static TransferStatus Status(TransferEventType type) => type switch
    {
        TransferEventType.Requested => TransferStatus.Requested,
        TransferEventType.Approved => TransferStatus.Approved,
        TransferEventType.Dispatched => TransferStatus.InTransit,
        TransferEventType.Received => TransferStatus.Received,
        TransferEventType.Cancelled => TransferStatus.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
    // Explicit chronological path order, including equal server timestamps; not persisted enum ordinals.
    public static int Order(TransferEventType type) => type switch
    { TransferEventType.Requested => 0, TransferEventType.Approved => 1, TransferEventType.Dispatched => 2, TransferEventType.Received => 3, TransferEventType.Cancelled => 4, _ => -1 };
}
public sealed class TransferEvent
{
    public const int MaximumReasonLength = 512;
    private TransferEvent() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid TransferId { get; private set; }
    public TransferEventType EventType { get; private set; }
    public Guid ActorId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string? Reason { get; private set; }
    public static bool IsValidReason(string? reason) => !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length <= MaximumReasonLength;
    public static TransferEvent Create(Transfer transfer, TransferEventType type, Guid actor, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        if (actor == Guid.Empty || TransferEventCodes.Status(type) != transfer.Status ||
            (type == TransferEventType.Cancelled ? !IsValidReason(reason) : reason is not null)) throw new ArgumentException("Event must match transition, actor and cancellation reason.");
        return new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = transfer.TenantId,
            TransferId = transfer.Id,
            EventType = type,
            ActorId = actor,
            OccurredAt = transfer.UpdatedAt,
            Reason = reason?.Trim()
        };
    }
}
