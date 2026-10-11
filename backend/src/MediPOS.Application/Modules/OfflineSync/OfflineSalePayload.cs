using System.Text.Json;
using System.Text.Json.Serialization;
using MediPOS.Application.Modules.SalesPos.ConfirmSale;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.OfflineSync;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.SharedKernel;

namespace MediPOS.Application.Modules.OfflineSync;

// Version is carried by offline_sale.v1 / schema_version=1. All prices are per base unit, as in SalesPos.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OfflineSalePayload(
    [property: JsonRequired] Guid BranchId, [property: JsonRequired] Guid CashSessionId,
    [property: JsonRequired] IReadOnlyList<OfflineSaleLine> Lines,
    [property: JsonRequired] IReadOnlyList<OfflineSalePayment> Payments,
    [property: JsonRequired] decimal TotalAmountSnapshot);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OfflineSaleLine(
    [property: JsonRequired] Guid BusinessProductId, [property: JsonRequired] Guid ProductUnitId,
    [property: JsonRequired] decimal Quantity, [property: JsonRequired] decimal ConversionToBaseSnapshot,
    [property: JsonRequired] decimal UnitPriceSnapshot, [property: JsonRequired] string PriceKind);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OfflineSalePayment([property: JsonRequired] string Method, [property: JsonRequired] decimal Amount);

public static class OfflineSalePayloadPolicy
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    { PropertyNameCaseInsensitive = false, NumberHandling = JsonNumberHandling.Strict };
    public static OfflineSalePayload Read(string canonicalPayload)
    {
        try
        {
            using var json = JsonDocument.Parse(canonicalPayload);
            RequireExactNumbers(json.RootElement);
            var payload = JsonSerializer.Deserialize<OfflineSalePayload>(canonicalPayload, Options) ?? throw new ArgumentException("Offline sale payload is inconsistent.");
            Validate(payload);
            return payload;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or ArithmeticException or InvalidOperationException or FormatException)
        { throw SyncAccess.Error("sync.sale_payload_invalid"); }
    }

    public static PriceKind Price(string code) => PriceKindCodes.FromCode(code);
    public static IReadOnlyList<SalePaymentInput> PaymentInputs(OfflineSalePayload payload) => payload.Payments
        .Select(payment => new SalePaymentInput(PaymentMethodCodes.FromCode(payment.Method), payment.Amount)).ToArray();

    private static void Validate(OfflineSalePayload payload)
    {
        if (payload.BranchId == Guid.Empty || payload.CashSessionId == Guid.Empty || payload.Lines is null || payload.Payments is null ||
            payload.Lines.Count is 0 or > Sale.MaximumLines || payload.Payments.Count is 0 or > 5 || payload.Payments.Any(payment => payment is null) ||
            payload.TotalAmountSnapshot < 0 || payload.TotalAmountSnapshot > Sale.MaximumAmount || decimal.Round(payload.TotalAmountSnapshot, 4) != payload.TotalAmountSnapshot)
            throw new ArgumentException("Offline sale payload is inconsistent.");
        var keys = new HashSet<(Guid, Guid, string)>();
        var total = 0m;
        foreach (var line in payload.Lines)
        {
            if (line is null || line.BusinessProductId == Guid.Empty || line.ProductUnitId == Guid.Empty ||
                !keys.Add((line.BusinessProductId, line.ProductUnitId, line.PriceKind)) ||
                line.Quantity <= 0 || line.Quantity > SaleLine.MaximumQuantity || decimal.Round(line.Quantity, 12) != line.Quantity ||
                line.ConversionToBaseSnapshot <= 0 || line.ConversionToBaseSnapshot > SaleLine.MaximumQuantity || decimal.Round(line.ConversionToBaseSnapshot, 12) != line.ConversionToBaseSnapshot ||
                line.UnitPriceSnapshot < 0 || line.UnitPriceSnapshot > Sale.MaximumAmount || decimal.Round(line.UnitPriceSnapshot, 4) != line.UnitPriceSnapshot)
                throw new ArgumentException("Offline sale payload is inconsistent.");
            _ = Price(line.PriceKind);
            var quantity = ProductUnit.ConvertExactly(line.Quantity, line.ConversionToBaseSnapshot);
            if (quantity <= 0 || quantity > SaleLine.MaximumQuantity || decimal.Round(quantity, 12) != quantity) throw new ArgumentException("Offline sale payload is inconsistent.");
            total = checked(total + ExactMoney.MultiplyAndRoundToEven4(quantity, line.UnitPriceSnapshot));
        }
        var inputs = PaymentInputs(payload);
        if (inputs.Any(payment => !SalePayment.IsValidAmount(payment.Amount)) || inputs.Select(payment => payment.Method).Distinct().Count() != inputs.Count ||
            total != payload.TotalAmountSnapshot || inputs.Sum(payment => payment.Amount) != total) throw new ArgumentException("Offline sale payload is inconsistent.");
    }

    private static void RequireExactNumbers(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            // System.Text.Json may round decimal underflow/long mantissas; reject every such conversion.
            var number = value.GetDecimal();
            if (SyncPayload.Canonicalize("{\"n\":" + JsonSerializer.Serialize(number) + "}").Json !=
                SyncPayload.Canonicalize("{\"n\":" + value.GetRawText() + "}").Json) throw new ArgumentException("Offline sale payload is inconsistent.");
        }
        else if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject()) RequireExactNumbers(property.Value);
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RequireExactNumbers(item);
    }
}
