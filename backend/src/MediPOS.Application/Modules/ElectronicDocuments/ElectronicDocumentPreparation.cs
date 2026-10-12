using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediPOS.Domain.Modules.ElectronicDocuments;
using MediPOS.Domain.Modules.SalesPos;

namespace MediPOS.Application.Modules.ElectronicDocuments;

public static class ElectronicDocumentPreparation
{
    // Exact documentary input, explicitly supplied. This does not certify tax treatment or emit fiscal XML.
    public static string Fingerprint(RequestElectronicDocumentCommand request)
    {
        var fiscal = request.Fiscal;
        ElectronicDocumentRules.Require(fiscal is not null && fiscal.Customer is not null && fiscal.Lines is not null && fiscal.Lines.Count is > 0 and <= 200 && fiscal.Lines.All(v => v is not null), "fiscal_data_required");
        var json = JsonSerializer.Serialize(new
        {
            request.SeriesId,
            request.Type,
            fiscal!.Profile,
            fiscal.Currency,
            fiscal.Customer,
            lines = fiscal.Lines.OrderBy(v => v.SaleLineId).Select(v => new { v.SaleLineId, net = Number(v.NetAmount), tax = Number(v.TaxAmount), v.TaxClassification })
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
    public static string Snapshot(ElectronicRequestData data, FiscalInput input)
    {
        ArgumentNullException.ThrowIfNull(data); ArgumentNullException.ThrowIfNull(input);
        var sale = data.Sale; var series = data.Series;
        ElectronicDocumentRules.Require(sale.Status == SaleStatus.Confirmed && sale.ConfirmedAt.HasValue, "confirmed_sale_required");
        ElectronicDocumentRules.Require(sale.TenantId == series.TenantId && data.Branch.TenantId == sale.TenantId && data.Branch.Id == sale.BranchId &&
            data.Legal.TenantId == sale.TenantId && data.Legal.Id == data.Branch.LegalEntityId && data.Legal.Id == series.LegalEntityId, "issuer_mismatch");
        ElectronicDocumentRules.Require(input.Profile == ElectronicDocumentRules.Profile && input.Profile == series.Profile && input.Currency == "PEN", "profile_or_currency_unsupported");
        ElectronicDocumentRules.Require(input.Customer is not null && input.Lines is not null && input.Lines.Count is > 0 and <= 200 && input.Lines.All(v => v is not null), "fiscal_data_required");
        var customer = input.Customer!;
        ElectronicDocumentRules.Text(data.Legal.LegalName, 256, "issuer_name_required");
        ElectronicDocumentRules.Text(series.IssuerAddress, 512, "issuer_address_required");
        ElectronicDocumentRules.Require(Digits(data.Legal.Ruc, 11), "issuer_ruc_invalid");
        ElectronicDocumentRules.Text(customer.Name, 256, "customer_name_required"); ElectronicDocumentRules.Text(customer.Address, 512, "customer_address_required");
        ElectronicDocumentRules.Require(customer.IdentityType == "ruc" && Digits(customer.IdentityNumber, 11) || series.DocumentType == ElectronicDocumentTypes.Receipt && customer.IdentityType == "dni" && Digits(customer.IdentityNumber, 8), "customer_identity_invalid");
        ElectronicDocumentRules.Require(input.Lines!.Select(v => v.SaleLineId).Distinct().Count() == input.Lines.Count && input.Lines.Count == sale.Lines.Count && sale.Lines.Count is > 0 and <= 200, "fiscal_lines_invalid");
        var fiscal = input.Lines.ToDictionary(v => v.SaleLineId); decimal sum = 0;
        foreach (var line in sale.Lines)
        {
            line.ValidateSnapshots(sale);
            ElectronicDocumentRules.Require(fiscal.ContainsKey(line.Id), "fiscal_line_missing"); var amounts = fiscal[line.Id];
            ElectronicDocumentRules.Money(amounts.NetAmount); ElectronicDocumentRules.Money(amounts.TaxAmount);
            ElectronicDocumentRules.Text(amounts.TaxClassification, 64, "tax_classification_required");
            ElectronicDocumentRules.Require(amounts.TaxClassification.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'), "tax_classification_invalid");
            ElectronicDocumentRules.Require(amounts.NetAmount + amounts.TaxAmount == line.LineTotal, "fiscal_amount_mismatch");
            sum += line.LineTotal;
        }
        ElectronicDocumentRules.Money(sale.TotalAmount); ElectronicDocumentRules.Require(sum == sale.TotalAmount && sum > 0, "sale_amount_mismatch");
        return JsonSerializer.Serialize(new
        {
            profile = input.Profile,
            issuer = new { data.Legal.Id, data.Legal.LegalName, data.Legal.Ruc, address = series.IssuerAddress },
            customer,
            sale = new
            {
                sale.Id,
                sale.BranchId,
                sale.SellerMembershipId,
                sale.ConfirmedAt,
                lines = sale.Lines.OrderBy(v => v.Id).Select(v => new
                {
                    v.Id,
                    v.BusinessProductId,
                    v.ProductUnitIdSnapshot,
                    v.ProductNameSnapshot,
                    v.Quantity,
                    v.BaseQuantity,
                    v.UnitNameSnapshot,
                    v.ConversionToBaseSnapshot,
                    priceKind = PriceKindCodes.ToCode(v.PriceKind),
                    v.UnitPriceSnapshot,
                    unitPriceBasis = "BASE_UNIT",
                    v.LineTotal,
                    fiscal = fiscal[v.Id]
                })
            },
            monetary = new { input.Currency, total = sale.TotalAmount, net = input.Lines.Sum(v => v.NetAmount), tax = input.Lines.Sum(v => v.TaxAmount) }
        });
    }
    private static bool Digits(string? text, int count) => text is not null && text.Length == count && text.All(char.IsAsciiDigit);
    private static string Number(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);
}
