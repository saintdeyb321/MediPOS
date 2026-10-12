using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MediPOS.Application.Errors;
using MediPOS.Infrastructure.Modules.Purchasing.Ubl;
using MediPOS.TestFixtures;

namespace MediPOS.UnitTests.Modules.Purchasing.Ubl;

public sealed class SupplierUblParserTests
{
    private static async Task<MediPOS.Application.Modules.Purchasing.Ubl.ParsedSupplierUblInvoice> Parse(string xml, string filename = "synthetic.dat")
    { using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml)); return await new SupplierUblInvoiceParser().ParseAsync(stream, filename, TestContext.Current.CancellationToken); }
    [Fact]
    public async Task ValidPeruvianInvoicePreservesEvidenceUnicodeAndExactChecksum()
    {
        var xml = SupplierUblSyntheticXml.Create(document: "F001-00001"); var parsed = await Parse(xml);
        Assert.Equal("1", parsed.Invoice.Correlative); Assert.Equal("F001-00001", parsed.Invoice.OriginalDocumentId);
        Assert.Equal("2.1", parsed.Invoice.UblVersion); Assert.Equal("2.0", parsed.Invoice.Customization);
        Assert.Equal("Distribuidora Ñandú & Peña", parsed.Invoice.SupplierName); Assert.Equal("Botica San José", parsed.Invoice.BuyerName);
        Assert.Contains("solución Peña", Assert.Single(parsed.Invoice.Lines).Description, StringComparison.Ordinal);
        Assert.Equal(20m, parsed.Invoice.Subtotal); Assert.Equal(3.6m, parsed.Invoice.Tax); Assert.Equal(23.6m, parsed.Invoice.Total);
        Assert.Empty(parsed.Invoice.BlockingIssues); Assert.Equal(xml, Encoding.UTF8.GetString(parsed.Xml));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(xml))), parsed.Checksum);
    }
    [Theory]
    [InlineData("truncated")]
    [InlineData("doctype")]
    [InlineData("xxe")]
    [InlineData("expansion")]
    [InlineData("namespace")]
    [InlineData("cbc_namespace")]
    [InlineData("version")]
    [InlineData("customization")]
    [InlineData("type")]
    [InlineData("credit_note")]
    [InlineData("missing_supplier")]
    [InlineData("missing_buyer")]
    [InlineData("number")]
    [InlineData("zero_number")]
    [InlineData("negative_quantity")]
    [InlineData("negative_price")]
    [InlineData("comma_price")]
    [InlineData("duplicate_line")]
    [InlineData("description_length")]
    [InlineData("wrong_currency_attribute")]
    public async Task InvalidOrMaliciousDocumentsAreRejected(string scenario)
    {
        var xml = SupplierUblSyntheticXml.Create();
        xml = scenario switch
        {
            "truncated" => xml[..^30],
            "doctype" => xml.Replace("<Invoice ", "<!DOCTYPE Invoice><Invoice ", StringComparison.Ordinal),
            "xxe" => xml.Replace("<Invoice ", "<!DOCTYPE Invoice [<!ENTITY attack SYSTEM 'https://never-resolve.invalid/private'>]><Invoice ", StringComparison.Ordinal).Replace("Botica San José", "&attack;", StringComparison.Ordinal),
            "expansion" => xml.Replace("<Invoice ", "<!DOCTYPE Invoice [<!ENTITY a 'boom'><!ENTITY b '&a;&a;&a;&a;'>]><Invoice ", StringComparison.Ordinal).Replace("Botica San José", "&b;", StringComparison.Ordinal),
            "namespace" => xml.Replace("xsd:Invoice-2", "xsd:NotInvoice-2", StringComparison.Ordinal),
            "cbc_namespace" => xml.Replace("xsd:CommonBasicComponents-2", "xsd:WrongComponents-2", StringComparison.Ordinal),
            "version" => xml.Replace(">2.1<", ">2.0<", StringComparison.Ordinal),
            "customization" => xml.Replace(">2.0<", ">1.0<", StringComparison.Ordinal),
            "type" => xml.Replace(">01<", ">07<", StringComparison.Ordinal),
            "credit_note" => xml.Replace("Invoice", "CreditNote", StringComparison.Ordinal),
            "missing_supplier" => xml.Replace(SupplierUblSyntheticXml.SupplierRuc, "", StringComparison.Ordinal),
            "missing_buyer" => xml.Replace(SupplierUblSyntheticXml.BuyerRuc, "", StringComparison.Ordinal),
            "number" => xml.Replace("F001-1", "001-1", StringComparison.Ordinal),
            "zero_number" => xml.Replace("F001-1", "F001-0", StringComparison.Ordinal),
            "negative_quantity" => xml.Replace("unitCode='BX'>2<", "unitCode='BX'>-2<", StringComparison.Ordinal),
            "negative_price" => xml.Replace("<cbc:PriceAmount currencyID='PEN'>10<", "<cbc:PriceAmount currencyID='PEN'>-10<", StringComparison.Ordinal),
            "comma_price" => xml.Replace("<cbc:PriceAmount currencyID='PEN'>10<", "<cbc:PriceAmount currencyID='PEN'>10,00<", StringComparison.Ordinal),
            "duplicate_line" => SupplierUblSyntheticXml.Create(count: 2).Replace("<cbc:ID>2</cbc:ID>", "<cbc:ID>1</cbc:ID>", StringComparison.Ordinal),
            "description_length" => xml.Replace("Medicamento sintético 1 — solución Peña", new string('x', 2049), StringComparison.Ordinal),
            _ => xml.Replace("<cbc:PriceAmount currencyID='PEN'>", "<cbc:PriceAmount currencyID='USD'>", StringComparison.Ordinal),
        };
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => Parse(xml)); Assert.StartsWith("ubl.", error.Error.Code, StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("fr-FR")]
    [InlineData("es-PE")]
    [InlineData("en-US")]
    public async Task DecimalParsingUsesInvariantCulture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture); var invoice = (await Parse(SupplierUblSyntheticXml.Create(quantity: 2.5m))).Invoice;
            Assert.Equal(2.5m, invoice.Lines[0].Quantity); Assert.Equal(29.5m, invoice.Total); Assert.Empty(invoice.BlockingIssues);
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }
    [Theory]
    [InlineData("tax", "tax_incoherent")]
    [InlineData("total", "total_incoherent")]
    [InlineData("price", "line_amount_incoherent")]
    [InlineData("currency", "currency_unsupported")]
    public async Task DocumentIncoherenceAndUnsupportedCurrencyRemainExplicitEvidence(string scenario, string issue)
    {
        var xml = SupplierUblSyntheticXml.Create();
        xml = scenario switch
        {
            "tax" => xml.Replace("<cbc:Percent>18</cbc:Percent>", "<cbc:Percent>10</cbc:Percent>", StringComparison.Ordinal),
            "total" => xml.Replace("<cbc:PayableAmount currencyID='PEN'>23.60", "<cbc:PayableAmount currencyID='PEN'>100.00", StringComparison.Ordinal),
            "price" => xml.Replace("<cbc:PriceAmount currencyID='PEN'>10", "<cbc:PriceAmount currencyID='PEN'>11", StringComparison.Ordinal),
            _ => SupplierUblSyntheticXml.Create(currency: "USD")
        };
        Assert.Contains(issue, (await Parse(xml)).Invoice.BlockingIssues);
    }
    [Fact]
    public async Task FileLineDepthAndArchiveLimitsAreEnforced()
    {
        using var oversized = new MemoryStream(new byte[SupplierUblInvoiceParser.MaximumBytes + 1]);
        Assert.Equal("ubl.file_too_large", (await Assert.ThrowsAsync<ApplicationErrorException>(() => new SupplierUblInvoiceParser().ParseAsync(oversized, "fixture.xml", TestContext.Current.CancellationToken))).Error.Code);
        Assert.Equal("ubl.too_many_lines", (await Assert.ThrowsAsync<ApplicationErrorException>(() => Parse(SupplierUblSyntheticXml.Create(count: 1001)))).Error.Code);
        var deep = SupplierUblSyntheticXml.Create().Replace("</Invoice>", string.Concat(Enumerable.Repeat("<nested>", 66)) + string.Concat(Enumerable.Repeat("</nested>", 66)) + "</Invoice>", StringComparison.Ordinal);
        Assert.Equal("ubl.xml_complexity_exceeded", (await Assert.ThrowsAsync<ApplicationErrorException>(() => Parse(deep))).Error.Code);
        Assert.Equal("ubl.archive_unsupported", (await Assert.ThrowsAsync<ApplicationErrorException>(() => Parse(SupplierUblSyntheticXml.Create(), "archive.zip"))).Error.Code);
    }
    [Fact]
    public async Task CancellationStopsBeforeReading()
    {
        using var cancel = new CancellationTokenSource(); await cancel.CancelAsync(); using var stream = new MemoryStream(Encoding.UTF8.GetBytes(SupplierUblSyntheticXml.Create()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SupplierUblInvoiceParser().ParseAsync(stream, "fixture.xml", cancel.Token)); Assert.Equal(0, stream.Position);
    }
    [Fact]
    public async Task DeclaredTwoDecimalAmountsAllowOnlyTheExplicitCentTolerance()
    {
        Assert.Empty((await Parse(SupplierUblSyntheticXml.Create(quantity: 1, unitPrice: 10.0049999999m))).Invoice.BlockingIssues);
        var outside = SupplierUblSyntheticXml.Create(quantity: 1, unitPrice: 10m).Replace("<cbc:PriceAmount currencyID='PEN'>10", "<cbc:PriceAmount currencyID='PEN'>10.02", StringComparison.Ordinal);
        Assert.Contains("line_amount_incoherent", (await Parse(outside)).Invoice.BlockingIssues);
    }
    [Fact]
    public async Task PriceBaseQuantityAndGrossPriceRemainSeparateDocumentaryValues()
    {
        var xml = SupplierUblSyntheticXml.Create().Replace("<cac:Item>", "<cac:PricingReference><cac:AlternativeConditionPrice><cbc:PriceAmount currencyID='PEN'>11.80</cbc:PriceAmount><cbc:PriceTypeCode>01</cbc:PriceTypeCode></cac:AlternativeConditionPrice></cac:PricingReference><cac:Item>", StringComparison.Ordinal);
        var parsed = (await Parse(xml)).Invoice; Assert.Empty(parsed.BlockingIssues);
        Assert.Equal(10m, parsed.Lines[0].UnitPrice); Assert.Equal(11.8m, parsed.Lines[0].GrossUnitPrice);
        Assert.Equal(3.6m, parsed.Lines[0].Tax); Assert.Equal(20m, parsed.Lines[0].LineAmount);
        var perTwo = SupplierUblSyntheticXml.Create().Replace("<cbc:PriceAmount currencyID='PEN'>10</cbc:PriceAmount>", "<cbc:PriceAmount currencyID='PEN'>20</cbc:PriceAmount><cbc:BaseQuantity unitCode='BX'>2</cbc:BaseQuantity>", StringComparison.Ordinal);
        var byBasis = (await Parse(perTwo)).Invoice; Assert.Empty(byBasis.BlockingIssues);
        Assert.Equal(2m, byBasis.Lines[0].PriceBaseQuantity); Assert.Equal(20m, byBasis.Lines[0].UnitPrice);
        Assert.Contains("gross_price_incoherent", (await Parse(xml.Replace(">11.80<", ">12.00<", StringComparison.Ordinal))).Invoice.BlockingIssues);
    }
    [Fact]
    public async Task LineDiscountIsCheckedAndPreservedWithoutBecomingInventoryCost()
    {
        var xml = SupplierUblSyntheticXml.Create().Replace("<cac:Item>", "<cac:AllowanceCharge><cbc:ChargeIndicator>false</cbc:ChargeIndicator><cbc:Amount currencyID='PEN'>4.00</cbc:Amount><cbc:BaseAmount currencyID='PEN'>24.00</cbc:BaseAmount></cac:AllowanceCharge><cac:Item>", StringComparison.Ordinal)
            .Replace("<cbc:PriceAmount currencyID='PEN'>10<", "<cbc:PriceAmount currencyID='PEN'>12<", StringComparison.Ordinal);
        var parsed = (await Parse(xml)).Invoice; Assert.Empty(parsed.BlockingIssues);
        Assert.Equal(4m, parsed.Lines[0].Discounts); Assert.Equal(12m, parsed.Lines[0].UnitPrice); Assert.Equal(20m, parsed.Lines[0].LineAmount);
        Assert.Contains("line_amount_incoherent", (await Parse(xml.Replace(">4.00<", ">5.00<", StringComparison.Ordinal))).Invoice.BlockingIssues);
    }
    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e2")]
    [InlineData("1000000000000")]
    public async Task NonDecimalOrOutOfProfilePricesAreRejected(string price)
    {
        var xml = SupplierUblSyntheticXml.Create().Replace("<cbc:PriceAmount currencyID='PEN'>10<", "<cbc:PriceAmount currencyID='PEN'>" + price + "<", StringComparison.Ordinal);
        Assert.Equal("ubl.number_invalid", (await Assert.ThrowsAsync<ApplicationErrorException>(() => Parse(xml))).Error.Code);
    }
    [Fact]
    public async Task ZeroAndLargeBoundedAmountsPreserveExactDecimalArithmetic()
    {
        Assert.Empty((await Parse(SupplierUblSyntheticXml.Create(unitPrice: 0))).Invoice.BlockingIssues);
        var invoice = (await Parse(SupplierUblSyntheticXml.Create(quantity: 1, unitPrice: 100000000000m))).Invoice;
        Assert.Equal(118000000000m, invoice.Total); Assert.Empty(invoice.BlockingIssues);
    }
    [Fact]
    public async Task CancellationDuringStreamReadStopsImport()
    {
        using var cancel = new CancellationTokenSource();
        using var stream = new CancellingStream(Encoding.UTF8.GetBytes(SupplierUblSyntheticXml.Create()), cancel);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SupplierUblInvoiceParser().ParseAsync(stream, "synthetic.xml", cancel.Token));
        Assert.True(stream.Position > 0);
    }
    private sealed class CancellingStream(byte[] bytes, CancellationTokenSource cancel) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { var count = await base.ReadAsync(buffer, cancellationToken); await cancel.CancelAsync(); return count; }
    }
}
