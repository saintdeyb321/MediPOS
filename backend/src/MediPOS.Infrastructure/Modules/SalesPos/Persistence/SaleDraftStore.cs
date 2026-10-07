using System.Data;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediPOS.Infrastructure.Modules.SalesPos.Persistence;

internal sealed class SaleDraftStore(MediPosDbContext context) : ISaleDraftStore
{
    public async Task<SaleDraftSnapshot?> FindAsync(Guid tenantId, Guid branchId, Guid saleId, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        // One statement snapshot for header, version and lines; no inventory or cost reads.
        return await context.Sales.AsNoTracking().Include(value => value.Lines).AsSingleQuery()
            .Where(value => value.TenantId == tenantId && value.BranchId == branchId && value.Id == saleId)
            .Select(value => new SaleDraftSnapshot(value, EF.Property<uint>(value, "Version")))
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<uint> AddAsync(Sale sale, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sale);
        sale.EnsureDraft();
        if (sale.Lines.Count != 0 || sale.TotalAmount != 0) throw new InvalidOperationException("Creation requires an empty draft.");
        context.SelectTenant(sale.TenantId);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        try
        {
            context.Sales.Add(sale);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            var version = context.Entry(sale).Property<uint>("Version").CurrentValue;
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            context.Entry(sale).State = EntityState.Detached;
            return version;
        }
        catch { context.ChangeTracker.Clear(); throw; }
    }

    public async Task<uint> ReplaceLinesAsync(Sale sale, uint expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sale);
        sale.EnsureDraft();
        context.SelectTenant(sale.TenantId);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        try
        {
            // Compare-and-set the parent FIRST, including unchanged/empty carts. PostgreSQL serializes
            // this sale row only; a stale xmin cannot delete or overwrite the winning editor's lines.
            var updated = await context.Sales.Where(value => value.TenantId == sale.TenantId && value.Id == sale.Id &&
                value.BranchId == sale.BranchId && value.SellerMembershipId == sale.SellerMembershipId &&
                value.CashSessionId == sale.CashSessionId && value.Status == SaleStatus.Draft && EF.Property<uint>(value, "Version") == expectedVersion)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.TotalAmount, sale.TotalAmount)
                    .SetProperty(value => value.UpdatedAt, sale.UpdatedAt), cancellationToken).ConfigureAwait(false);
            if (updated != 1) throw new ApplicationErrorException(SalesPosErrors.ConcurrentEdit);
            await context.SaleLines.Where(value => value.TenantId == sale.TenantId && value.SaleId == sale.Id)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            context.Sales.Attach(sale);
            context.SaleLines.AddRange(sale.Lines);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            var version = await context.Sales.Where(value => value.TenantId == sale.TenantId && value.Id == sale.Id)
                .Select(value => EF.Property<uint>(value, "Version")).SingleAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            context.Entry(sale).State = EntityState.Detached;
            foreach (var line in sale.Lines) context.Entry(line).State = EntityState.Detached;
            return version;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: SaleLineConfiguration.SelectionIndex })
        { context.ChangeTracker.Clear(); throw new ApplicationErrorException(SalesPosErrors.DuplicateLines); }
        catch { context.ChangeTracker.Clear(); throw; }
    }
}
