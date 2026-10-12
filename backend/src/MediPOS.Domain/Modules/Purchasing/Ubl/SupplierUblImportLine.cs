using System.Text.Json;
using MediPOS.Domain.Modules.Catalog;

namespace MediPOS.Domain.Modules.Purchasing.Ubl;

public sealed class SupplierUblImportLine
{
    private SupplierUblImportLine() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ImportId { get; private set; }
    public int LineNumber { get; private set; }
    public string Description { get; private set; } = "";
    public string? SupplierProductCode { get; private set; }
    public string? BuyerProductCode { get; private set; }
    public string? StandardProductCode { get; private set; }
    public string? StandardCodeScheme { get; private set; }
    public decimal DocumentaryQuantity { get; private set; }
    public string DocumentaryUnit { get; private set; } = "";
    public decimal UnitPrice { get; private set; }
    public decimal Tax { get; private set; }
    public decimal LineAmount { get; private set; }
    public string EvidenceJson { get; private set; } = "";
    public string SuggestionStatus { get; private set; } = "unmatched";
    public Guid? SuggestedBusinessProductId { get; private set; }
    public Guid? SuggestedProductUnitId { get; private set; }
    public Guid? BusinessProductId { get; private set; }
    public Guid? ProductUnitId { get; private set; }
    public string ReviewStatus { get; private set; } = "requires_review";
    public string? ReviewNotes { get; private set; }
    public decimal? ReceivedQuantity { get; private set; }
    public decimal? ApprovedUnitCost { get; private set; }
    public decimal? ApprovedConversionToBase { get; private set; }
    public string? ApprovedUnitName { get; private set; }
    public string ReceiptPlanJson { get; private set; } = "[]";
    public IReadOnlyList<SupplierUblReceipt> Receipts => JsonSerializer.Deserialize<SupplierUblReceipt[]>(ReceiptPlanJson)!;
    public static SupplierUblImportLine Create(SupplierUblImport import, SupplierUblLineEvidence evidence, string suggestion = "unmatched", Guid? product = null, Guid? unit = null) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = import.TenantId,
        ImportId = import.Id,
        LineNumber = evidence.Number,
        Description = evidence.Description,
        SupplierProductCode = evidence.SupplierCode,
        BuyerProductCode = evidence.BuyerCode,
        StandardProductCode = evidence.StandardCode,
        StandardCodeScheme = evidence.StandardCodeScheme,
        DocumentaryQuantity = evidence.Quantity,
        DocumentaryUnit = evidence.UnitCode,
        UnitPrice = evidence.UnitPrice,
        Tax = evidence.Tax,
        LineAmount = evidence.LineAmount,
        EvidenceJson = SupplierUblRules.Json(evidence),
        SuggestionStatus = suggestion,
        SuggestedBusinessProductId = product,
        SuggestedProductUnitId = unit,
    };
    public void ClearMapping(SupplierUblImport import, string notes)
    {
        import.RequireEditable(); SupplierUblRules.Require(import.TenantId == TenantId && import.Id == ImportId && !string.IsNullOrWhiteSpace(notes) && notes.Length <= 2048, "review_invalid");
        BusinessProductId = null; ProductUnitId = null; ReviewStatus = "requires_review"; ReviewNotes = notes;
        ReceivedQuantity = null; ApprovedUnitCost = null; ApprovedConversionToBase = null; ApprovedUnitName = null; ReceiptPlanJson = "[]";
    }
    public void ApproveMapping(SupplierUblImport import, BusinessProduct product, ProductUnit unit, decimal receivedQuantity,
        decimal unitCost, bool presentationConfirmed, string notes, IReadOnlyList<SupplierUblReceipt> receipts)
    {
        import.RequireEditable(); SupplierUblRules.Require(import.Id == ImportId && import.TenantId == TenantId, "scope_invalid");
        SupplierUblRules.ValidateReceipt(product, unit, TenantId, receivedQuantity, unitCost, presentationConfirmed, notes, receipts);
        // Human-approved cost per selected local presentation; never inferred from UBL net/gross prices or tax totals.
        BusinessProductId = product.Id; ProductUnitId = unit.Id; ReceivedQuantity = receivedQuantity; ApprovedUnitCost = unitCost;
        ApprovedConversionToBase = unit.ConversionToBase; ApprovedUnitName = unit.Name; ReviewNotes = notes;
        ReceiptPlanJson = SupplierUblRules.Json(receipts); ReviewStatus = "ready";
    }
    public void RequireCurrentMapping(BusinessProduct product, ProductUnit unit)
    {
        SupplierUblRules.Require(ReviewStatus == "ready" && BusinessProductId == product.Id && ProductUnitId == unit.Id &&
            ApprovedUnitName == unit.Name && ApprovedConversionToBase == unit.ConversionToBase, "mapping_changed");
        SupplierUblRules.ValidateReceipt(product, unit, TenantId, ReceivedQuantity!.Value, ApprovedUnitCost!.Value, true, ReviewNotes!, Receipts);
    }
}

public sealed class SupplierUblPurchaseLineLink
{
    private SupplierUblPurchaseLineLink() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ImportId { get; private set; }
    public Guid ImportLineId { get; private set; }
    public Guid PurchaseId { get; private set; }
    public Guid PurchaseLineId { get; private set; }
    public static SupplierUblPurchaseLineLink Create(SupplierUblImport import, SupplierUblImportLine source, PurchaseLine received)
    {
        SupplierUblRules.Require(import.Status == "converted_to_draft" && import.TenantId == source.TenantId && import.TenantId == received.TenantId &&
            source.ImportId == import.Id && received.PurchaseId == import.RelatedPurchaseId && source.BusinessProductId == received.BusinessProductId, "draft_mismatch");
        return new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = import.TenantId,
            ImportId = import.Id,
            ImportLineId = source.Id,
            PurchaseId = received.PurchaseId,
            PurchaseLineId = received.Id
        };
    }
}
