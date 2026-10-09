using System.Data;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Reporting.GetOwnerBranchOverview;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.Reporting.Persistence;

internal sealed class OwnerBranchOverviewReader(MediPosDbContext context) : IOwnerBranchOverviewReader
{
    public async Task<IReadOnlyList<BranchOverview>> ReadAsync(OwnerOverviewReadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TenantId == Guid.Empty || request.BranchId == Guid.Empty)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);

        context.SelectTenant(request.TenantId);
        // All grouped indicators use the same snapshot, without row locks or reservations.
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken).ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken).ConfigureAwait(false);

        var branches = await context.Branches.AsNoTracking()
            .Where(branch => branch.TenantId == request.TenantId && (!request.BranchId.HasValue || branch.Id == request.BranchId))
            .OrderBy(branch => branch.Name).ThenBy(branch => branch.Id)
            .Select(branch => new { branch.Id, branch.Name, branch.IsMainHub })
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        if (request.BranchId.HasValue && branches.Length == 0)
            throw new ApplicationErrorException(new("access.branch_denied", ErrorCategory.Forbidden, "Operational access was denied."));

        // Aggregate each source once; joining these tables before grouping would multiply counts and amounts.
        var sales = await OwnerOverviewQueries.Sales(context.Sales.AsNoTracking(), request)
            .ToDictionaryAsync(metric => metric.BranchId, cancellationToken).ConfigureAwait(false);
        var openCash = await OwnerOverviewQueries.OpenCash(context.CashSessions.AsNoTracking(), request)
            .ToDictionaryAsync(metric => metric.BranchId, cancellationToken).ConfigureAwait(false);
        var stock = await OwnerOverviewQueries.Stock(context.InventoryLots.AsNoTracking(), context.BusinessProducts.AsNoTracking(), request)
            .ToDictionaryAsync(metric => metric.BranchId, cancellationToken).ConfigureAwait(false);
        var incomingProducts = await OwnerOverviewQueries.IncomingProducts(context.Transfers.AsNoTracking(), request)
            .ToDictionaryAsync(metric => metric.BranchId, cancellationToken).ConfigureAwait(false);
        var outgoingProducts = await OwnerOverviewQueries.OutgoingProducts(context.Transfers.AsNoTracking(), request)
            .ToDictionaryAsync(metric => metric.BranchId, cancellationToken).ConfigureAwait(false);
        var incomingCash = await OwnerOverviewQueries.IncomingCash(context.CashTransfers.AsNoTracking(), request)
            .ToDictionaryAsync(metric => metric.BranchId, cancellationToken).ConfigureAwait(false);

        var rows = branches.Select(branch =>
        {
            var branchSales = sales.GetValueOrDefault(branch.Id);
            var branchOpenCash = openCash.GetValueOrDefault(branch.Id);
            var branchStock = stock.GetValueOrDefault(branch.Id);
            var branchIncomingProducts = incomingProducts.GetValueOrDefault(branch.Id);
            var branchOutgoingProducts = outgoingProducts.GetValueOrDefault(branch.Id);
            var branchIncomingCash = incomingCash.GetValueOrDefault(branch.Id);
            return new BranchOverview(branch.Id, branch.Name, branch.IsMainHub,
                branchSales?.NetSalesAmount ?? 0m, branchSales?.ConfirmedSaleCount ?? 0L, branchSales?.VoidedSaleCount ?? 0L,
                branchOpenCash?.OpenCashSessionCount ?? 0L, branchStock?.ProductsWithAvailableStockCount ?? 0L,
                branchStock?.ExpiringLotCount ?? 0L, branchStock?.ExpiredLotCount ?? 0L,
                branchIncomingProducts?.BeforeDispatchCount ?? 0L, branchIncomingProducts?.InTransitCount ?? 0L,
                branchOutgoingProducts?.BeforeDispatchCount ?? 0L, branchIncomingCash?.InTransitCount ?? 0L);
        }).ToArray();

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Array.AsReadOnly(rows);
    }
}
