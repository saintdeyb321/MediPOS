namespace MediPOS.Domain.Modules.Transfers;

public enum TransferStatus { Requested, Approved, InTransit, Received, Cancelled }
public static class TransferStatusCodes
{
    public static string ToCode(TransferStatus status) => status switch
    {
        TransferStatus.Requested => "requested",
        TransferStatus.Approved => "approved",
        TransferStatus.InTransit => "in_transit",
        TransferStatus.Received => "received",
        TransferStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
    public static TransferStatus FromCode(string code) => code switch
    {
        "requested" => TransferStatus.Requested,
        "approved" => TransferStatus.Approved,
        "in_transit" => TransferStatus.InTransit,
        "received" => TransferStatus.Received,
        "cancelled" => TransferStatus.Cancelled,
        _ => throw new InvalidOperationException("Unknown transfer status."),
    };
}
public static class TransferQuantity
{
    public const decimal Maximum = 9999999999999999.999999999999m; // numeric(28,12).
    public static bool IsValid(decimal quantity, bool zero = false) =>
        (zero ? quantity >= 0 : quantity > 0) && quantity <= Maximum && decimal.Round(quantity, 12) == quantity;
    public static void Require(decimal quantity, bool zero = false)
    {
        if (!IsValid(quantity, zero)) throw new ArgumentOutOfRangeException(nameof(quantity), "Transfer quantities must fit numeric(28,12) exactly.");
    }
}
public sealed class Transfer
{
    public const int MaximumLines = 200;
    public const int MaximumAllocations = 1000;
    private readonly List<TransferLine> _lines = [];
    private Transfer() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SourceBranchId { get; private set; }
    public Guid DestinationBranchId { get; private set; }
    public TransferStatus Status { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyList<TransferLine> Lines => _lines.AsReadOnly();
    public static Transfer Request(Guid tenantId, Guid source, Guid destination, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty || source == Guid.Empty || destination == Guid.Empty || source == destination)
            throw new ArgumentException("Distinct valid branches in one tenant are required.");
        return new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            SourceBranchId = source,
            DestinationBranchId = destination,
            Status = TransferStatus.Requested,
            RequestedAt = now.ToUniversalTime(),
            UpdatedAt = now.ToUniversalTime()
        };
    }
    public void SetRequestedLines(IReadOnlyList<TransferLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (Status != TransferStatus.Requested || _lines.Count != 0 || lines.Count is 0 or > MaximumLines ||
            lines.Select(l => (l.BusinessProductId, l.ProductUnitIdSnapshot)).Distinct().Count() != lines.Count || lines.Select(l => l.Id).Distinct().Count() != lines.Count)
            throw new ArgumentException("A request requires bounded, distinct product/presentation lines.");
        foreach (var line in lines) line.ValidateFor(this);
        _lines.AddRange(lines);
    }
    public void Validate()
    {
        if (Id == Guid.Empty || TenantId == Guid.Empty || SourceBranchId == Guid.Empty || DestinationBranchId == Guid.Empty ||
            SourceBranchId == DestinationBranchId || !Enum.IsDefined(Status) || RequestedAt.Offset != TimeSpan.Zero ||
            UpdatedAt.Offset != TimeSpan.Zero || UpdatedAt < RequestedAt || _lines.Count is 0 or > MaximumLines ||
            _lines.Select(l => l.Id).Distinct().Count() != _lines.Count ||
            _lines.Select(l => (l.BusinessProductId, l.ProductUnitIdSnapshot)).Distinct().Count() != _lines.Count)
            throw new ArgumentException("Invalid transfer history.");
        foreach (var line in _lines) line.ValidateFor(this);
    }
    public void Approve(DateTimeOffset now) => Change(TransferStatus.Requested, TransferStatus.Approved, now);
    public void Dispatch(DateTimeOffset now) => Change(TransferStatus.Approved, TransferStatus.InTransit, now);
    public void Receive(DateTimeOffset now) => Change(TransferStatus.InTransit, TransferStatus.Received, now);
    public void Cancel(DateTimeOffset now)
    {
        if (Status is not (TransferStatus.Requested or TransferStatus.Approved)) throw new InvalidOperationException("Dispatched transfers cannot be cancelled.");
        Change(Status, TransferStatus.Cancelled, now);
    }
    private void Change(TransferStatus before, TransferStatus after, DateTimeOffset now)
    {
        if (Status != before) throw new InvalidOperationException("Invalid transfer transition.");
        Validate();
        var at = now.ToUniversalTime();
        if (at < UpdatedAt) throw new ArgumentOutOfRangeException(nameof(now));
        Status = after; UpdatedAt = at;
    }
}
