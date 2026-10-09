using System.Data;
using MediPOS.Application.Modules.Reporting.GetOwnerCommissionsReport;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.Reporting.Persistence;

internal sealed class OwnerCommissionsReportReader(MediPosDbContext context) : IOwnerCommissionsReportReader
{
    public async Task<CommissionReportPage> ReadAsync(OwnerCommissionsReadRequest request, CancellationToken cancellationToken)
    {
        context.SelectTenant(request.TenantId);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken).ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken).ConfigureAwait(false);
        var query = OwnerCommissionsReportQueries.Rows(context.CommissionEntries.AsNoTracking(), context.Sales.AsNoTracking(),
            context.Memberships.AsNoTracking(), context.Users.AsNoTracking(), context.BusinessProducts.AsNoTracking(), request);
        var totals = await OwnerCommissionsReportQueries.Totals(query).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? new CommissionReportTotals(0, 0m, 0m, 0m);
        var rows = await query.OrderByDescending(row => row.ConfirmedAtUtc).ThenBy(row => row.SaleId).ThenBy(row => row.SaleLineId)
            .ThenBy(row => row.CommissionEntryId).Skip(request.Offset).Take(request.Limit).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(Array.AsReadOnly(rows), totals);
    }
}
