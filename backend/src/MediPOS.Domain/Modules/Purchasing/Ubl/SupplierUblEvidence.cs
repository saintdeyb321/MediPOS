using System.Text.Json;
using MediPOS.Domain.Modules.Catalog;

namespace MediPOS.Domain.Modules.Purchasing.Ubl;

// Parsed documentary evidence. None of these values is an inventory cost or a receipt instruction.
public sealed record SupplierUblInvoiceEvidence(string UblVersion, string Customization, string DocumentType,
    string OriginalDocumentId, string Series, string Correlative, DateOnly IssueDate, string Currency,
    string SupplierRuc, string SupplierName, string BuyerRuc, string BuyerName,
    decimal Subtotal, decimal Discounts, decimal Charges, decimal Tax, decimal Total, decimal TaxInclusive,
    decimal? TaxExclusive, decimal Prepaid, decimal Rounding, IReadOnlyList<SupplierUblLineEvidence> Lines,
    IReadOnlyList<string> BlockingIssues, IReadOnlyList<string> ReviewWarnings);

public sealed record SupplierUblLineEvidence(int Number, string OriginalLineId, string Description,
    string? SupplierCode, string? BuyerCode, string? StandardCode, string? StandardCodeScheme,
    decimal Quantity, string UnitCode, decimal UnitPrice, decimal PriceBaseQuantity,
    decimal Discounts, decimal Charges, decimal Tax, decimal LineAmount, decimal? GrossUnitPrice,
    string TaxEvidenceJson, string AllowanceEvidenceJson, IReadOnlyList<string> ReviewWarnings);

public sealed record SupplierUblReceipt(decimal Quantity, string? BatchNumber, DateOnly? ExpirationDate);

public static class SupplierUblRules
{
    public const int MaximumLines = 1000;
    public const int MaximumReceiptsPerLine = 100;
    public const decimal MoneyTolerance = 0.01m;
    public static bool IsRuc(string value) => value.Length == 11 && value.All(char.IsAsciiDigit);
    public static void Require(bool condition, string code)
    { if (!condition) throw new InvalidOperationException("ubl." + code); }
    public static void ValidateBuyer(Guid tenantId, Guid branchTenantId, Guid legalTenantId, string legalRuc, string buyerRuc) =>
        Require(tenantId != Guid.Empty && branchTenantId == tenantId && legalTenantId == tenantId && IsRuc(legalRuc) && legalRuc == buyerRuc, "buyer_mismatch");
    public static void ValidateReceipt(BusinessProduct product, ProductUnit unit, Guid tenantId, decimal quantity,
        decimal cost, bool presentationConfirmed, string notes, IReadOnlyList<SupplierUblReceipt> receipts)
    {
        Require(product.TenantId == tenantId && unit.TenantId == tenantId && unit.BusinessProductId == product.Id && product.IsActive && unit.IsActive, "mapping_invalid");
        Require(presentationConfirmed && !string.IsNullOrWhiteSpace(notes) && notes.Length <= 2048 && !notes.Any(char.IsControl), "review_required");
        Require(quantity > 0 && cost >= 0 && receipts.Count is > 0 and <= MaximumReceiptsPerLine, "receipt_invalid");
        Require(receipts.All(value => value is not null && value.Quantity > 0 &&
            (value.BatchNumber is null || value.BatchNumber.Length is > 0 and <= 128 && value.BatchNumber == value.BatchNumber.Trim() && !value.BatchNumber.Any(char.IsControl))), "receipt_invalid");
        Require(receipts.Sum(value => value.Quantity) == quantity, "receipt_quantity_mismatch");
        if (product.ProductType == ProductType.Medicine)
            Require(receipts.All(value => !string.IsNullOrWhiteSpace(value.BatchNumber) && value.ExpirationDate.HasValue), "medicine_receipt_required");
        foreach (var value in receipts) _ = unit.ToBaseQuantity(value.Quantity);
    }
    public static string Json<T>(T value) => JsonSerializer.Serialize(value);
}
