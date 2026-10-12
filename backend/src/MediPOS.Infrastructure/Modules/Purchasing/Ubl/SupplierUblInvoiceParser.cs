using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using MediPOS.Application.Modules.Purchasing.Ubl;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Purchasing.Ubl;

namespace MediPOS.Infrastructure.Modules.Purchasing.Ubl;

public sealed partial class SupplierUblInvoiceParser : ISupplierUblInvoiceParser
{
    public const int MaximumBytes = 5 * 1024 * 1024;
    private static readonly XNamespace InvoiceNs = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    [GeneratedRegex("^F[A-Z0-9]{3}-[0-9]{1,8}$", RegexOptions.CultureInvariant)]
    private static partial Regex DocumentNumber();
    [GeneratedRegex("^-?[0-9]{1,12}(\\.[0-9]{1,10})?$", RegexOptions.CultureInvariant)]
    private static partial Regex Number();
    public async Task<ParsedSupplierUblInvoice> ParseAsync(Stream input, string fileName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input); cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 256 || fileName.Any(c => char.IsControl(c) || c is '/' or '\\' or ':')) throw SupplierUblErrors.Error("file_name_invalid");
        if (Path.GetExtension(fileName).Equals(".zip", StringComparison.OrdinalIgnoreCase)) throw SupplierUblErrors.Error("archive_unsupported");
        using var buffer = new MemoryStream(); var block = new byte[16384];
        while (true)
        {
            var count = await input.ReadAsync(block, cancellationToken).ConfigureAwait(false); if (count == 0) break;
            if (buffer.Length + count > MaximumBytes) throw SupplierUblErrors.Error("file_too_large");
            await buffer.WriteAsync(block.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
        }
        if (buffer.Length == 0) throw SupplierUblErrors.Error("xml_invalid");
        var bytes = buffer.ToArray(); var checksum = Convert.ToHexStringLower(SHA256.HashData(bytes));
        string? declaredEncoding = null;
        try
        {
            var settings = new XmlReaderSettings
            {
                Async = true,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumBytes,
                MaxCharactersFromEntities = 1024,
                IgnoreComments = true
            };
            buffer.Position = 0; var nodes = 0; var lines = 0;
            using (var reader = XmlReader.Create(buffer, settings))
            {
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (reader.NodeType == XmlNodeType.XmlDeclaration) declaredEncoding = reader.GetAttribute("encoding");
                    if (reader.Depth > 64 || ++nodes > 200000) throw SupplierUblErrors.Error("xml_complexity_exceeded");
                    if (reader.AttributeCount > 32 || reader.Value.Length > 32768) throw SupplierUblErrors.Error("field_too_long");
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        while (reader.MoveToNextAttribute())
                            if (reader.Value.Length > 2048) throw SupplierUblErrors.Error("field_too_long");
                        reader.MoveToElement();
                    }
                    if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "InvoiceLine" && reader.NamespaceURI == Cac.NamespaceName && ++lines > SupplierUblRules.MaximumLines)
                        throw SupplierUblErrors.Error("too_many_lines");
                }
            }
            buffer.Position = 0; using var hardened = XmlReader.Create(buffer, settings);
            var document = await XDocument.LoadAsync(hardened, LoadOptions.PreserveWhitespace, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new(Parse(document.Root ?? throw SupplierUblErrors.Error("xml_invalid"), cancellationToken), bytes, checksum);
        }
        catch (XmlException)
        { throw SupplierUblErrors.Error(HasDtdDeclaration(bytes, declaredEncoding) ? "xml_invalid_or_unsafe" : "xml_invalid"); }
        catch (ArithmeticException) { throw SupplierUblErrors.Error("monetary_out_of_range"); }
    }
    // Classify an already rejected XML without parsing a DTD or inspecting localized exception messages.
    // The original hardened reader remains the authority; this bounded scan never accepts a document.
    private static bool HasDtdDeclaration(byte[] bytes, string? declaredEncoding)
    {
        Encoding encoding;
        if (bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xff, 0xfe, 0, 0]) || bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0x3c, 0, 0, 0])) encoding = Encoding.UTF32;
        else if (bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0, 0, 0xfe, 0xff]) || bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0, 0, 0, 0x3c])) encoding = new UTF32Encoding(true, false);
        else if (bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xff, 0xfe]) || bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0x3c, 0, 0x3f, 0])) encoding = Encoding.Unicode;
        else if (bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xfe, 0xff]) || bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0, 0x3c, 0, 0x3f])) encoding = Encoding.BigEndianUnicode;
        else
        {
            try { encoding = declaredEncoding is null ? Encoding.UTF8 : Encoding.GetEncoding(declaredEncoding); }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
        }
        var xml = encoding.GetString(bytes);
        var offset = 0;
        while ((offset = xml.IndexOf('<', offset)) >= 0)
        {
            var remaining = xml.AsSpan(offset);
            string? terminator = remaining.StartsWith("<!--", StringComparison.Ordinal) ? "-->" :
                remaining.StartsWith("<![CDATA[", StringComparison.Ordinal) ? "]]>" :
                remaining.StartsWith("<?", StringComparison.Ordinal) ? "?>" : null;
            if (terminator is not null)
            {
                var end = xml.IndexOf(terminator, offset + 2, StringComparison.Ordinal);
                if (end < 0) return false;
                offset = end + terminator.Length; continue;
            }
            if (remaining.StartsWith("<!DOCTYPE", StringComparison.Ordinal) && remaining.Length > 9 && remaining[9] is ' ' or '\t' or '\r' or '\n') return true;
            char quote = '\0';
            while (++offset < xml.Length)
            {
                var value = xml[offset];
                if (quote != '\0') { if (value == quote) quote = '\0'; }
                else if (value is '\'' or '"') quote = value;
                else if (value == '>') { offset++; break; }
            }
        }
        return false;
    }
    private static XElement RequireElement(XElement? parent, XName name) =>
        OptionalElement(parent, name) ?? throw SupplierUblErrors.Error("required_element_missing");
    private static XElement? OptionalElement(XElement? parent, XName name)
    {
        parent = RequireContainer(parent);
        if (parent.Elements().Any(e => e.Name.LocalName == name.LocalName && e.Name != name)) throw SupplierUblErrors.Error("namespace_invalid");
        var values = parent.Elements(name).Take(2).ToArray();
        if (values.Length > 1) throw SupplierUblErrors.Error("element_duplicated");
        return values.SingleOrDefault();
    }
    private static XElement RequireContainer(XElement? parent)
    {
        if (parent is null || parent.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)))
            throw SupplierUblErrors.Error("structure_invalid");
        return parent;
    }
    private static string Text(XElement? element, int maximum, bool required = true)
    {
        var value = element?.Value;
        if (element?.HasElements == true || value?.Length > maximum || required && string.IsNullOrWhiteSpace(value)) throw SupplierUblErrors.Error("field_invalid");
        return value ?? "";
    }
    private static string Code(XElement? element, int maximum, bool required = true) => Text(element, maximum, required).Trim();
    private static IEnumerable<XElement> Many(XElement parent, XName name)
    {
        RequireContainer(parent);
        if (parent.Elements().Any(e => e.Name.LocalName == name.LocalName && e.Name != name)) throw SupplierUblErrors.Error("namespace_invalid");
        return parent.Elements(name);
    }
    private static decimal Numeric(XElement? element, bool optional = false, bool signed = false)
    {
        if (element is null && optional) return 0;
        var raw = Code(element, 32);
        if (!Number().IsMatch(raw) || !decimal.TryParse(raw, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || !signed && value < 0)
            throw SupplierUblErrors.Error("number_invalid");
        return value;
    }
    private static decimal Money(XElement? element, string currency, bool optional = false, bool signed = false)
    {
        if (element is null && optional) return 0;
        if (element?.Attribute("currencyID")?.Value != currency) throw SupplierUblErrors.Error("currency_mismatch");
        var value = Numeric(element, signed: signed);
        if (decimal.Round(value, 2) != value) throw SupplierUblErrors.Error("money_precision_invalid");
        return value;
    }
    private static (string Ruc, string Name) Party(XElement root, string name)
    {
        var party = RequireElement(RequireElement(root, Cac + name), Cac + "Party");
        var id = RequireElement(RequireElement(party, Cac + "PartyIdentification"), Cbc + "ID");
        var ruc = Code(id, 11);
        if (id.Attribute("schemeID")?.Value != "6" || !SupplierUblRules.IsRuc(ruc)) throw SupplierUblErrors.Error("ruc_invalid");
        return (ruc, Text(RequireElement(RequireElement(party, Cac + "PartyLegalEntity"), Cbc + "RegistrationName"), 256));
    }
    private static (decimal Discounts, decimal Charges, string Json) Allowances(XElement parent, string currency)
    {
        decimal discounts = 0, charges = 0; var evidence = new List<object>();
        if (Many(parent, Cac + "AllowanceCharge").Count() > 32) throw SupplierUblErrors.Error("structure_invalid");
        foreach (var allowance in Many(parent, Cac + "AllowanceCharge"))
        {
            var indicator = Code(RequireElement(allowance, Cbc + "ChargeIndicator"), 5);
            if (indicator is not ("true" or "false")) throw SupplierUblErrors.Error("allowance_invalid");
            var amount = Money(RequireElement(allowance, Cbc + "Amount"), currency);
            var baseElement = OptionalElement(allowance, Cbc + "BaseAmount"); var factorElement = OptionalElement(allowance, Cbc + "MultiplierFactorNumeric");
            var basis = baseElement is null ? (decimal?)null : Money(baseElement, currency); var factor = factorElement is null ? (decimal?)null : Numeric(factorElement);
            if (basis.HasValue && factor.HasValue && Math.Abs(amount - (factor == 0 ? 0 : ProductUnit.ConvertExactly(basis.Value, factor.Value))) > SupplierUblRules.MoneyTolerance)
                throw SupplierUblErrors.Error("allowance_incoherent");
            if (indicator == "true") charges = checked(charges + amount); else discounts = checked(discounts + amount);
            evidence.Add(new { charge = indicator == "true", amount, basis, factor, reason = Code(OptionalElement(allowance, Cbc + "AllowanceChargeReasonCode"), 32, false) });
        }
        return (discounts, charges, JsonSerializer.Serialize(evidence));
    }
    private static (decimal Amount, string Json, decimal? SingleBasis) Taxes(XElement parent, string currency, List<string> issues, List<string> warnings)
    {
        var total = RequireElement(parent, Cac + "TaxTotal"); var amount = Money(RequireElement(total, Cbc + "TaxAmount"), currency);
        var subtotals = new List<object>(); decimal sum = 0; decimal? singleBasis = null;
        foreach (var sub in Many(total, Cac + "TaxSubtotal"))
        {
            if (subtotals.Count >= 32) throw SupplierUblErrors.Error("structure_invalid");
            var tax = Money(RequireElement(sub, Cbc + "TaxAmount"), currency); sum = checked(sum + tax);
            var basisElement = OptionalElement(sub, Cbc + "TaxableAmount"); var basis = basisElement is null ? (decimal?)null : Money(basisElement, currency);
            singleBasis = basis;
            var category = RequireElement(sub, Cac + "TaxCategory");
            var percentElement = OptionalElement(category, Cbc + "Percent"); var percent = percentElement is null ? (decimal?)null : Numeric(percentElement);
            var scheme = Code(RequireElement(RequireElement(category, Cac + "TaxScheme"), Cbc + "ID"), 32);
            if (basis.HasValue && percent.HasValue)
            {
                if (percent > 100 || Math.Abs(tax - (percent == 0 ? 0 : ProductUnit.ConvertExactly(basis.Value, percent.Value) / 100m)) > SupplierUblRules.MoneyTolerance) issues.Add("tax_incoherent");
            }
            else warnings.Add("tax_basis_requires_review");
            if (scheme is not ("1000" or "9997" or "9998")) warnings.Add("tax_scheme_requires_review");
            subtotals.Add(new { tax, basis, percent, scheme, exemption = Code(OptionalElement(category, Cbc + "TaxExemptionReasonCode"), 32, false) });
        }
        if (subtotals.Count == 0) warnings.Add("tax_breakdown_requires_review");
        else if (Math.Abs(sum - amount) > SupplierUblRules.MoneyTolerance) issues.Add("tax_incoherent");
        return (amount, JsonSerializer.Serialize(subtotals), subtotals.Count == 1 ? singleBasis : null);
    }
    private static SupplierUblInvoiceEvidence Parse(XElement root, CancellationToken token)
    {
        if (root.Name.LocalName != "Invoice") throw SupplierUblErrors.Error("invoice_type_unsupported");
        if (root.Name != InvoiceNs + "Invoice") throw SupplierUblErrors.Error("namespace_invalid");
        var version = Code(RequireElement(root, Cbc + "UBLVersionID"), 8); var customization = Code(RequireElement(root, Cbc + "CustomizationID"), 8);
        if (version != "2.1" || customization != "2.0") throw SupplierUblErrors.Error("profile_unsupported");
        var type = Code(RequireElement(root, Cbc + "InvoiceTypeCode"), 2); if (type != "01") throw SupplierUblErrors.Error("invoice_type_unsupported");
        var originalId = Text(RequireElement(root, Cbc + "ID"), 64); var number = originalId.Trim();
        if (!DocumentNumber().IsMatch(number)) throw SupplierUblErrors.Error("document_number_invalid");
        var parts = number.Split('-'); var serial = uint.Parse(parts[1], CultureInfo.InvariantCulture);
        if (serial == 0) throw SupplierUblErrors.Error("document_number_invalid");
        if (!DateOnly.TryParseExact(Code(RequireElement(root, Cbc + "IssueDate"), 10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) throw SupplierUblErrors.Error("issue_date_invalid");
        var currency = Code(RequireElement(root, Cbc + "DocumentCurrencyCode"), 3);
        if (currency.Length != 3 || !currency.All(char.IsAsciiLetterUpper)) throw SupplierUblErrors.Error("currency_invalid");
        var supplier = Party(root, "AccountingSupplierParty"); var buyer = Party(root, "AccountingCustomerParty");
        var issues = new List<string>(); var warnings = new List<string>(); var lines = new List<SupplierUblLineEvidence>();
        if (root.Elements().Any(e => e.Name.LocalName == "InvoiceLine" && e.Name != Cac + "InvoiceLine")) throw SupplierUblErrors.Error("namespace_invalid");
        foreach (var line in root.Elements(Cac + "InvoiceLine"))
        {
            token.ThrowIfCancellationRequested(); if (lines.Count >= SupplierUblRules.MaximumLines) throw SupplierUblErrors.Error("too_many_lines");
            var id = Text(RequireElement(line, Cbc + "ID"), 16); if (!int.TryParse(id.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal) || ordinal <= 0 || lines.Any(value => value.Number == ordinal)) throw SupplierUblErrors.Error("line_number_invalid");
            var quantityElement = RequireElement(line, Cbc + "InvoicedQuantity"); var quantity = Numeric(quantityElement); if (quantity <= 0) throw SupplierUblErrors.Error("quantity_invalid");
            var unit = quantityElement.Attribute("unitCode")?.Value ?? ""; if (unit.Length is < 1 or > 16 || !unit.All(char.IsAsciiLetterOrDigit)) throw SupplierUblErrors.Error("unit_invalid");
            var price = RequireElement(line, Cac + "Price"); var priceAmount = RequireElement(price, Cbc + "PriceAmount"); var unitPrice = Numeric(priceAmount);
            if (priceAmount.Attribute("currencyID")?.Value != currency) throw SupplierUblErrors.Error("currency_mismatch");
            var basisElement = OptionalElement(price, Cbc + "BaseQuantity"); var priceBasis = basisElement is null ? 1m : Numeric(basisElement); if (priceBasis <= 0) throw SupplierUblErrors.Error("price_invalid");
            var lineWarnings = new List<string>(); if (basisElement?.Attribute("unitCode") is { } baseUnit && baseUnit.Value != unit) lineWarnings.Add("price_unit_requires_review");
            var adjustments = Allowances(line, currency); var taxes = Taxes(line, currency, issues, lineWarnings);
            var amount = Money(RequireElement(line, Cbc + "LineExtensionAmount"), currency);
            if (taxes.SingleBasis.HasValue && Math.Abs(taxes.SingleBasis.Value - amount) > SupplierUblRules.MoneyTolerance) issues.Add("tax_incoherent");
            var expected = (unitPrice == 0 ? 0 : ProductUnit.ConvertExactly(quantity, unitPrice) / priceBasis) - adjustments.Discounts + adjustments.Charges;
            if (Math.Abs(amount - expected) > SupplierUblRules.MoneyTolerance) issues.Add("line_amount_incoherent");
            var item = RequireElement(line, Cac + "Item"); var standard = OptionalElement(item, Cac + "StandardItemIdentification"); var standardId = standard is null ? null : RequireElement(standard, Cbc + "ID");
            string? ItemCode(string container) { var e = OptionalElement(item, Cac + container); return e is null ? null : Text(RequireElement(e, Cbc + "ID"), 128); }
            decimal? gross = null; var pricing = OptionalElement(line, Cac + "PricingReference");
            if (pricing is not null)
                foreach (var alternative in Many(pricing, Cac + "AlternativeConditionPrice"))
                {
                    if (Code(RequireElement(alternative, Cbc + "PriceTypeCode"), 8) == "01")
                    { if (gross.HasValue) throw SupplierUblErrors.Error("price_invalid"); var grossAmount = RequireElement(alternative, Cbc + "PriceAmount"); gross = Numeric(grossAmount); if (grossAmount.Attribute("currencyID")?.Value != currency) throw SupplierUblErrors.Error("currency_mismatch"); }
                    else lineWarnings.Add("alternative_price_requires_review");
                }
            if (gross.HasValue && adjustments.Discounts == 0 && adjustments.Charges == 0 &&
                Math.Abs((gross == 0 ? 0 : ProductUnit.ConvertExactly(quantity, gross.Value) / priceBasis) - (amount + taxes.Amount)) > SupplierUblRules.MoneyTolerance) issues.Add("gross_price_incoherent");
            if (standardId?.Attribute("schemeID")?.Value is { Length: > 32 }) throw SupplierUblErrors.Error("field_too_long");
            lines.Add(new(ordinal, id, Text(RequireElement(item, Cbc + "Description"), 2048), ItemCode("SellersItemIdentification"), ItemCode("BuyersItemIdentification"),
                standardId is null ? null : Text(standardId, 128), standardId?.Attribute("schemeID")?.Value, quantity, unit, unitPrice, priceBasis,
                adjustments.Discounts, adjustments.Charges, taxes.Amount, amount, gross, taxes.Json, adjustments.Json, lineWarnings.Distinct(StringComparer.Ordinal).ToArray()));
        }
        if (lines.Count == 0) throw SupplierUblErrors.Error("lines_required");
        var global = Allowances(root, currency); var taxTotal = Taxes(root, currency, issues, warnings); var legal = RequireElement(root, Cac + "LegalMonetaryTotal");
        var subtotal = Money(RequireElement(legal, Cbc + "LineExtensionAmount"), currency); var inclusive = Money(RequireElement(legal, Cbc + "TaxInclusiveAmount"), currency);
        var total = Money(RequireElement(legal, Cbc + "PayableAmount"), currency); var exclusiveElement = OptionalElement(legal, Cbc + "TaxExclusiveAmount");
        var exclusive = exclusiveElement is null ? (decimal?)null : Money(exclusiveElement, currency);
        var prepaid = Money(OptionalElement(legal, Cbc + "PrepaidAmount"), currency, true); var rounding = Money(OptionalElement(legal, Cbc + "PayableRoundingAmount"), currency, true, true);
        var declaredDiscount = Money(OptionalElement(legal, Cbc + "AllowanceTotalAmount"), currency, true); var declaredCharges = Money(OptionalElement(legal, Cbc + "ChargeTotalAmount"), currency, true);
        if (Math.Abs(subtotal - lines.Sum(v => v.LineAmount)) > SupplierUblRules.MoneyTolerance || Math.Abs(global.Discounts - declaredDiscount) > SupplierUblRules.MoneyTolerance || Math.Abs(global.Charges - declaredCharges) > SupplierUblRules.MoneyTolerance) issues.Add("subtotal_incoherent");
        if (exclusive.HasValue && Math.Abs(exclusive.Value - (subtotal - global.Discounts + global.Charges)) > SupplierUblRules.MoneyTolerance) issues.Add("subtotal_incoherent");
        if (taxTotal.SingleBasis.HasValue && Math.Abs(taxTotal.SingleBasis.Value - (subtotal - global.Discounts + global.Charges)) > SupplierUblRules.MoneyTolerance) issues.Add("tax_incoherent");
        if (Math.Abs(taxTotal.Amount - lines.Sum(v => v.Tax)) > SupplierUblRules.MoneyTolerance) issues.Add("tax_incoherent");
        if (Math.Abs(inclusive - (subtotal - global.Discounts + global.Charges + taxTotal.Amount)) > SupplierUblRules.MoneyTolerance || Math.Abs(total - (inclusive - prepaid + rounding)) > SupplierUblRules.MoneyTolerance || Math.Abs(rounding) > SupplierUblRules.MoneyTolerance) issues.Add("total_incoherent");
        if (currency != "PEN") issues.Add("currency_unsupported");
        return new(version, customization, type, originalId, parts[0], serial.ToString(CultureInfo.InvariantCulture), date, currency,
            supplier.Ruc, supplier.Name, buyer.Ruc, buyer.Name, subtotal, global.Discounts, global.Charges, taxTotal.Amount, total, inclusive,
            exclusive, prepaid, rounding, lines, issues.Distinct(StringComparer.Ordinal).ToArray(), warnings.Distinct(StringComparer.Ordinal).ToArray());
    }
}
