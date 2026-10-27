using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Purchasing.ReplacePurchaseLines;

namespace MediPOS.Application.Modules.Purchasing;

// Opaque precondition for the exact immutable line identities and snapshots reviewed by the caller.
// Replacing even identical inputs creates new line IDs, so an older review cannot become valid again.
public static class PurchaseRevision
{
    public static string For(Guid purchaseId, IEnumerable<PurchaseLineDetails> lines) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            purchaseId,
            lines = lines.OrderBy(line => line.Id).Select(line => new
            {
                line.Id, line.BusinessProductId, line.UnitNameSnapshot,
                quantity = Number(line.Quantity), conversion = Number(line.ConversionToBaseSnapshot),
                baseQuantity = Number(line.BaseQuantity), cost = Number(line.UnitCost), line.BatchNumber, line.ExpirationDate,
            }),
        })));

    public static void Require(Guid purchaseId, IEnumerable<PurchaseLineDetails> lines, string expected)
    {
        if (expected is null || expected.Length != 64 || !expected.All(Uri.IsHexDigit))
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        if (!string.Equals(For(purchaseId, lines), expected, StringComparison.OrdinalIgnoreCase))
            throw new ApplicationErrorException(new("purchasing.receipt_changed", ErrorCategory.Conflict, "The reviewed purchase has changed. Read it again before writing."));
    }

    private static string Number(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);
}
