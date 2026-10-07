using System.Globalization;
using MediPOS.Domain.Modules.SalesPos;

namespace MediPOS.Application.Modules.SalesPos;

// These names and legal display fields are current data, not transaction snapshots.
public sealed record InternalTicketCurrentDisplay(Guid TenantId, string BusinessName, Guid LegalEntityId, string LegalName,
    string Ruc, Guid BranchId, string BranchName, Guid SellerMembershipId, Guid SellerUserId, string SellerDisplayName);
public sealed record InternalTicketLine(string ProductName, string UnitName, decimal Quantity, decimal BaseQuantity,
    decimal ConversionToBase, decimal UnitPrice, string PriceKind, decimal LineTotal)
{
    // SaleLine.UnitPriceSnapshot is a price per base unit; LineTotal uses BaseQuantity, not Quantity.
    public string UnitPriceBasis { get; } = "BASE_UNIT";
}
public sealed record InternalTicketPayment(string Method, decimal Amount);
public sealed record InternalTicketSnapshot(Sale Sale, IReadOnlyList<SalePayment> Payments, InternalTicketCurrentDisplay CurrentDisplayData);

public sealed record InternalTicket(Guid SaleId, Guid CashSessionId, string TicketNumber, InternalTicketCurrentDisplay CurrentDisplayData,
    DateTimeOffset SaleDateTime, string Status, string StatusLabel, bool IsVoided, DateTimeOffset? VoidedAt, string? VoidReason,
    IReadOnlyList<InternalTicketLine> Lines, IReadOnlyList<InternalTicketPayment> Payments, decimal TotalAmount)
{
    private static readonly IReadOnlyList<int> PrintWidths = Array.AsReadOnly<int>([58, 80]);
    public string DocumentType { get; } = "INTERNAL_TICKET";
    public string Footer { get; } = "DOCUMENTO INTERNO - NO ES COMPROBANTE DE PAGO ELECTRÓNICO";
    public IReadOnlyList<int> SupportedPrintWidthsMm { get; } = PrintWidths;

    public static InternalTicket From(InternalTicketSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var sale = snapshot.Sale;
        if (sale.Status is not (SaleStatus.Confirmed or SaleStatus.Voided))
            throw new ArgumentException("Only confirmed or voided history has a final internal ticket.", nameof(snapshot));
        sale.ValidateForCheckout();
        sale.ValidatePayments(snapshot.Payments);
        var display = snapshot.CurrentDisplayData;
        if (display.TenantId != sale.TenantId || display.BranchId != sale.BranchId || display.SellerMembershipId != sale.SellerMembershipId ||
            display.LegalEntityId == Guid.Empty || display.SellerUserId == Guid.Empty || string.IsNullOrWhiteSpace(display.BusinessName) ||
            string.IsNullOrWhiteSpace(display.LegalName) || string.IsNullOrWhiteSpace(display.Ruc) ||
            string.IsNullOrWhiteSpace(display.BranchName) || string.IsNullOrWhiteSpace(display.SellerDisplayName))
            throw new ArgumentException("Current display data must belong to the sale's business, branch and seller.", nameof(snapshot));
        var lines = sale.Lines.OrderBy(line => line.Id).Select(line => new InternalTicketLine(line.ProductNameSnapshot, line.UnitNameSnapshot,
            line.Quantity, line.BaseQuantity, line.ConversionToBaseSnapshot, line.UnitPriceSnapshot, PriceKindCodes.ToCode(line.PriceKind), line.LineTotal)).ToArray();
        var payments = snapshot.Payments.OrderBy(payment => PaymentMethodCodes.ToCode(payment.Method), StringComparer.Ordinal)
            .Select(payment => new InternalTicketPayment(PaymentMethodCodes.ToCode(payment.Method), payment.Amount)).ToArray();
        // Full UUID prevents ambiguous truncation without introducing a fiscal sequence or numbering table.
        return new(sale.Id, sale.CashSessionId, "MP-" + sale.Id.ToString("N", CultureInfo.InvariantCulture), display,
            sale.ConfirmedAt!.Value, SaleStatusCodes.ToCode(sale.Status), sale.Status == SaleStatus.Voided ? "ANULADA" : "CONFIRMADA",
            sale.Status == SaleStatus.Voided, sale.VoidedAt, sale.VoidReason, Array.AsReadOnly(lines), Array.AsReadOnly(payments), sale.TotalAmount);
    }
}

public interface IInternalTicketReader
{
    Task<InternalTicketSnapshot?> FindAsync(Guid tenantId, Guid branchId, Guid saleId, CancellationToken cancellationToken);
}
