using System.Collections.ObjectModel;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Reporting.ExportOwnerReport;
using MediPOS.Application.Modules.Reporting.GetOwnerCommissionsReport;
using MediPOS.Application.Modules.Reporting.Operational;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.Reporting.Persistence;

internal sealed class ReportExportReader(MediPosDbContext context) : IReportExportReader
{
    public Task<ReportExportSnapshot> ReadAsync(ReportExportReadRequest request, CancellationToken token) =>
        OperationalReportSnapshot.ReadAsync(context, request.Scope, async () =>
        {
            var data = request.Query.ReportType switch
            {
                OwnerReportType.Sales => await SalesAsync(request, token).ConfigureAwait(false),
                OwnerReportType.Commissions => await CommissionsAsync(request, token).ConfigureAwait(false),
                OwnerReportType.StockRisk => await StockAsync(request, token).ConfigureAwait(false),
                OwnerReportType.Expirations => await ExpirationsAsync(request, token).ConfigureAwait(false),
                OwnerReportType.InventoryCapital => await CapitalAsync(request, token).ConfigureAwait(false),
                OwnerReportType.ProductRotation => await RotationAsync(request, token).ConfigureAwait(false),
                _ => throw new ApplicationErrorException(ApplicationErrors.InvalidRequest),
            };
            // Metadata is read inside the same snapshot, after bounded rows; never enumerate the complete catalogue.
            var metadata = await MetadataAsync(request, data, token).ConfigureAwait(false);
            var snapshot = new ReportExportSnapshot(data, metadata);
            ReportExportPolicy.Validate(snapshot, request);
            return snapshot;
        }, token);

    private async Task<ReportExportData> SalesAsync(ReportExportReadRequest r, CancellationToken token)
    {
        var request = ReportExportPolicy.SalesRequest(r);
        var groups = OperationalSalesQueries.Groups(context.Sales.AsNoTracking(), context.SaleLines.AsNoTracking(), context.BusinessProducts.AsNoTracking(),
            context.Branches.AsNoTracking(), context.Memberships.AsNoTracking(), context.Users.AsNoTracking(), context.Categories.AsNoTracking(), request);
        var count = await groups.LongCountAsync(token).ConfigureAwait(false);
        ReportExportLimits.RequireRows(count);
        var totals = await OperationalSalesQueries.Totals(context.Sales.AsNoTracking(), context.SaleLines.AsNoTracking(), context.BusinessProducts.AsNoTracking(), request)
            .SingleOrDefaultAsync(token).ConfigureAwait(false);
        var rows = await BoundedAsync(OperationalSalesQueries.Order(groups, request.Dimension, request.Sort), token).ConfigureAwait(false);
        return new SalesExportData(new(rows, new(count, totals?.DistinctSaleCount ?? 0, totals?.SalesAmount ?? 0m)));
    }
    private async Task<ReportExportData> CommissionsAsync(ReportExportReadRequest r, CancellationToken token)
    {
        var query = OwnerCommissionsReportQueries.Rows(context.CommissionEntries.AsNoTracking(), context.Sales.AsNoTracking(), context.Memberships.AsNoTracking(),
            context.Users.AsNoTracking(), context.BusinessProducts.AsNoTracking(), ReportExportPolicy.CommissionRequest(r));
        var totals = await OwnerCommissionsReportQueries.Totals(query).SingleOrDefaultAsync(token).ConfigureAwait(false) ?? new(0, 0m, 0m, 0m);
        ReportExportLimits.RequireRows(totals.EarnedEntryCount);
        var rows = await BoundedAsync(query.OrderByDescending(row => row.ConfirmedAtUtc).ThenBy(row => row.SaleId).ThenBy(row => row.SaleLineId)
            .ThenBy(row => row.CommissionEntryId), token).ConfigureAwait(false);
        return new CommissionsExportData(new(rows, totals));
    }
    private async Task<ReportExportData> StockAsync(ReportExportReadRequest r, CancellationToken token)
    {
        var query = OperationalInventoryQueries.Risk(context.Branches.AsNoTracking(), context.BusinessProducts.AsNoTracking(), context.InventoryLots.AsNoTracking(),
            context.BranchProductStockThresholds.AsNoTracking(), ReportExportPolicy.StockRequest(r));
        var totals = await query.GroupBy(_ => 1).Select(group => new { Count = group.LongCount(), Critical = group.LongCount(row => row.IsCritical) })
            .SingleOrDefaultAsync(token).ConfigureAwait(false);
        ReportExportLimits.RequireRows(totals?.Count ?? 0);
        var rows = await BoundedAsync(query.OrderBy(row => row.ProductName).ThenBy(row => row.BranchId).ThenBy(row => row.BusinessProductId), token).ConfigureAwait(false);
        return new StockRiskExportData(new(rows, new(totals?.Count ?? 0, totals?.Critical ?? 0)));
    }
    private async Task<ReportExportData> ExpirationsAsync(ReportExportReadRequest r, CancellationToken token)
    {
        var query = OperationalInventoryQueries.Expirations(context.InventoryLots.AsNoTracking(), context.BusinessProducts.AsNoTracking(), ReportExportPolicy.ExpirationRequest(r));
        var totals = await query.GroupBy(_ => 1).Select(group => new
        {
            Count = group.LongCount(),
            Expired = group.LongCount(row => row.State == OwnerExpirationState.Expired),
            Expiring = group.LongCount(row => row.State == OwnerExpirationState.Expiring),
            Later = group.LongCount(row => row.State == OwnerExpirationState.Later),
        }).SingleOrDefaultAsync(token).ConfigureAwait(false);
        ReportExportLimits.RequireRows(totals?.Count ?? 0);
        var rows = await BoundedAsync(query.OrderBy(row => row.ExpirationDate).ThenBy(row => row.BranchId).ThenBy(row => row.InventoryLotId), token).ConfigureAwait(false);
        return new ExpirationsExportData(new(rows, new(totals?.Count ?? 0, totals?.Expired ?? 0, totals?.Expiring ?? 0, totals?.Later ?? 0)));
    }
    private async Task<ReportExportData> CapitalAsync(ReportExportReadRequest r, CancellationToken token)
    {
        var scope = r.Scope;
        var branches = await BoundedAsync(context.Branches.AsNoTracking().Where(branch => branch.TenantId == scope.TenantId &&
            (!scope.BranchId.HasValue || branch.Id == scope.BranchId)).OrderBy(branch => branch.Id).Select(branch => branch.Id), token).ConfigureAwait(false);
        var values = await OperationalInventoryQueries.CapitalBranches(OperationalInventoryQueries.CapitalLots(context.InventoryLots.AsNoTracking(),
            context.PurchaseLines.AsNoTracking(), context.BusinessProducts.AsNoTracking(), scope)).ToDictionaryAsync(row => row.BranchId, token).ConfigureAwait(false);
        var rows = branches.Select(id => values.GetValueOrDefault(id) ?? new OwnerBranchInventoryCapital { BranchId = id }).ToArray();
        // The existing capital reader's final branch sums: per-lot SQL ToEven4 values, no second rounding.
        return new CapitalExportData(new(Array.AsReadOnly(rows), new(rows.Sum(row => row.TotalInventoryCapital),
            rows.Sum(row => row.ExpiredInventoryCapital), rows.Sum(row => row.ExpiringInventoryCapital))));
    }
    private async Task<ReportExportData> RotationAsync(ReportExportReadRequest r, CancellationToken token)
    {
        var products = context.BusinessProducts.AsNoTracking();
        var lines = OperationalSalesQueries.Lines(context.Sales.AsNoTracking(), context.SaleLines.AsNoTracking(), products, r.Scope, r.Period!);
        var query = OperationalInventoryQueries.Rotation(products, lines, context.InventoryLots.AsNoTracking(), ReportExportPolicy.RotationRequest(r));
        var totals = await query.GroupBy(_ => 1).Select(group => new { Count = group.LongCount(), Amount = group.Sum(row => row.SalesAmount) })
            .SingleOrDefaultAsync(token).ConfigureAwait(false);
        ReportExportLimits.RequireRows(totals?.Count ?? 0);
        var rows = await BoundedAsync(OperationalInventoryQueries.OrderRotation(query, r.Query.RotationMode!.Value), token).ConfigureAwait(false);
        return new RotationExportData(new(rows, new(totals?.Count ?? 0, totals?.Amount ?? 0m)));
    }

    private async Task<ReportExportMetadata> MetadataAsync(ReportExportReadRequest r, ReportExportData data, CancellationToken token)
    {
        var q = r.Query;
        var name = await context.Tenants.AsNoTracking().Where(tenant => tenant.Id == r.Scope.TenantId).Select(tenant => tenant.TradingName)
            .SingleAsync(token).ConfigureAwait(false);
        var branchRows = await BoundedAsync(context.Branches.AsNoTracking().Where(branch => branch.TenantId == r.Scope.TenantId &&
            (!r.Scope.BranchId.HasValue || branch.Id == r.Scope.BranchId)).OrderBy(branch => branch.Id).Select(branch => new { branch.Id, branch.Name }), token).ConfigureAwait(false);
        var branches = branchRows.ToDictionary(branch => branch.Id, branch => branch.Name);
        if (r.Scope.BranchId.HasValue && !branches.ContainsKey(r.Scope.BranchId.Value)) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        string? employee = null; string? product = null; string? category = null;
        if (q.EmployeeMembershipId is { } employeeId)
        {
            employee = await (from member in context.Memberships.AsNoTracking()
                              where member.TenantId == r.Scope.TenantId && member.Id == employeeId
                              join user in context.Users.AsNoTracking() on member.UserId equals user.Id
                              select user.DisplayName).SingleOrDefaultAsync(token).ConfigureAwait(false);
            if (employee is null) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        }
        if (q.BusinessProductId is { } productId)
        {
            product = await context.BusinessProducts.AsNoTracking().Where(p => p.TenantId == r.Scope.TenantId && p.Id == productId).Select(p => p.Name)
                .SingleOrDefaultAsync(token).ConfigureAwait(false);
            if (product is null) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        }
        if (q.CategoryId is { } categoryId)
        {
            category = await context.Categories.AsNoTracking().Where(c => c.Id == categoryId).Select(c => c.Name).SingleOrDefaultAsync(token).ConfigureAwait(false);
            if (category is null) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        }
        var categoryIds = data is RotationExportData rotation ? rotation.Page.Rows.Select(row => row.CategoryId).Distinct().ToArray() : [];
        var categories = categoryIds.Length == 0 ? new Dictionary<Guid, string>() : await context.Categories.AsNoTracking().Where(c => categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, token).ConfigureAwait(false);
        return new(name, new ReadOnlyDictionary<Guid, string>(branches), new ReadOnlyDictionary<Guid, string>(categories), employee, product, category);
    }

    private static async Task<IReadOnlyList<T>> BoundedAsync<T>(IQueryable<T> query, CancellationToken token)
    {
        var rows = await query.Take(ReportExportLimits.MaximumRows + 1).ToArrayAsync(token).ConfigureAwait(false);
        ReportExportLimits.RequireRows(rows.Length);
        return Array.AsReadOnly(rows);
    }
}
