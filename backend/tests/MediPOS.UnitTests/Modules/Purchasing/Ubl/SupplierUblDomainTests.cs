using System.Text;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Purchasing;
using MediPOS.Domain.Modules.Purchasing.Ubl;
using MediPOS.Infrastructure.Modules.Purchasing.Ubl;
using MediPOS.TestFixtures;

namespace MediPOS.UnitTests.Modules.Purchasing.Ubl;

public sealed class SupplierUblDomainTests
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 12, 16, 0, 0, TimeSpan.Zero);
    private static async Task<(SupplierUblImport Import, SupplierUblImportLine Line, BusinessProduct Product, ProductUnit Unit, Supplier Supplier)> Data(bool medicine = true, string currency = "PEN")
    {
        var tenant = Guid.NewGuid(); using var stream = new MemoryStream(Encoding.UTF8.GetBytes(SupplierUblSyntheticXml.Create(currency: currency)));
        var parsed = await new SupplierUblInvoiceParser().ParseAsync(stream, "fixture.xml", TestContext.Current.CancellationToken);
        var supplier = Supplier.Create(tenant, "Distribuidora", SupplierUblSyntheticXml.SupplierRuc, null, Now);
        var import = SupplierUblImport.Create(tenant, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), parsed.Invoice, "fixture.xml", parsed.Checksum, parsed.Xml, supplier.Id, Now);
        var product = BusinessProduct.CreateLocal(tenant, "SKU-1", medicine ? ProductType.Medicine : ProductType.Retail, "Producto", Guid.NewGuid(), "Laboratorio", null,
            medicine ? MedicineData.Create([MedicineComponent.Create("Ingrediente sintético", "500 mg")], "Tableta") : null, 10m, 8m, Now);
        return (import, SupplierUblImportLine.Create(import, parsed.Invoice.Lines[0]), product, ProductUnit.Create(tenant, product.Id, tenant, "Caja", 100m, false), supplier);
    }
    [Fact]
    public async Task ApprovedReceiptCostAndPresentationAreExplicitAndOriginalEvidenceNeverChanges()
    {
        var d = await Data(); var evidence = d.Line.EvidenceJson; var bytes = d.Import.SourceXml.ToArray(); var checksum = d.Import.XmlChecksumSha256;
        d.Line.ApproveMapping(d.Import, d.Product, d.Unit, 3m, 2.125m, true, "Received three local boxes; cost explicitly approved including reviewed tax treatment.",
            [new(1m, "A", new(2030, 1, 1)), new(2m, "B", new(2031, 1, 1))]);
        Assert.Equal(2m, d.Line.DocumentaryQuantity); Assert.Equal(3m, d.Line.ReceivedQuantity); Assert.Equal(10m, d.Line.UnitPrice); Assert.Equal(2.125m, d.Line.ApprovedUnitCost);
        Assert.Equal(100m, d.Line.ApprovedConversionToBase); Assert.Equal(evidence, d.Line.EvidenceJson); Assert.Equal(bytes, d.Import.SourceXml); Assert.Equal(checksum, d.Import.XmlChecksumSha256);
        d.Import.Review(1, d.Supplier, Guid.NewGuid(), "Human reviewed supplier and all lines.", Now, [d.Line], true, false);
        var purchase = Purchase.Create(d.Import.TenantId, d.Import.BranchId, d.Supplier.Id, "F001-1", Guid.NewGuid(), Now);
        var receipts = d.Line.Receipts.Select(v => PurchaseLine.Create(purchase, d.Product, d.Unit, v.Quantity, d.Line.ApprovedUnitCost!.Value, v.BatchNumber, v.ExpirationDate)).ToArray();
        d.Import.Convert(d.Import.Version, purchase, [d.Line]);
        Assert.Equal(PurchaseStatus.Draft, purchase.Status); Assert.Null(purchase.ConfirmedAt); Assert.Equal(2, receipts.Length); Assert.Equal(300m, receipts.Sum(v => v.BaseQuantity));
        Assert.Equal(d.Import.RelatedPurchaseId, purchase.Id); Assert.Equal("converted_to_draft", d.Import.Status);
        Assert.All(receipts, v => Assert.Equal(2.125m, v.UnitCost)); Assert.Equal(10m, d.Product.RetailPrice); Assert.Equal(8m, d.Product.WholesalePrice);
        Assert.Throws<InvalidOperationException>(() => d.Line.ClearMapping(d.Import, "Cannot rewrite converted provenance."));
    }
    [Theory]
    [InlineData("batch", "ubl.medicine_receipt_required")]
    [InlineData("expiration", "ubl.medicine_receipt_required")]
    [InlineData("quantity", "ubl.receipt_quantity_mismatch")]
    [InlineData("presentation", "ubl.review_required")]
    [InlineData("foreign_product", "ubl.mapping_invalid")]
    [InlineData("foreign_unit", "ubl.mapping_invalid")]
    [InlineData("wrong_product_unit", "ubl.mapping_invalid")]
    [InlineData("cost", "ubl.receipt_invalid")]
    public async Task UnsafeMappingsAndIncompleteMedicineReceiptsAreRejected(string scenario, string code)
    {
        var d = await Data(); var product = d.Product; var unit = d.Unit;
        if (scenario == "foreign_product") product = BusinessProduct.CreateLocal(Guid.NewGuid(), "X", ProductType.Retail, "X", Guid.NewGuid(), "X", null, null, 0, null, Now);
        if (scenario == "foreign_unit") { var t = Guid.NewGuid(); unit = ProductUnit.Create(t, Guid.NewGuid(), t, "Caja", 1, false); }
        if (scenario == "wrong_product_unit") unit = ProductUnit.Create(d.Import.TenantId, Guid.NewGuid(), d.Import.TenantId, "Caja", 1, false);
        var receipts = new SupplierUblReceipt[] { new(scenario == "quantity" ? 1m : 2m, scenario == "batch" ? null : "LOT", scenario == "expiration" ? null : new DateOnly(2030, 1, 1)) };
        Assert.Equal(code, Assert.Throws<InvalidOperationException>(() => d.Line.ApproveMapping(d.Import, product, unit, 2m, scenario == "cost" ? -1m : 10m, scenario != "presentation", "Reviewed explicitly.", receipts)).Message);
        Assert.Equal("requires_review", d.Line.ReviewStatus);
    }
    [Fact]
    public async Task UnmatchedLinesAndUnsupportedCurrencyCannotCompleteReview()
    {
        var d = await Data(); Assert.Equal("ubl.lines_require_review", Assert.Throws<InvalidOperationException>(() => d.Import.Review(1, d.Supplier, Guid.NewGuid(), "Complete", Now, [d.Line], true, false)).Message);
        var usd = await Data(currency: "USD"); Assert.Contains("currency_unsupported", usd.Import.BlockingIssuesJson, StringComparison.Ordinal);
        Assert.Equal("ubl.currency_unsupported", Assert.Throws<InvalidOperationException>(() => usd.Import.RequireReady([usd.Line])).Message);
    }
    [Fact]
    public async Task ExactFileRetriesDoNotOverwriteEvidenceAndChangedChecksumIsConflict()
    {
        var d = await Data(); d.Import.RequireSameFile(d.Import.XmlChecksumSha256);
        Assert.Equal("ubl.document_content_conflict", Assert.Throws<InvalidOperationException>(() => d.Import.RequireSameFile(new string('f', 64))).Message);
        Assert.Equal(1, d.Import.Version); Assert.Null(d.Import.RelatedPurchaseId);
    }
    [Fact]
    public async Task SupplierIdentificationNeverUsesOnlyNameAndRejectionPreservesUnmatchedDocument()
    {
        var d = await Data(); var wrong = Supplier.Create(d.Import.TenantId, d.Supplier.Name, "20199999999", null, Now);
        Assert.Equal("ubl.supplier_mismatch", Assert.Throws<InvalidOperationException>(() => d.Import.Review(1, wrong, Guid.NewGuid(), "Same name is insufficient", Now, [d.Line], false, false)).Message);
        var original = d.Import.EvidenceJson; d.Import.Review(1, null, Guid.NewGuid(), "Reject unmatched invoice.", Now, [d.Line], false, true);
        Assert.Equal("rejected", d.Import.Status); Assert.Equal(original, d.Import.EvidenceJson); Assert.Null(d.Import.RelatedPurchaseId);
        Assert.Throws<InvalidOperationException>(() => d.Import.Review(2, d.Supplier, Guid.NewGuid(), "Reopen", Now, [d.Line], false, false));
    }
    [Fact]
    public void ReceiverMustBelongToTenantAndHaveTheSameLegalRuc()
    {
        var tenant = Guid.NewGuid(); SupplierUblRules.ValidateBuyer(tenant, tenant, tenant, SupplierUblSyntheticXml.BuyerRuc, SupplierUblSyntheticXml.BuyerRuc);
        Assert.Equal("ubl.buyer_mismatch", Assert.Throws<InvalidOperationException>(() => SupplierUblRules.ValidateBuyer(tenant, tenant, tenant, SupplierUblSyntheticXml.BuyerRuc, "20100000009")).Message);
        Assert.Throws<InvalidOperationException>(() => SupplierUblRules.ValidateBuyer(tenant, Guid.NewGuid(), tenant, SupplierUblSyntheticXml.BuyerRuc, SupplierUblSyntheticXml.BuyerRuc));
        Assert.Throws<InvalidOperationException>(() => SupplierUblRules.ValidateBuyer(tenant, tenant, Guid.NewGuid(), SupplierUblSyntheticXml.BuyerRuc, SupplierUblSyntheticXml.BuyerRuc));
    }
    [Fact]
    public async Task RetailMayOmitMedicineLotsAndApprovalVersionDetectsStaleReview()
    {
        var d = await Data(false); d.Line.ApproveMapping(d.Import, d.Product, d.Unit, 2m, 0m, true, "Received explicitly; zero cost approved.", [new(2m, null, null)]);
        d.Import.Review(1, d.Supplier, Guid.NewGuid(), "Approved retail", Now, [d.Line], true, false);
        Assert.Equal("ubl.stale_review", Assert.Throws<InvalidOperationException>(() => d.Import.RequireVersion(1)).Message);
        d.Line.ClearMapping(d.Import, "Incorrect mapping rejected."); Assert.Equal("requires_review", d.Line.ReviewStatus);
        Assert.Null(d.Line.ApprovedUnitCost); Assert.Equal(2m, d.Line.DocumentaryQuantity);
    }
}
