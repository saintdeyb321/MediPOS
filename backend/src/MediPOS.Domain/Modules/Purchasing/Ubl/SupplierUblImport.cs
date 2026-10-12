using MediPOS.Domain.Modules.Catalog;

namespace MediPOS.Domain.Modules.Purchasing.Ubl;

public sealed class SupplierUblImport
{
    private SupplierUblImport() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid LegalEntityId { get; private set; }
    public Guid? SupplierId { get; private set; }
    public bool SupplierApproved { get; private set; }
    public string SourceFileName { get; private set; } = "";
    public string XmlChecksumSha256 { get; private set; } = "";
    public byte[] SourceXml { get; private set; } = [];
    public string DocumentType { get; private set; } = "";
    public string SupplierRuc { get; private set; } = "";
    public string BuyerRuc { get; private set; } = "";
    public string Series { get; private set; } = "";
    public string Correlative { get; private set; } = "";
    public string OriginalDocumentId { get; private set; } = "";
    public DateOnly IssueDate { get; private set; }
    public string CurrencyCode { get; private set; } = "";
    public decimal DeclaredTotal { get; private set; }
    public int OriginalLineCount { get; private set; }
    public string EvidenceJson { get; private set; } = "";
    public string BlockingIssuesJson { get; private set; } = "[]";
    public string Status { get; private set; } = "requires_review";
    public long Version { get; private set; }
    public DateTimeOffset ImportedAtUtc { get; private set; }
    public Guid ImportedByMembershipId { get; private set; }
    public DateTimeOffset? ReviewCompletedAtUtc { get; private set; }
    public Guid? ReviewedByMembershipId { get; private set; }
    public Guid? RelatedPurchaseId { get; private set; }
    public string? ReviewNotes { get; private set; }
    public static SupplierUblImport Create(Guid tenantId, Guid branchId, Guid legalEntityId, Guid membershipId,
        SupplierUblInvoiceEvidence invoice, string fileName, string checksum, byte[] xml, Guid? matchedSupplier, DateTimeOffset now)
    {
        SupplierUblRules.Require(tenantId != Guid.Empty && branchId != Guid.Empty && legalEntityId != Guid.Empty && membershipId != Guid.Empty && now.Offset == TimeSpan.Zero, "scope_invalid");
        SupplierUblRules.Require(xml.Length is > 0 and <= 5242880 && checksum.Length == 64 && checksum.All(char.IsAsciiHexDigitLower), "file_invalid");
        SupplierUblRules.Require(!string.IsNullOrWhiteSpace(fileName) && fileName.Length <= 256 && !fileName.Any(value => char.IsControl(value) || value is '/' or '\\' or ':'), "file_name_invalid");
        SupplierUblRules.Require(SupplierUblRules.IsRuc(invoice.SupplierRuc) && SupplierUblRules.IsRuc(invoice.BuyerRuc) && invoice.Lines.Count is > 0 and <= SupplierUblRules.MaximumLines, "document_invalid");
        return new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            BranchId = branchId,
            LegalEntityId = legalEntityId,
            ImportedByMembershipId = membershipId,
            ImportedAtUtc = now,
            Version = 1,
            SupplierId = matchedSupplier,
            SourceFileName = fileName,
            XmlChecksumSha256 = checksum,
            SourceXml = xml.ToArray(),
            DocumentType = invoice.DocumentType,
            SupplierRuc = invoice.SupplierRuc,
            BuyerRuc = invoice.BuyerRuc,
            Series = invoice.Series,
            Correlative = invoice.Correlative,
            OriginalDocumentId = invoice.OriginalDocumentId,
            IssueDate = invoice.IssueDate,
            CurrencyCode = invoice.Currency,
            DeclaredTotal = invoice.Total,
            OriginalLineCount = invoice.Lines.Count,
            EvidenceJson = SupplierUblRules.Json(invoice with { Lines = [] }),
            BlockingIssuesJson = SupplierUblRules.Json(invoice.BlockingIssues)
        };
    }
    public void RequireVersion(long version) => SupplierUblRules.Require(Version == version, "stale_review");
    public void RequireEditable() => SupplierUblRules.Require(Status is not ("converted_to_draft" or "rejected"), "terminal_import");
    public void RequireSameFile(string checksum) => SupplierUblRules.Require(XmlChecksumSha256 == checksum, "document_content_conflict");
    public void Review(long expected, Supplier? supplier, Guid membership, string notes, DateTimeOffset now,
        IReadOnlyList<SupplierUblImportLine> lines, bool complete, bool reject)
    {
        RequireVersion(expected); RequireEditable();
        SupplierUblRules.Require(reject || supplier is not null, "supplier_required");
        if (supplier is not null) SupplierUblRules.Require(supplier.TenantId == TenantId && supplier.IsActive && supplier.Ruc == SupplierRuc, "supplier_mismatch");
        SupplierUblRules.Require(membership != Guid.Empty && !string.IsNullOrWhiteSpace(notes) && notes.Length <= 2048 && !notes.Any(char.IsControl) && now >= ImportedAtUtc && now.Offset == TimeSpan.Zero, "review_invalid");
        if (complete) RequireReady(lines);
        SupplierId = supplier?.Id; SupplierApproved = supplier is not null; ReviewedByMembershipId = membership; ReviewNotes = notes;
        ReviewCompletedAtUtc = complete ? now : null; Status = reject ? "rejected" : complete ? "reviewed" : "requires_review";
        Version = checked(Version + 1);
    }
    public void RequireReady(IReadOnlyList<SupplierUblImportLine> lines)
    {
        SupplierUblRules.Require(CurrencyCode == "PEN", "currency_unsupported");
        SupplierUblRules.Require(BlockingIssuesJson == "[]", "document_incoherent");
        SupplierUblRules.Require(lines.Count == OriginalLineCount && lines.Count is > 0 and <= SupplierUblRules.MaximumLines && lines.All(value => value.TenantId == TenantId && value.ImportId == Id && value.ReviewStatus == "ready"), "lines_require_review");
    }
    public void Convert(long expected, Purchase purchase, IReadOnlyList<SupplierUblImportLine> lines)
    {
        RequireVersion(expected); SupplierUblRules.Require(Status == "reviewed" && SupplierApproved, "review_required"); RequireReady(lines);
        SupplierUblRules.Require(purchase.TenantId == TenantId && purchase.BranchId == BranchId && purchase.SupplierId == SupplierId && purchase.Status == PurchaseStatus.Draft, "draft_mismatch");
        RelatedPurchaseId = purchase.Id; Status = "converted_to_draft"; Version = checked(Version + 1);
    }
}
