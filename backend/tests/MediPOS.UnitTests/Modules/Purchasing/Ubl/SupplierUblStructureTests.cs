using System.Text;
using System.Xml.Linq;
using MediPOS.Application.Errors;
using MediPOS.Infrastructure.Modules.Purchasing.Ubl;
using MediPOS.TestFixtures;

namespace MediPOS.UnitTests.Modules.Purchasing.Ubl;

public sealed class SupplierUblStructureTests
{
    public static TheoryData<string> RequiredPaths => new(SupplierUblSyntheticXml.RequiredElementPaths);
    [Theory]
    [MemberData(nameof(RequiredPaths))]
    public async Task EveryRequiredNodeMissingIsAControlledSanitizedApplicationError(string path)
    {
        var xml = SupplierUblSyntheticXml.ChangeElement(SupplierUblSyntheticXml.Create(), path, element => element.Remove());
        await AssertRejected(xml, "ubl.required_element_missing");
    }
    [Theory]
    [MemberData(nameof(RequiredPaths))]
    public async Task DuplicatedSingleNodesAreRejectedBeforeReadingValues(string path)
    {
        var xml = SupplierUblSyntheticXml.ChangeElement(SupplierUblSyntheticXml.Create(), path, element => element.AddAfterSelf(new XElement(element)));
        await AssertRejected(xml, "ubl.element_duplicated");
    }
    [Theory]
    [MemberData(nameof(RequiredPaths))]
    public async Task AMatchingLocalNameInAnotherNamespaceCannotSatisfyTheStructure(string path)
    {
        var xml = SupplierUblSyntheticXml.ChangeElement(SupplierUblSyntheticXml.Create(), path, element => element.Name = XName.Get(element.Name.LocalName, "urn:synthetic:wrong"));
        await AssertRejected(xml, "ubl.namespace_invalid");
    }
    [Fact]
    public async Task AllInvoiceLinesMissingIsControlledWithoutInventingALine()
    {
        var xml = SupplierUblSyntheticXml.ChangeElement(SupplierUblSyntheticXml.Create(), "cac:InvoiceLine", element => element.Remove());
        await AssertRejected(xml, "ubl.lines_required");
    }
    [Theory]
    [InlineData("cac:InvoiceLine/cac:Price/cbc:PriceAmount")]
    [InlineData("cac:InvoiceLine/cac:Item/cbc:Description")]
    [InlineData("cac:TaxTotal/cbc:TaxAmount")]
    public async Task NestedElementsCannotMasqueradeAsScalarRequiredValues(string path)
    {
        var xml = SupplierUblSyntheticXml.ChangeElement(SupplierUblSyntheticXml.Create(), path, element => element.Add(new XElement("nested", "secret")));
        await AssertRejected(xml, "ubl.field_invalid");
    }
    [Fact]
    public async Task ExactNamespaceAndSpoofedNamespaceTogetherAreStillRejected()
    {
        var xml = SupplierUblSyntheticXml.ChangeElement(SupplierUblSyntheticXml.Create(), "cac:TaxTotal", element =>
            element.AddAfterSelf(new XElement(XName.Get(element.Name.LocalName, "urn:synthetic:spoof"), element.Elements())));
        await AssertRejected(xml, "ubl.namespace_invalid");
    }
    [Theory]
    [InlineData("cac:AccountingSupplierParty")]
    [InlineData("cac:TaxTotal")]
    [InlineData("cac:InvoiceLine/cac:Price")]
    public async Task AggregateNodesRejectAmbiguousMixedText(string path)
    {
        var xml = SupplierUblSyntheticXml.ChangeElement(SupplierUblSyntheticXml.Create(), path, element => element.AddFirst("Unexpected aggregate value"));
        await AssertRejected(xml, "ubl.structure_invalid");
    }
    [Fact]
    public async Task OptionalTaxBreakdownKeepsTheExistingExplicitReviewWarning()
    {
        var xml = SupplierUblSyntheticXml.ChangeElement(SupplierUblSyntheticXml.Create(), "cac:TaxTotal/cac:TaxSubtotal", element => element.Remove());
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var parsed = await new SupplierUblInvoiceParser().ParseAsync(input, "synthetic.xml", TestContext.Current.CancellationToken);
        Assert.Contains("tax_breakdown_requires_review", parsed.Invoice.ReviewWarnings);
        Assert.Equal(3.60m, parsed.Invoice.Tax); Assert.Equal(23.60m, parsed.Invoice.Total);
    }
    [Theory]
    [InlineData("cac:InvoiceLine/cac:AllowanceCharge/cbc:ChargeIndicator")]
    [InlineData("cac:InvoiceLine/cac:AllowanceCharge/cbc:Amount")]
    [InlineData("cac:InvoiceLine/cac:PricingReference/cac:AlternativeConditionPrice/cbc:PriceTypeCode")]
    [InlineData("cac:InvoiceLine/cac:PricingReference/cac:AlternativeConditionPrice/cbc:PriceAmount")]
    [InlineData("cac:InvoiceLine/cac:Item/cac:StandardItemIdentification/cbc:ID")]
    public async Task PresentOptionalContainersMustSupplyTheirRequiredChildren(string path)
    {
        var xml = SupplierUblSyntheticXml.Create(barcode: "0123456789012")
            .Replace("<cac:Item>", "<cac:AllowanceCharge><cbc:ChargeIndicator>false</cbc:ChargeIndicator><cbc:Amount currencyID='PEN'>0</cbc:Amount></cac:AllowanceCharge><cac:PricingReference><cac:AlternativeConditionPrice><cbc:PriceAmount currencyID='PEN'>11.80</cbc:PriceAmount><cbc:PriceTypeCode>01</cbc:PriceTypeCode></cac:AlternativeConditionPrice></cac:PricingReference><cac:Item>", StringComparison.Ordinal);
        await AssertRejected(SupplierUblSyntheticXml.ChangeElement(xml, path, element => element.Remove()), "ubl.required_element_missing");
    }
    [Fact]
    public async Task CancellationRemainsCancellationEvenWhenTheDocumentIsIncomplete()
    {
        var xml = SupplierUblSyntheticXml.ChangeElement(SupplierUblSyntheticXml.Create(), "cac:TaxTotal", element => element.Remove());
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(xml)); using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SupplierUblInvoiceParser().ParseAsync(input, "synthetic.xml", cancel.Token));
        Assert.Equal(0, input.Position);
    }
    [Theory]
    [InlineData("malformed", "ubl.xml_invalid")]
    [InlineData("malformed_comment", "ubl.xml_invalid")]
    [InlineData("malformed_cdata", "ubl.xml_invalid")]
    [InlineData("doctype", "ubl.xml_invalid_or_unsafe")]
    [InlineData("xxe", "ubl.xml_invalid_or_unsafe")]
    public async Task MalformedAndUnsafeXmlHaveControlledDistinctCodes(string scenario, string code)
    {
        var xml = SupplierUblSyntheticXml.Create();
        xml = scenario switch
        {
            "malformed" => xml[..^15],
            "malformed_comment" => xml.Replace("<Invoice ", "<!-- <!DOCTYPE Invoice> --><Invoice ", StringComparison.Ordinal)[..^15],
            "malformed_cdata" => xml.Replace("Medicamento sintético 1 — solución Peña", "<![CDATA[<!DOCTYPE Invoice>]]>", StringComparison.Ordinal)[..^15],
            "doctype" => xml.Replace("<Invoice ", "<!DOCTYPE Invoice><Invoice ", StringComparison.Ordinal),
            _ => xml.Replace("<Invoice ", "<!DOCTYPE Invoice [<!ENTITY attack SYSTEM 'file:///internal-secret'>]><Invoice ", StringComparison.Ordinal).Replace("Botica San José", "&attack;", StringComparison.Ordinal),
        };
        await AssertRejected(xml, code);
    }
    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    [InlineData("utf-32")]
    [InlineData("utf-32BE")]
    public async Task DtdRejectionClassificationRespectsXmlEncoding(string name)
    {
        var xml = SupplierUblSyntheticXml.Create().Replace("encoding=\"utf-8\"", "encoding=\"" + name + "\"", StringComparison.Ordinal)
            .Replace("<Invoice ", "<!DOCTYPE Invoice><Invoice ", StringComparison.Ordinal);
        var encoding = Encoding.GetEncoding(name);
        await AssertRejectedBytes(encoding.GetPreamble().Concat(encoding.GetBytes(xml)).ToArray(), "ubl.xml_invalid_or_unsafe");
    }
    [Fact]
    public async Task ValidUnicodeAndLiteralDtdTextInCommentsOrCdataRemainEvidence()
    {
        var xml = SupplierUblSyntheticXml.Create().Replace("<Invoice ", "<!-- <!DOCTYPE Invoice> --><Invoice ", StringComparison.Ordinal)
            .Replace("Medicamento sintético 1 — solución Peña", "<![CDATA[Medicamento sintético — solución Peña <!DOCTYPE Invoice>]]>", StringComparison.Ordinal);
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var parsed = await new SupplierUblInvoiceParser().ParseAsync(input, "synthetic.xml", TestContext.Current.CancellationToken);
        Assert.Equal("Distribuidora Ñandú & Peña", parsed.Invoice.SupplierName); Assert.Equal("Botica San José", parsed.Invoice.BuyerName);
        Assert.Contains("solución Peña <!DOCTYPE Invoice>", parsed.Invoice.Lines[0].Description, StringComparison.Ordinal);
        Assert.Equal(Encoding.UTF8.GetBytes(xml), parsed.Xml); Assert.Empty(parsed.Invoice.BlockingIssues);
    }
    private static Task AssertRejected(string xml, string code) => AssertRejectedBytes(Encoding.UTF8.GetBytes(xml), code);
    private static async Task AssertRejectedBytes(byte[] xml, string code)
    {
        using var input = new MemoryStream(xml);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => new SupplierUblInvoiceParser().ParseAsync(input, "synthetic.xml", TestContext.Current.CancellationToken));
        Assert.Equal(code, error.Error.Code); Assert.Equal(ErrorCategory.Validation, error.Error.Category); Assert.Null(error.InnerException);
        Assert.Equal("Supplier invoice operation could not be completed.", error.Error.Message);
        Assert.DoesNotContain(SupplierUblSyntheticXml.SupplierRuc, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SupplierUblSyntheticXml.BuyerRuc, error.Message, StringComparison.Ordinal);
    }
}
