namespace MediPOS.Domain.Modules.SalesPos;

public sealed class SalePaymentReversal
{
    private SalePaymentReversal() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SaleId { get; private set; }
    public Guid SalePaymentId { get; private set; }
    public PaymentMethod Method { get; private set; }
    public decimal Amount { get; private set; }
    public Guid ActorId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    public static SalePaymentReversal Reverse(Sale sale, SalePayment original, Guid actorId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(sale);
        ArgumentNullException.ThrowIfNull(original);
        if (sale.Status != SaleStatus.Confirmed) throw new InvalidOperationException("Payment reversal requires a confirmed sale.");
        original.ValidateFor(sale);
        if (actorId == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(actorId));
        var at = now.ToUniversalTime();
        if (!sale.ConfirmedAt.HasValue || at < sale.UpdatedAt) throw new ArgumentOutOfRangeException(nameof(now));
        return new SalePaymentReversal
        {
            Id = Guid.CreateVersion7(),
            TenantId = sale.TenantId,
            SaleId = sale.Id,
            SalePaymentId = original.Id,
            Method = original.Method,
            Amount = original.Amount,
            ActorId = actorId,
            OccurredAt = at,
        };
    }

    public void ValidateAgainst(Sale sale, SalePayment original)
    {
        ArgumentNullException.ThrowIfNull(sale);
        ArgumentNullException.ThrowIfNull(original);
        original.ValidateFor(sale);
        if (Id == Guid.Empty || TenantId != sale.TenantId || SaleId != sale.Id || SalePaymentId != original.Id ||
            Method != original.Method || Amount != original.Amount || ActorId == Guid.Empty || OccurredAt.Offset != TimeSpan.Zero ||
            !sale.ConfirmedAt.HasValue || OccurredAt < sale.ConfirmedAt)
            throw new ArgumentException("Payment reversal must exactly match the original payment and sale.");
    }
}
