using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.Purchasing.Ubl;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Purchasing;
using MediPOS.Domain.Modules.Purchasing.Ubl;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.Purchasing.Ubl;

internal sealed class SupplierUblImportStore(MediPosDbContext context, TimeProvider clock) : ISupplierUblImportStore
{
    private void Transaction(Guid tenant)
    { context.SelectTenant(tenant); if (context.Database.CurrentTransaction is null) throw new InvalidOperationException("UBL writes require a license transaction."); }
    public Task<SupplierUblImport?> FindIdentityAsync(Guid tenantId, SupplierUblInvoiceEvidence invoice, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        return context.SupplierUblImports.AsNoTracking().SingleOrDefaultAsync(v => v.SupplierRuc == invoice.SupplierRuc && v.DocumentType == invoice.DocumentType && v.Series == invoice.Series && v.Correlative == invoice.Correlative, token);
    }
    public async Task<SupplierUblImport?> LockAsync(Guid tenantId, Guid branchId, Guid id, CancellationToken token)
    {
        Transaction(tenantId);
        var value = await context.SupplierUblImports.FromSqlInterpolated($"SELECT * FROM supplier_ubl_imports WHERE tenant_id={tenantId} AND branch_id={branchId} AND id={id} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(token).ConfigureAwait(false);
        if (value is null) return null;
        foreach (var entry in context.ChangeTracker.Entries<SupplierUblImport>().Where(e => e.Entity.Id == id).ToArray()) entry.State = EntityState.Detached;
        context.Attach(value); return value;
    }
    public async Task<IReadOnlyList<SupplierUblImportLine>> LinesAsync(Guid tenantId, Guid importId, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        foreach (var entry in context.ChangeTracker.Entries<SupplierUblImportLine>().Where(e => e.Entity.ImportId == importId).ToArray()) entry.State = EntityState.Detached;
        return await context.SupplierUblImportLines.Where(v => v.ImportId == importId).OrderBy(v => v.LineNumber).Take(1001).ToArrayAsync(token).ConfigureAwait(false);
    }
    public async Task<SupplierUblCatalog> CatalogAsync(Guid tenantId, IReadOnlyList<Guid> products, IReadOnlyList<Guid> units, CancellationToken token)
    {
        context.SelectTenant(tenantId); var productIds = products.Distinct().ToArray(); var unitIds = units.Distinct().ToArray();
        var productRows = await context.BusinessProducts.AsNoTracking().Where(v => productIds.Contains(v.Id)).ToArrayAsync(token).ConfigureAwait(false);
        var unitRows = await context.ProductUnits.AsNoTracking().Where(v => unitIds.Contains(v.Id)).ToArrayAsync(token).ConfigureAwait(false);
        return new(productRows.ToDictionary(v => v.Id), unitRows.ToDictionary(v => v.Id));
    }
    public async Task<SupplierUblSuggestions> SuggestAsync(Guid tenantId, SupplierUblInvoiceEvidence invoice, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        var suppliers = await context.Suppliers.AsNoTracking().Where(v => v.IsActive && v.Ruc == invoice.SupplierRuc).Select(v => v.Id).Take(2).ToArrayAsync(token).ConfigureAwait(false);
        var supplierId = suppliers.Length == 1 ? suppliers[0] : (Guid?)null;
        var sellerCodes = invoice.Lines.Where(v => v.SupplierCode is not null).Select(v => v.SupplierCode!).Distinct().ToArray();
        var buyerCodes = invoice.Lines.Where(v => v.BuyerCode is not null).Select(v => v.BuyerCode!).Distinct().ToArray();
        var barcodes = invoice.Lines.Where(v => v.StandardCodeScheme is "GTIN" or "EAN" && v.StandardCode is not null).Select(v => v.StandardCode!).Distinct().ToArray();
        var prior = await (from line in context.SupplierUblImportLines.AsNoTracking()
                           join import in context.SupplierUblImports.AsNoTracking() on line.ImportId equals import.Id
                           join product in context.BusinessProducts.AsNoTracking() on line.BusinessProductId equals product.Id
                           join unit in context.ProductUnits.AsNoTracking() on line.ProductUnitId equals unit.Id
                           where supplierId.HasValue && import.SupplierId == supplierId && import.SupplierApproved && import.Status != "rejected" &&
                               line.ReviewStatus == "ready" && product.IsActive && unit.IsActive && unit.ConversionToBase == line.ApprovedConversionToBase &&
                               unit.Name == line.ApprovedUnitName && sellerCodes.Contains(line.SupplierProductCode!)
                           select new { line.SupplierProductCode, line.DocumentaryUnit, Product = product.Id, Unit = unit.Id }).Distinct().Take(10001).ToArrayAsync(token).ConfigureAwait(false);
        var catalog = await context.BusinessProducts.AsNoTracking().Where(v => v.IsActive && (buyerCodes.Contains(v.InternalCode) || barcodes.Contains(v.Barcode!)))
            .Select(v => new { v.Id, v.InternalCode, v.Barcode }).Take(10001).ToArrayAsync(token).ConfigureAwait(false);
        var result = new Dictionary<int, (string, Guid?, Guid?)>();
        foreach (var line in invoice.Lines)
        {
            if (prior.Length > 10000 || catalog.Length > 10000) { result[line.Number] = ("ambiguous", null, null); continue; }
            var previous = prior.Where(v => v.SupplierProductCode == line.SupplierCode && v.DocumentaryUnit == line.UnitCode).ToArray();
            if (previous.Length > 0)
            { result[line.Number] = previous.Length == 1 ? ("previous_mapping", previous[0].Product, previous[0].Unit) : ("ambiguous", null, null); continue; }
            var byCode = catalog.Where(v => line.BuyerCode is not null && v.InternalCode == line.BuyerCode).ToArray();
            var byBarcode = catalog.Where(v => line.StandardCodeScheme is "GTIN" or "EAN" && line.StandardCode is not null && v.Barcode == line.StandardCode).ToArray();
            var found = byCode.Concat(byBarcode).Select(v => v.Id).Distinct().ToArray();
            result[line.Number] = found.Length == 1 ? (byCode.Length > 0 ? "buyer_code" : "barcode", found[0], null) : (found.Length > 1 ? "ambiguous" : "unmatched", null, null);
        }
        return new(supplierId, result);
    }
    public async Task SaveImportAsync(SupplierUblImport import, IReadOnlyList<SupplierUblImportLine> lines, Guid actorId, CancellationToken token)
    {
        Transaction(import.TenantId); context.SupplierUblImports.Add(import); context.SupplierUblImportLines.AddRange(lines);
        context.AddAudit(AuditTrail.Record(import.TenantId, actorId, AuditAction.SupplierUblImported, import.Id, import.ImportedAtUtc, null,
            SupplierUblRules.Json(new { version = import.Version, status = import.Status, lineCount = lines.Count })), import.TenantId, AuditAction.SupplierUblImported, import.Id);
        await context.SaveAuditedChangesAsync(token).ConfigureAwait(false);
    }
    public async Task SaveReviewAsync(SupplierUblImport import, string before, Guid actorId, CancellationToken token)
    {
        Transaction(import.TenantId); var action = import.Status == "rejected" ? AuditAction.SupplierUblRejected : AuditAction.SupplierUblReviewed;
        var lines = context.ChangeTracker.Entries<SupplierUblImportLine>().Where(v => v.Entity.ImportId == import.Id).Select(v => v.Entity).ToArray();
        var after = SupplierUblRules.Json(new { version = import.Version, status = import.Status, import.SupplierId, lines = lines.Select(v => new { v.Id, v.BusinessProductId, v.ProductUnitId, v.ReviewStatus, v.ReviewNotes, v.ReceiptPlanJson, v.ApprovedUnitCost, v.ReceivedQuantity }).ToArray() });
        context.AddAudit(AuditTrail.Record(import.TenantId, actorId, action, import.Id, import.ReviewCompletedAtUtc ?? clock.GetUtcNow(), before, after), import.TenantId, action, import.Id);
        await context.SaveAuditedChangesAsync(token).ConfigureAwait(false);
    }
    public async Task SaveConversionAsync(SupplierUblImport import, Purchase purchase, IReadOnlyList<PurchaseLine> lines,
        IReadOnlyList<SupplierUblPurchaseLineLink> links, Guid actorId, CancellationToken token)
    {
        Transaction(import.TenantId); context.Purchases.Add(purchase); context.PurchaseLines.AddRange(lines); context.SupplierUblPurchaseLineLinks.AddRange(links);
        context.AddAudit(AuditTrail.Record(import.TenantId, actorId, AuditAction.SupplierUblConverted, import.Id, clock.GetUtcNow(),
            SupplierUblRules.Json(new { version = import.Version - 1, status = "reviewed" }),
            SupplierUblRules.Json(new { version = import.Version, status = import.Status, purchaseId = purchase.Id, lineCount = lines.Count })), import.TenantId, AuditAction.SupplierUblConverted, import.Id);
        await context.SaveAuditedChangesAsync(token).ConfigureAwait(false);
    }
    public async Task RecordRejectedFileAsync(Guid tenantId, Guid branchId, Guid actorId, string code, CancellationToken token)
    {
        Transaction(tenantId); var target = Guid.CreateVersion7();
        context.AddAudit(AuditTrail.Record(tenantId, actorId, AuditAction.SupplierUblFileRejected, target, clock.GetUtcNow(), null,
            SupplierUblRules.Json(new { branchId, code })), tenantId, AuditAction.SupplierUblFileRejected, target);
        await context.SaveAuditedChangesAsync(token).ConfigureAwait(false);
    }
    public Task<SupplierUblImportDetails?> GetAsync(GetSupplierUblImportCommand command, CancellationToken token)
    {
        context.SelectTenant(command.TenantId);
        return context.SupplierUblImports.AsNoTracking().Where(v => v.Id == command.ImportId && v.BranchId == command.BranchId).Select(SupplierUblImportQueries.Details).SingleOrDefaultAsync(token);
    }
    public async Task<SupplierUblLinePage> PageLinesAsync(GetSupplierUblImportLinesCommand command, CancellationToken token)
    {
        context.SelectTenant(command.TenantId);
        var rows = await context.SupplierUblImportLines.AsNoTracking().Where(v => v.ImportId == command.ImportId).OrderBy(v => v.LineNumber)
            .Skip(command.Offset).Take(command.Limit + 1).ToArrayAsync(token).ConfigureAwait(false);
        return new(rows.Take(command.Limit).Select(SupplierUblImportLineDetails.From).ToArray(), rows.Length > command.Limit);
    }
    public async Task<SupplierUblImportPage> SearchAsync(SearchSupplierUblImportsCommand command, CancellationToken token)
    {
        context.SelectTenant(command.TenantId);
        var rows = await SupplierUblImportQueries.Search(context.SupplierUblImports.AsNoTracking(), command).ToArrayAsync(token).ConfigureAwait(false);
        return new(rows.Take(command.Limit).ToArray(), rows.Length > command.Limit);
    }
}
