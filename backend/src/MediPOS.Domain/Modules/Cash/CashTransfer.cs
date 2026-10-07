namespace MediPOS.Domain.Modules.Cash;

public enum CashTransferStatus { InTransit, Received }
public static class CashTransferStatusCodes
{
    public static string ToCode(CashTransferStatus status) => status switch
    {
        CashTransferStatus.InTransit => "in_transit",
        CashTransferStatus.Received => "received",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
    public static CashTransferStatus FromCode(string code) => code switch
    {
        "in_transit" => CashTransferStatus.InTransit,
        "received" => CashTransferStatus.Received,
        _ => throw new InvalidOperationException("Unknown cash transfer status."),
    };
}
public sealed class InsufficientCashException : InvalidOperationException
{
    public InsufficientCashException() : base("Cash transfer exceeds current expected cash.") { }
}

public sealed class CashTransfer
{
    private CashTransfer() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SourceBranchId { get; private set; }
    public Guid SourceCashSessionId { get; private set; }
    public Guid DestinationBranchId { get; private set; }
    public Guid? DestinationCashSessionId { get; private set; }
    public decimal Amount { get; private set; }
    public CashTransferStatus Status { get; private set; }
    public DateTimeOffset DispatchedAt { get; private set; }
    public Guid DispatchedByActorId { get; private set; }
    public DateTimeOffset? ReceivedAt { get; private set; }
    public Guid? ReceivedByActorId { get; private set; }
    public static bool IsValidAmount(decimal amount) => amount > 0 && CashSession.IsValidOpeningAmount(amount);

    public static CashTransfer Dispatch(CashSession source, Guid destinationBranchId, decimal amount, decimal expectedCash,
        Guid actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Status != CashSessionStatus.Open) throw new InvalidOperationException("Dispatch requires an open cash session.");
        if (destinationBranchId == Guid.Empty || actor == Guid.Empty || !IsValidAmount(amount) ||
            !CashSession.IsValidReconciliationAmount(expectedCash) || now.ToUniversalTime() < source.OpenedAt)
            throw new ArgumentException("Valid dispatch data and exact PEN amount are required.");
        if (amount > expectedCash) throw new InsufficientCashException();
        return new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = source.TenantId,
            SourceBranchId = source.BranchId,
            SourceCashSessionId = source.Id,
            DestinationBranchId = destinationBranchId,
            Amount = amount,
            Status = CashTransferStatus.InTransit,
            DispatchedAt = now.ToUniversalTime(),
            DispatchedByActorId = actor
        };
    }
    public void Receive(CashSession destination, Guid actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(destination);
        Validate();
        if (Status != CashTransferStatus.InTransit) throw new InvalidOperationException("Cash transfer can be received once.");
        var at = now.ToUniversalTime();
        if (destination.TenantId != TenantId || destination.BranchId != DestinationBranchId || destination.Id == SourceCashSessionId ||
            destination.Status != CashSessionStatus.Open || actor == Guid.Empty || at < DispatchedAt || at < destination.OpenedAt)
            throw new ArgumentException("Receipt requires a different open destination session, actor and UTC time.");
        DestinationCashSessionId = destination.Id; Status = CashTransferStatus.Received; ReceivedAt = at; ReceivedByActorId = actor;
    }
    public void Validate()
    {
        if (Id == Guid.Empty || TenantId == Guid.Empty || SourceBranchId == Guid.Empty || DestinationBranchId == Guid.Empty ||
            SourceCashSessionId == Guid.Empty || DispatchedByActorId == Guid.Empty || !IsValidAmount(Amount) || DispatchedAt.Offset != TimeSpan.Zero ||
            !Enum.IsDefined(Status) || (Status == CashTransferStatus.InTransit
                ? DestinationCashSessionId.HasValue || ReceivedAt.HasValue || ReceivedByActorId.HasValue
                : !DestinationCashSessionId.HasValue || DestinationCashSessionId == Guid.Empty || DestinationCashSessionId == SourceCashSessionId ||
                    !ReceivedAt.HasValue || ReceivedAt.Value.Offset != TimeSpan.Zero || ReceivedAt < DispatchedAt ||
                    !ReceivedByActorId.HasValue || ReceivedByActorId == Guid.Empty))
            throw new ArgumentException("Cash transfer history must be complete and immutable.");
    }
}
