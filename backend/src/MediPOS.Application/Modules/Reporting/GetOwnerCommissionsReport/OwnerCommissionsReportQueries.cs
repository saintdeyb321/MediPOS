using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Commissions;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.SalesPos;

namespace MediPOS.Application.Modules.Reporting.GetOwnerCommissionsReport;

public static class OwnerCommissionsReportQueries
{
    public static IQueryable<CommissionReportRow> Rows(IQueryable<CommissionEntry> entries, IQueryable<Sale> sales,
        IQueryable<Membership> memberships, IQueryable<User> users, IQueryable<BusinessProduct> products, OwnerCommissionsReadRequest request) =>
        from earned in entries
        where earned.TenantId == request.TenantId && earned.EntryType == CommissionEntryType.Earned &&
            (!request.SellerMembershipId.HasValue || earned.SellerMembershipId == request.SellerMembershipId) &&
            (!request.BusinessProductId.HasValue || earned.BusinessProductId == request.BusinessProductId)
        join sale in sales on new { earned.TenantId, Id = earned.SaleId } equals new { sale.TenantId, sale.Id }
        where sale.TenantId == request.TenantId && (sale.Status == SaleStatus.Confirmed || sale.Status == SaleStatus.Voided) &&
            sale.ConfirmedAt >= request.PeriodStartUtc && sale.ConfirmedAt < request.PeriodEndExclusiveUtc &&
            (!request.BranchId.HasValue || sale.BranchId == request.BranchId)
        join reversal in entries.Where(entry => entry.TenantId == request.TenantId && entry.EntryType == CommissionEntryType.Reversal)
            on new { earned.TenantId, Id = (Guid?)earned.Id } equals new { reversal.TenantId, Id = reversal.ReversesCommissionEntryId } into compensations
        from reversal in compensations.DefaultIfEmpty()
        join member in memberships.Where(member => member.TenantId == request.TenantId)
            on new { earned.TenantId, Id = earned.SellerMembershipId } equals new { member.TenantId, member.Id } into sellers
        from member in sellers.DefaultIfEmpty()
        join user in users on member.UserId equals user.Id into identities
        from user in identities.DefaultIfEmpty()
        join product in products.Where(product => product.TenantId == request.TenantId)
            on new { earned.TenantId, Id = earned.BusinessProductId } equals new { product.TenantId, product.Id } into catalogue
        from product in catalogue.DefaultIfEmpty()
        select new CommissionReportRow
        {
            CommissionEntryId = earned.Id,
            SaleId = sale.Id,
            SaleLineId = earned.SaleLineId,
            BranchId = sale.BranchId,
            SellerMembershipId = earned.SellerMembershipId,
            SellerName = user == null ? null : user.DisplayName,
            BusinessProductId = earned.BusinessProductId,
            ProductName = product == null ? null : product.Name,
            ConfirmedAtUtc = sale.ConfirmedAt!.Value,
            OriginalEarnedAmount = earned.Amount,
            CompensatedAmount = reversal == null ? 0m : -reversal.Amount,
            CurrentNetAmount = sale.Status == SaleStatus.Confirmed ? earned.Amount : 0m,
            ReversedAtUtc = reversal == null ? null : reversal.OccurredAt,
        };

    public static IQueryable<CommissionReportTotals> Totals(IQueryable<CommissionReportRow> rows) => rows.GroupBy(_ => 1)
        .Select(group => new CommissionReportTotals(group.LongCount(), group.Sum(row => row.OriginalEarnedAmount),
            group.Sum(row => row.CompensatedAmount), group.Sum(row => row.CurrentNetAmount)));
}
