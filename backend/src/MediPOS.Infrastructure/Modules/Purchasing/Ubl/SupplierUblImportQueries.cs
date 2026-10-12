using System.Linq.Expressions;
using MediPOS.Application.Modules.Purchasing.Ubl;
using MediPOS.Domain.Modules.Purchasing.Ubl;

namespace MediPOS.Infrastructure.Modules.Purchasing.Ubl;

public static class SupplierUblImportQueries
{
    public static readonly Expression<Func<SupplierUblImport, SupplierUblImportDetails>> Details = v => new(v.Id, v.BranchId, v.SupplierId,
        v.SupplierApproved, v.SourceFileName, v.XmlChecksumSha256, v.DocumentType, v.SupplierRuc, v.BuyerRuc, v.Series,
        v.Correlative, v.OriginalDocumentId, v.IssueDate, v.CurrencyCode, v.DeclaredTotal, v.Status, v.Version,
        v.ImportedAtUtc, v.ImportedByMembershipId, v.ReviewCompletedAtUtc, v.RelatedPurchaseId, v.BlockingIssuesJson, v.ReviewNotes, v.EvidenceJson);
    public static IQueryable<SupplierUblImportDetails> Search(IQueryable<SupplierUblImport> query, SearchSupplierUblImportsCommand c)
    {
        query = query.Where(v => v.TenantId == c.TenantId);
        if (c.BranchId.HasValue) query = query.Where(v => v.BranchId == c.BranchId.Value);
        if (c.SupplierId.HasValue) query = query.Where(v => v.SupplierId == c.SupplierId.Value);
        if (c.From.HasValue) query = query.Where(v => v.IssueDate >= c.From.Value);
        if (c.To.HasValue) query = query.Where(v => v.IssueDate <= c.To.Value);
        if (c.Status is not null) query = query.Where(v => v.Status == c.Status);
        if (c.Document is not null) query = query.Where(v => v.OriginalDocumentId == c.Document || v.Series + "-" + v.Correlative == c.Document);
        return query.OrderByDescending(v => v.IssueDate).ThenBy(v => v.Id).Skip(c.Offset).Take(c.Limit + 1).Select(Details);
    }
}
