using MediPOS.Domain.Modules.Cash;

namespace MediPOS.Application.Modules.Reporting.GetOwnerBranchOverview;

// Financial metrics use the requested period; operational metrics use the current consistent snapshot.
public sealed record BranchOverview(Guid BranchId, string BranchName, bool IsMainHub, decimal NetSalesAmount,
    long ConfirmedSaleCount, long VoidedSaleCount, long OpenCashSessionCount, long ProductsWithAvailableStockCount,
    long ExpiringLotCount, long ExpiredLotCount, long PendingProductTransfersBeforeDispatchInCount,
    long InTransitProductTransfersInCount, long PendingProductTransfersBeforeDispatchOutCount, long PendingCashTransfersInCount);
public sealed record ConsolidatedTotals(decimal NetSalesAmount, long ConfirmedSaleCount, long VoidedSaleCount, long OpenCashSessionCount,
    long BranchProductAvailabilityCount, long ExpiringLotCount, long ExpiredLotCount, long PendingProductTransfersBeforeDispatchInCount,
    long InTransitProductTransfersInCount, long PendingProductTransfersBeforeDispatchOutCount, long PendingCashTransfersInCount);
public sealed record OwnerBranchOverview(Guid TenantId, DateTimeOffset PeriodStartUtc, DateTimeOffset PeriodEndExclusiveUtc,
    DateTimeOffset GeneratedAtUtc, IReadOnlyList<BranchOverview> Branches, ConsolidatedTotals ConsolidatedTotals)
{
    public static OwnerBranchOverview From(OwnerOverviewReadRequest request, IReadOnlyList<BranchOverview> branches, DateTimeOffset generatedAt)
    {
        if (branches.Any(b => b.NetSalesAmount > CashSession.MaximumReconciliationAmount))
            throw new OverflowException("Branch monetary aggregate exceeds numeric(28,4).");
        if (branches.Any(b => b.BranchId == Guid.Empty || string.IsNullOrWhiteSpace(b.BranchName) || !CashSession.IsValidReconciliationAmount(b.NetSalesAmount) ||
                b.ConfirmedSaleCount < 0 || b.VoidedSaleCount < 0 || b.OpenCashSessionCount < 0 || b.ProductsWithAvailableStockCount < 0 ||
                b.ExpiringLotCount < 0 || b.ExpiredLotCount < 0 || b.PendingProductTransfersBeforeDispatchInCount < 0 || b.InTransitProductTransfersInCount < 0 ||
                b.PendingProductTransfersBeforeDispatchOutCount < 0 || b.PendingCashTransfersInCount < 0 ||
                (request.BranchId.HasValue && b.BranchId != request.BranchId)) || branches.Select(b => b.BranchId).Distinct().Count() != branches.Count ||
            (request.BranchId.HasValue && branches.Count != 1)) throw new ArgumentException("Overview rows must match the selected branches with valid exact metrics.");
        var money = 0m;
        long confirmed = 0, voided = 0, open = 0, products = 0, expiring = 0, expired = 0, incoming = 0, transit = 0, outgoing = 0, cash = 0;
        foreach (var b in branches)
        {
            money = checked(money + b.NetSalesAmount);
            if (!CashSession.IsValidReconciliationAmount(money)) throw new OverflowException("Consolidated money exceeds the existing numeric(28,4) aggregate range.");
            confirmed = checked(confirmed + b.ConfirmedSaleCount); voided = checked(voided + b.VoidedSaleCount); open = checked(open + b.OpenCashSessionCount);
            products = checked(products + b.ProductsWithAvailableStockCount); expiring = checked(expiring + b.ExpiringLotCount); expired = checked(expired + b.ExpiredLotCount);
            incoming = checked(incoming + b.PendingProductTransfersBeforeDispatchInCount); transit = checked(transit + b.InTransitProductTransfersInCount);
            outgoing = checked(outgoing + b.PendingProductTransfersBeforeDispatchOutCount); cash = checked(cash + b.PendingCashTransfersInCount);
        }
        return new(request.TenantId, request.PeriodStartUtc, request.PeriodEndExclusiveUtc, generatedAt.ToUniversalTime(),
            Array.AsReadOnly(branches.OrderBy(b => b.BranchName, StringComparer.Ordinal).ThenBy(b => b.BranchId).ToArray()),
            new(money, confirmed, voided, open, products, expiring, expired, incoming, transit, outgoing, cash));
    }
}
public sealed record OwnerOverviewReadRequest(Guid TenantId, Guid? BranchId, DateTimeOffset PeriodStartUtc, DateTimeOffset PeriodEndExclusiveUtc, DateOnly TodayLocal);
public interface IOwnerBranchOverviewReader
{
    Task<IReadOnlyList<BranchOverview>> ReadAsync(OwnerOverviewReadRequest request, CancellationToken cancellationToken);
}
