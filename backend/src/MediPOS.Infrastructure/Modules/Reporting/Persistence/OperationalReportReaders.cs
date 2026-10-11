using System.Data;
using MediPOS.Application.Modules.Reporting.Operational;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediPOS.Infrastructure.Modules.Reporting.Persistence;

// One consistent, read-only snapshot for each report's bounded set of SQL aggregates and page queries.
internal static class OperationalReportSnapshot
{
    internal static async Task<T> ReadAsync<T>(MediPosDbContext context, OperationalReadScope scope, Func<Task<T>> read, CancellationToken token)
    {
        context.SelectTenant(scope.TenantId);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token).ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", token).ConfigureAwait(false);
        try
        {
            var result = await read().ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
            return result;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.NumericValueOutOfRange)
        {
            throw new OverflowException("A PostgreSQL report aggregate exceeds its supported range.", exception);
        }
    }
}

internal sealed class OwnerSalesReportReader(MediPosDbContext context) : IOwnerSalesReportReader
{
    public Task<SalesReportPage> ReadAsync(SalesReportReadRequest request, CancellationToken token) =>
        OperationalReportSnapshot.ReadAsync(context, request.Scope, async () =>
        {
            var sales = context.Sales.AsNoTracking();
            var lines = context.SaleLines.AsNoTracking();
            var products = context.BusinessProducts.AsNoTracking();
            var groups = OperationalSalesQueries.Groups(sales, lines, products, context.Branches.AsNoTracking(),
                context.Memberships.AsNoTracking(), context.Users.AsNoTracking(), context.Categories.AsNoTracking(), request);
            var totalsQuery = OperationalSalesQueries.Totals(sales, lines, products, request);
            var totals = await totalsQuery.SingleOrDefaultAsync(token).ConfigureAwait(false);
            var count = await groups.LongCountAsync(token).ConfigureAwait(false);
            var rows = await OperationalSalesQueries.Order(groups, request.Dimension, request.Sort).Skip(request.Offset).Take(request.Limit)
                .ToArrayAsync(token).ConfigureAwait(false);
            return new SalesReportPage(Array.AsReadOnly(rows), new(count, totals?.DistinctSaleCount ?? 0, totals?.SalesAmount ?? 0m));
        }, token);
}

internal sealed class OwnerInventoryRiskReader(MediPosDbContext context) : IOwnerInventoryRiskReader
{
    public Task<StockRiskPage> ReadStockAsync(StockRiskReadRequest request, CancellationToken token) =>
        OperationalReportSnapshot.ReadAsync(context, request.Scope, async () =>
        {
            var query = OperationalInventoryQueries.Risk(context.Branches.AsNoTracking(), context.BusinessProducts.AsNoTracking(),
                context.InventoryLots.AsNoTracking(), context.BranchProductStockThresholds.AsNoTracking(), request);
            var totals = await query.GroupBy(_ => 1).Select(group => new
            { Count = group.LongCount(), Critical = group.LongCount(row => row.IsCritical) }).SingleOrDefaultAsync(token).ConfigureAwait(false);
            var rows = await query.OrderBy(row => row.ProductName).ThenBy(row => row.BranchId).ThenBy(row => row.BusinessProductId)
                .Skip(request.Offset).Take(request.Limit).ToArrayAsync(token).ConfigureAwait(false);
            return new StockRiskPage(Array.AsReadOnly(rows), new(totals?.Count ?? 0, totals?.Critical ?? 0));
        }, token);

    public Task<ExpirationPage> ReadExpirationsAsync(ExpirationReadRequest request, CancellationToken token) =>
        OperationalReportSnapshot.ReadAsync(context, request.Scope, async () =>
        {
            var query = OperationalInventoryQueries.Expirations(context.InventoryLots.AsNoTracking(), context.BusinessProducts.AsNoTracking(), request);
            var totals = await query.GroupBy(_ => 1).Select(group => new
            {
                Count = group.LongCount(),
                Expired = group.LongCount(row => row.State == OwnerExpirationState.Expired),
                Expiring = group.LongCount(row => row.State == OwnerExpirationState.Expiring),
                Later = group.LongCount(row => row.State == OwnerExpirationState.Later),
            }).SingleOrDefaultAsync(token).ConfigureAwait(false);
            var rows = await query.OrderBy(row => row.ExpirationDate).ThenBy(row => row.BranchId).ThenBy(row => row.InventoryLotId)
                .Skip(request.Offset).Take(request.Limit).ToArrayAsync(token).ConfigureAwait(false);
            return new ExpirationPage(Array.AsReadOnly(rows), new(totals?.Count ?? 0, totals?.Expired ?? 0, totals?.Expiring ?? 0, totals?.Later ?? 0));
        }, token);

    public Task<InventoryCapitalSnapshot> ReadCapitalAsync(OperationalReadScope scope, CancellationToken token) =>
        OperationalReportSnapshot.ReadAsync(context, scope, async () =>
        {
            // Only branch identifiers and SQL aggregates are materialized; no lots or purchase entities are loaded.
            var branches = await context.Branches.AsNoTracking().Where(branch => branch.TenantId == scope.TenantId &&
                (!scope.BranchId.HasValue || branch.Id == scope.BranchId)).OrderBy(branch => branch.Id).Select(branch => branch.Id)
                .ToArrayAsync(token).ConfigureAwait(false);
            var values = await OperationalInventoryQueries.CapitalBranches(OperationalInventoryQueries.CapitalLots(context.InventoryLots.AsNoTracking(),
                context.PurchaseLines.AsNoTracking(), context.BusinessProducts.AsNoTracking(), scope)).ToDictionaryAsync(row => row.BranchId, token).ConfigureAwait(false);
            var rows = branches.Select(id => values.GetValueOrDefault(id) ?? new OwnerBranchInventoryCapital { BranchId = id }).ToArray();
            return new InventoryCapitalSnapshot(Array.AsReadOnly(rows), new(rows.Sum(row => row.TotalInventoryCapital),
                rows.Sum(row => row.ExpiredInventoryCapital), rows.Sum(row => row.ExpiringInventoryCapital)));
        }, token);
}

internal sealed class OwnerProductRotationReader(MediPosDbContext context) : IOwnerProductRotationReader
{
    public Task<ProductRotationPage> ReadAsync(ProductRotationReadRequest request, CancellationToken token) =>
        OperationalReportSnapshot.ReadAsync(context, request.Scope, async () =>
        {
            var products = context.BusinessProducts.AsNoTracking();
            var lines = OperationalSalesQueries.Lines(context.Sales.AsNoTracking(), context.SaleLines.AsNoTracking(), products, request.Scope, request.Period);
            var query = OperationalInventoryQueries.Rotation(products, lines, context.InventoryLots.AsNoTracking(), request);
            var totals = await query.GroupBy(_ => 1).Select(group => new { Count = group.LongCount(), Amount = group.Sum(row => row.SalesAmount) })
                .SingleOrDefaultAsync(token).ConfigureAwait(false);
            var rows = await OperationalInventoryQueries.OrderRotation(query, request.Mode).Skip(request.Offset).Take(request.Limit).ToArrayAsync(token).ConfigureAwait(false);
            return new ProductRotationPage(Array.AsReadOnly(rows), new(totals?.Count ?? 0, totals?.Amount ?? 0m));
        }, token);
}

internal sealed class OwnerOperationalDashboardReader(MediPosDbContext context) : IOwnerOperationalDashboardReader
{
    public Task<DashboardMetrics> ReadAsync(OperationalReadScope scope, OperationalReportPeriod period, CancellationToken token) =>
        OperationalReportSnapshot.ReadAsync(context, scope, async () =>
        {
            var sales = await OperationalSalesQueries.DashboardSales(context.Sales.AsNoTracking(), scope, period).SingleOrDefaultAsync(token).ConfigureAwait(false);
            var openCash = await context.CashSessions.AsNoTracking().LongCountAsync(session => session.TenantId == scope.TenantId &&
                (!scope.BranchId.HasValue || session.BranchId == scope.BranchId) && session.Status == CashSessionStatus.Open, token).ConfigureAwait(false);
            var critical = await OperationalInventoryQueries.Risk(context.Branches.AsNoTracking(), context.BusinessProducts.AsNoTracking(),
                context.InventoryLots.AsNoTracking(), context.BranchProductStockThresholds.AsNoTracking(), new(scope, null, true, 0, 1)).LongCountAsync(token).ConfigureAwait(false);
            var expiry = await OperationalInventoryQueries.Expirations(context.InventoryLots.AsNoTracking(), context.BusinessProducts.AsNoTracking(),
                new(scope, DateOnly.MinValue, scope.TodayLocal.AddDays(30), 0, 1)).GroupBy(_ => 1).Select(group => new
                { Expired = group.LongCount(row => row.State == OwnerExpirationState.Expired), Expiring = group.LongCount(row => row.State == OwnerExpirationState.Expiring) })
                .SingleOrDefaultAsync(token).ConfigureAwait(false);
            var capital = await OperationalInventoryQueries.CapitalLots(context.InventoryLots.AsNoTracking(), context.PurchaseLines.AsNoTracking(),
                context.BusinessProducts.AsNoTracking(), scope).GroupBy(_ => 1).Select(group => new
                { Total = group.Sum(lot => lot.Value), Expired = group.Sum(lot => lot.IsExpired ? lot.Value : 0m), Expiring = group.Sum(lot => lot.IsExpiring ? lot.Value : 0m) })
                .SingleOrDefaultAsync(token).ConfigureAwait(false);
            return new DashboardMetrics(sales?.NetSalesAmount ?? 0m, sales?.ConfirmedSaleCount ?? 0, sales?.VoidedSaleCount ?? 0, openCash,
                capital?.Total ?? 0m, capital?.Expired ?? 0m, capital?.Expiring ?? 0m, expiry?.Expiring ?? 0, expiry?.Expired ?? 0, critical);
        }, token);
}
