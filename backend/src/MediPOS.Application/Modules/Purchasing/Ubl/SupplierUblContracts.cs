using MediPOS.Application.Errors;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Purchasing;
using MediPOS.Domain.Modules.Purchasing.Ubl;

namespace MediPOS.Application.Modules.Purchasing.Ubl;

// Tenant/branch are selections. All actor identity and permissions come from ResolveAccessContextHandler.
public sealed record ImportSupplierUblInvoiceCommand(Guid TenantId, Guid BranchId, Stream InputStream, string OriginalFileName);
public sealed record ReviewSupplierUblLineInput(Guid LineId, bool Ready, Guid? BusinessProductId, Guid? ProductUnitId,
    decimal? ReceivedQuantity, decimal? UnitCost, bool PresentationConfirmed, string Notes, IReadOnlyList<SupplierUblReceipt> Receipts);
public sealed record ReviewSupplierUblImportCommand(Guid TenantId, Guid BranchId, Guid ImportId, long ExpectedVersion,
    Guid? SupplierId, string Action, string Notes, IReadOnlyList<ReviewSupplierUblLineInput> Lines);
public sealed record ConvertSupplierUblImportToPurchaseDraftCommand(Guid TenantId, Guid BranchId, Guid ImportId, long ExpectedVersion);
public sealed record GetSupplierUblImportCommand(Guid TenantId, Guid BranchId, Guid ImportId);
public sealed record GetSupplierUblImportLinesCommand(Guid TenantId, Guid BranchId, Guid ImportId, int Offset = 0, int Limit = 100);
public sealed record SearchSupplierUblImportsCommand(Guid TenantId, Guid? BranchId = null, Guid? SupplierId = null,
    DateOnly? From = null, DateOnly? To = null, string? Status = null, string? Document = null, int Offset = 0, int Limit = 100);
public sealed record SupplierUblImportDetails(Guid Id, Guid BranchId, Guid? SupplierId, bool SupplierApproved,
    string SourceFileName, string XmlChecksumSha256, string DocumentType, string SupplierRuc, string BuyerRuc,
    string Series, string Correlative, string OriginalDocumentId, DateOnly IssueDate, string CurrencyCode,
    decimal DeclaredTotal, string Status, long Version, DateTimeOffset ImportedAtUtc, Guid ImportedByMembershipId,
    DateTimeOffset? ReviewCompletedAtUtc, Guid? RelatedPurchaseId, string BlockingIssuesJson, string? ReviewNotes, string EvidenceJson)
{
    public static SupplierUblImportDetails From(SupplierUblImport value) => new(value.Id, value.BranchId, value.SupplierId,
        value.SupplierApproved, value.SourceFileName, value.XmlChecksumSha256, value.DocumentType, value.SupplierRuc,
        value.BuyerRuc, value.Series, value.Correlative, value.OriginalDocumentId, value.IssueDate, value.CurrencyCode,
        value.DeclaredTotal, value.Status, value.Version, value.ImportedAtUtc, value.ImportedByMembershipId,
        value.ReviewCompletedAtUtc, value.RelatedPurchaseId, value.BlockingIssuesJson, value.ReviewNotes, value.EvidenceJson);
}
public sealed record SupplierUblImportLineDetails(Guid Id, int LineNumber, string Description, string? SupplierProductCode,
    string? BuyerProductCode, string? StandardProductCode, string? StandardCodeScheme, decimal DocumentaryQuantity,
    string DocumentaryUnit, decimal UnitPrice, decimal Tax, decimal LineAmount, string EvidenceJson,
    string SuggestionStatus, Guid? SuggestedBusinessProductId, Guid? SuggestedProductUnitId,
    Guid? BusinessProductId, Guid? ProductUnitId, string ReviewStatus, string? ReviewNotes,
    decimal? ReceivedQuantity, decimal? ApprovedUnitCost, string ReceiptPlanJson)
{
    public static SupplierUblImportLineDetails From(SupplierUblImportLine v) => new(v.Id, v.LineNumber, v.Description,
        v.SupplierProductCode, v.BuyerProductCode, v.StandardProductCode, v.StandardCodeScheme, v.DocumentaryQuantity,
        v.DocumentaryUnit, v.UnitPrice, v.Tax, v.LineAmount, v.EvidenceJson, v.SuggestionStatus, v.SuggestedBusinessProductId,
        v.SuggestedProductUnitId, v.BusinessProductId, v.ProductUnitId, v.ReviewStatus, v.ReviewNotes,
        v.ReceivedQuantity, v.ApprovedUnitCost, v.ReceiptPlanJson);
}
public sealed record SupplierUblImportPage(IReadOnlyList<SupplierUblImportDetails> Imports, bool HasMore);
public sealed record SupplierUblLinePage(IReadOnlyList<SupplierUblImportLineDetails> Lines, bool HasMore);
public sealed record ParsedSupplierUblInvoice(SupplierUblInvoiceEvidence Invoice, byte[] Xml, string Checksum);
public interface ISupplierUblInvoiceParser
{ Task<ParsedSupplierUblInvoice> ParseAsync(Stream input, string fileName, CancellationToken cancellationToken); }
public sealed record SupplierUblCatalog(IReadOnlyDictionary<Guid, BusinessProduct> Products, IReadOnlyDictionary<Guid, ProductUnit> Units);
public sealed record SupplierUblSuggestions(Guid? SupplierId, IReadOnlyDictionary<int, (string Status, Guid? ProductId, Guid? UnitId)> Lines);

public interface ISupplierUblImportStore
{
    Task<SupplierUblImport?> FindIdentityAsync(Guid tenantId, SupplierUblInvoiceEvidence invoice, CancellationToken token);
    Task<SupplierUblImport?> LockAsync(Guid tenantId, Guid branchId, Guid id, CancellationToken token);
    Task<SupplierUblSuggestions> SuggestAsync(Guid tenantId, SupplierUblInvoiceEvidence invoice, CancellationToken token);
    Task<SupplierUblCatalog> CatalogAsync(Guid tenantId, IReadOnlyList<Guid> products, IReadOnlyList<Guid> units, CancellationToken token);
    Task<IReadOnlyList<SupplierUblImportLine>> LinesAsync(Guid tenantId, Guid importId, CancellationToken token);
    Task SaveImportAsync(SupplierUblImport import, IReadOnlyList<SupplierUblImportLine> lines, Guid actorId, CancellationToken token);
    Task SaveReviewAsync(SupplierUblImport import, string before, Guid actorId, CancellationToken token);
    Task SaveConversionAsync(SupplierUblImport import, Purchase purchase, IReadOnlyList<PurchaseLine> lines,
        IReadOnlyList<SupplierUblPurchaseLineLink> links, Guid actorId, CancellationToken token);
    Task RecordRejectedFileAsync(Guid tenantId, Guid branchId, Guid actorId, string code, CancellationToken token);
    Task<SupplierUblImportDetails?> GetAsync(GetSupplierUblImportCommand command, CancellationToken token);
    Task<SupplierUblLinePage> PageLinesAsync(GetSupplierUblImportLinesCommand command, CancellationToken token);
    Task<SupplierUblImportPage> SearchAsync(SearchSupplierUblImportsCommand command, CancellationToken token);
}
public static class SupplierUblErrors
{
    public static ApplicationErrorException Error(string code, ErrorCategory category = ErrorCategory.Validation) =>
        new(new(code.StartsWith("ubl.", StringComparison.Ordinal) ? code : "ubl." + code, category, "Supplier invoice operation could not be completed."));
    internal static ApplicationErrorException Domain(Exception error) => Error(error.Message.StartsWith("ubl.", StringComparison.Ordinal) ? error.Message : "review_invalid", ErrorCategory.Conflict);
}
