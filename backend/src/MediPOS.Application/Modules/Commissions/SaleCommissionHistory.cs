using MediPOS.Domain.Modules.Commissions;
using MediPOS.Domain.Modules.SalesPos;

namespace MediPOS.Application.Modules.Commissions;

public static class SaleCommissionHistory
{
    public static void ValidateOriginals(Sale sale, IReadOnlyList<CommissionEntry> originals)
    {
        if ((sale.CommissionEntryCount ?? 0) != originals.Count ||
            originals.Select(entry => entry.Id).Distinct().Count() != originals.Count ||
            originals.Select(entry => entry.SaleLineId).Distinct().Count() != originals.Count)
            throw new InvalidOperationException("The original commission ledger must match the immutable confirmation count.");
        foreach (var original in originals) original.ValidateOriginal(sale);
    }

    public static void ValidateReversals(Sale sale, IReadOnlyList<CommissionEntry> originals, IReadOnlyList<CommissionEntry> reversals)
    {
        ValidateOriginals(sale, originals);
        if (reversals.Count != originals.Count || reversals.Select(entry => entry.Id).Distinct().Count() != reversals.Count ||
            reversals.Select(entry => entry.ReversesCommissionEntryId).Distinct().Count() != reversals.Count)
            throw new InvalidOperationException("Every original commission requires exactly one compensating entry.");
        var byId = originals.ToDictionary(entry => entry.Id);
        foreach (var reversal in reversals)
        {
            if (!reversal.ReversesCommissionEntryId.HasValue || !byId.TryGetValue(reversal.ReversesCommissionEntryId.Value, out var original))
                throw new InvalidOperationException("A reversal must reference an original earned entry of this sale.");
            reversal.ValidateReversal(sale, original);
            if (reversal.OccurredAt != sale.VoidedAt) throw new InvalidOperationException("Commission compensation must share the void timestamp.");
        }
    }
}
