namespace MediPOS.Domain.Modules.SalesPos;

public enum PaymentMethod { Cash, Yape, Plin, Card, Transfer }

public static class PaymentMethodCodes
{
    public static string ToCode(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "cash",
        PaymentMethod.Yape => "yape",
        PaymentMethod.Plin => "plin",
        PaymentMethod.Card => "card",
        PaymentMethod.Transfer => "transfer",
        _ => throw new ArgumentOutOfRangeException(nameof(method)),
    };
    public static PaymentMethod FromCode(string code) => code switch
    {
        "cash" => PaymentMethod.Cash,
        "yape" => PaymentMethod.Yape,
        "plin" => PaymentMethod.Plin,
        "card" => PaymentMethod.Card,
        "transfer" => PaymentMethod.Transfer,
        _ => throw new InvalidOperationException("Unknown persisted sale payment method."),
    };
}

public sealed class SalePayment
{
    private SalePayment() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SaleId { get; private set; }
    public PaymentMethod Method { get; private set; }
    public decimal Amount { get; private set; }

    public static bool IsValidAmount(decimal amount) =>
        amount > 0 && amount <= Sale.MaximumAmount && decimal.Round(amount, 4) == amount;

    public static SalePayment Create(Sale sale, PaymentMethod method, decimal amount)
    {
        ArgumentNullException.ThrowIfNull(sale);
        sale.EnsureDraft();
        if (!Enum.IsDefined(method)) throw new ArgumentOutOfRangeException(nameof(method));
        if (!IsValidAmount(amount)) throw new ArgumentOutOfRangeException(nameof(amount), "Payment must be positive and fit numeric(18,4) exactly.");
        return new SalePayment { Id = Guid.CreateVersion7(), TenantId = sale.TenantId, SaleId = sale.Id, Method = method, Amount = amount };
    }

    public void ValidateFor(Sale sale)
    {
        ArgumentNullException.ThrowIfNull(sale);
        if (Id == Guid.Empty || TenantId != sale.TenantId || SaleId != sale.Id || !Enum.IsDefined(Method) || !IsValidAmount(Amount))
            throw new ArgumentException("Payment must belong to this sale and preserve valid method/amount.");
    }
}
