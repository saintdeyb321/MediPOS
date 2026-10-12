using System.Globalization;
using System.Security;

namespace MediPOS.TestFixtures;

// Synthetic fixture only; never downloaded from SUNAT or a real supplier.
internal static class SupplierUblSyntheticXml
{
    internal const string SupplierRuc = "20100000001";
    internal const string BuyerRuc = "20999999999";
    internal static string Create(string document = "F001-1", string buyer = BuyerRuc, int count = 1,
        decimal quantity = 2m, decimal unitPrice = 10m, string currency = "PEN", string unit = "BX", string? buyerCode = "SKU-1", string? barcode = null)
    {
        string N(decimal v) => v.ToString(CultureInfo.InvariantCulture);
        string E(string v) => SecurityElement.Escape(v)!;
        var net = decimal.Round(quantity * unitPrice, 2, MidpointRounding.AwayFromZero);
        var tax = decimal.Round(net * 0.18m, 2, MidpointRounding.AwayFromZero);
        string Tax(decimal taxable, decimal amount) => $"<cac:TaxTotal><cbc:TaxAmount currencyID='{currency}'>{N(amount)}</cbc:TaxAmount><cac:TaxSubtotal><cbc:TaxableAmount currencyID='{currency}'>{N(taxable)}</cbc:TaxableAmount><cbc:TaxAmount currencyID='{currency}'>{N(amount)}</cbc:TaxAmount><cac:TaxCategory><cbc:Percent>18</cbc:Percent><cbc:TaxExemptionReasonCode>10</cbc:TaxExemptionReasonCode><cac:TaxScheme><cbc:ID>1000</cbc:ID><cbc:Name>IGV</cbc:Name></cac:TaxScheme></cac:TaxCategory></cac:TaxSubtotal></cac:TaxTotal>";
        var lines = Enumerable.Range(1, count).Select(i => $"""
            <cac:InvoiceLine><cbc:ID>{i}</cbc:ID><cbc:InvoicedQuantity unitCode='{E(unit)}'>{N(quantity)}</cbc:InvoicedQuantity>
            <cbc:LineExtensionAmount currencyID='{currency}'>{N(net)}</cbc:LineExtensionAmount>{Tax(net, tax)}
            <cac:Item><cbc:Description>Medicamento sintético {i} — solución Peña</cbc:Description>
            <cac:SellersItemIdentification><cbc:ID>DIST-{i}</cbc:ID></cac:SellersItemIdentification>
            {(buyerCode is null ? "" : $"<cac:BuyersItemIdentification><cbc:ID>{E(count == 1 ? buyerCode : buyerCode + i.ToString(CultureInfo.InvariantCulture))}</cbc:ID></cac:BuyersItemIdentification>")}
            {(barcode is null ? "" : $"<cac:StandardItemIdentification><cbc:ID schemeID='GTIN'>{E(barcode)}</cbc:ID></cac:StandardItemIdentification>")}
            </cac:Item><cac:Price><cbc:PriceAmount currencyID='{currency}'>{N(unitPrice)}</cbc:PriceAmount></cac:Price></cac:InvoiceLine>
            """);
        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
            xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2"
            xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
            <cbc:UBLVersionID>2.1</cbc:UBLVersionID><cbc:CustomizationID>2.0</cbc:CustomizationID>
            <cbc:ID>{E(document)}</cbc:ID><cbc:IssueDate>2026-10-12</cbc:IssueDate><cbc:InvoiceTypeCode>01</cbc:InvoiceTypeCode>
            <cbc:DocumentCurrencyCode>{currency}</cbc:DocumentCurrencyCode>
            <cac:AccountingSupplierParty><cac:Party><cac:PartyIdentification><cbc:ID schemeID='6'>{SupplierRuc}</cbc:ID></cac:PartyIdentification>
            <cac:PartyLegalEntity><cbc:RegistrationName>Distribuidora Ñandú &amp; Peña</cbc:RegistrationName></cac:PartyLegalEntity></cac:Party></cac:AccountingSupplierParty>
            <cac:AccountingCustomerParty><cac:Party><cac:PartyIdentification><cbc:ID schemeID='6'>{E(buyer)}</cbc:ID></cac:PartyIdentification>
            <cac:PartyLegalEntity><cbc:RegistrationName>Botica San José</cbc:RegistrationName></cac:PartyLegalEntity></cac:Party></cac:AccountingCustomerParty>
            {Tax(net * count, tax * count)}<cac:LegalMonetaryTotal><cbc:LineExtensionAmount currencyID='{currency}'>{N(net * count)}</cbc:LineExtensionAmount>
            <cbc:TaxExclusiveAmount currencyID='{currency}'>{N(net * count)}</cbc:TaxExclusiveAmount>
            <cbc:TaxInclusiveAmount currencyID='{currency}'>{N((net + tax) * count)}</cbc:TaxInclusiveAmount>
            <cbc:PayableAmount currencyID='{currency}'>{N((net + tax) * count)}</cbc:PayableAmount></cac:LegalMonetaryTotal>
            {string.Join("\n", lines)}</Invoice>
            """;
    }
}
