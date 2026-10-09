using MediPOS.Domain.Modules.Cash;

namespace MediPOS.Application.Modules.Reporting.GetOwnerCommissionsReport;

public sealed record CommissionReportRow(Guid CommissionEntryId, Guid SaleId, Guid SaleLineId,
    Guid BranchId, Guid SellerMembershipId, string? SellerName, Guid BusinessProductId, string? ProductName,
    DateTimeOffset ConfirmedAtUtc, decimal OriginalEarnedAmount, decimal CompensatedAmount, decimal CurrentNetAmount,
    DateTimeOffset? ReversedAtUtc)
{
    public CommissionReportRow() : this(Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, null,
        Guid.Empty, null, default, 0m, 0m, 0m, null) { }
}

public sealed record CommissionReportTotals(long EarnedEntryCount, decimal OriginalEarnedAmount,
    decimal CompensatedAmount, decimal CurrentNetAmount);

public sealed record OwnerCommissionsReport(Guid TenantId, DateTimeOffset PeriodStartUtc, DateTimeOffset PeriodEndExclusiveUtc,
    DateTimeOffset GeneratedAtUtc, int Offset, int Limit, IReadOnlyList<CommissionReportRow> Rows, CommissionReportTotals Totals)
{
    public const int MaximumOffset = 10_000;
    public const int MaximumLimit = 100;
    public string PeriodBasis { get; } = "original_sale_confirmation";
    public string CompensationBasis { get; } = "linked_to_original_sale_regardless_of_reversal_date";

    public static OwnerCommissionsReport From(OwnerCommissionsReadRequest request, CommissionReportPage page, DateTimeOffset generatedAt)
    {
        var totals = page.Totals;
        if (totals.OriginalEarnedAmount > CashSession.MaximumReconciliationAmount || totals.CompensatedAmount > CashSession.MaximumReconciliationAmount ||
            totals.CurrentNetAmount > CashSession.MaximumReconciliationAmount || page.Rows.Any(row => row.OriginalEarnedAmount > CashSession.MaximumReconciliationAmount ||
                row.CompensatedAmount > CashSession.MaximumReconciliationAmount || row.CurrentNetAmount > CashSession.MaximumReconciliationAmount))
            throw new OverflowException("Commission report amounts exceed numeric(28,4).");
        static bool Money(decimal value) => CashSession.IsValidReconciliationAmount(value);
        if (totals.EarnedEntryCount < 0 || totals.EarnedEntryCount < page.Rows.Count || page.Rows.Count > request.Limit ||
            page.Rows.Select(row => row.CommissionEntryId).Distinct().Count() != page.Rows.Count ||
            !Money(totals.OriginalEarnedAmount) || !Money(totals.CompensatedAmount) || !Money(totals.CurrentNetAmount) ||
            totals.OriginalEarnedAmount - totals.CompensatedAmount != totals.CurrentNetAmount ||
            page.Rows.Any(row => row.CommissionEntryId == Guid.Empty || row.SaleId == Guid.Empty || row.SaleLineId == Guid.Empty ||
                row.BranchId == Guid.Empty || row.SellerMembershipId == Guid.Empty || row.BusinessProductId == Guid.Empty ||
                row.ConfirmedAtUtc.Offset != TimeSpan.Zero || row.ConfirmedAtUtc < request.PeriodStartUtc || row.ConfirmedAtUtc >= request.PeriodEndExclusiveUtc ||
                (request.BranchId.HasValue && row.BranchId != request.BranchId) ||
                (request.SellerMembershipId.HasValue && row.SellerMembershipId != request.SellerMembershipId) ||
                (request.BusinessProductId.HasValue && row.BusinessProductId != request.BusinessProductId) ||
                row.OriginalEarnedAmount <= 0 || !Money(row.OriginalEarnedAmount) || !Money(row.CompensatedAmount) || !Money(row.CurrentNetAmount) ||
                row.OriginalEarnedAmount - row.CompensatedAmount != row.CurrentNetAmount ||
                (row.CompensatedAmount > 0 && (!row.ReversedAtUtc.HasValue || row.ReversedAtUtc.Value.Offset != TimeSpan.Zero || row.ReversedAtUtc < row.ConfirmedAtUtc))))
            throw new ArgumentException("Commission report history or aggregates are inconsistent.");
        return new(request.TenantId, request.PeriodStartUtc, request.PeriodEndExclusiveUtc, generatedAt.ToUniversalTime(),
            request.Offset, request.Limit, Array.AsReadOnly(page.Rows.ToArray()), totals);
    }
}

public sealed record OwnerCommissionsReadRequest(Guid TenantId, Guid? BranchId, Guid? SellerMembershipId, Guid? BusinessProductId,
    DateTimeOffset PeriodStartUtc, DateTimeOffset PeriodEndExclusiveUtc, int Offset, int Limit);
public sealed record CommissionReportPage(IReadOnlyList<CommissionReportRow> Rows, CommissionReportTotals Totals);
public interface IOwnerCommissionsReportReader
{
    Task<CommissionReportPage> ReadAsync(OwnerCommissionsReadRequest request, CancellationToken cancellationToken);
}
