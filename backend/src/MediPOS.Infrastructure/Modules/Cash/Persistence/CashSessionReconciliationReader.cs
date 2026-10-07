using MediPOS.Application.Modules.Cash;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.Cash.Persistence;

internal sealed class CashSessionReconciliationReader(MediPosDbContext context) : ICashSessionReconciliationReader
{
    public async Task<CashReconciliationSnapshot?> FindAsync(Guid tenantId, Guid branchId, Guid sessionId, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        return await (from session in context.CashSessions.AsNoTracking()
                      where session.TenantId == tenantId && session.BranchId == branchId && session.Id == sessionId
                      join branch in context.Branches on new { session.TenantId, Id = session.BranchId } equals new { branch.TenantId, branch.Id }
                      join membership in context.Memberships on new { session.TenantId, Id = session.MembershipId } equals new { membership.TenantId, membership.Id }
                      join user in context.Users on membership.UserId equals user.Id
                      select new CashReconciliationSnapshot(session, new CashSessionParty(branch.Id, branch.Name, membership.Id, user.Id, user.DisplayName)))
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<CashPaymentLedger> ReadLedgerAsync(CashSession session, CancellationToken cancellationToken) => LoadLedgerAsync(context, session, cancellationToken);

    internal static async Task<CashPaymentLedger> LoadLedgerAsync(MediPosDbContext context, CashSession session, CancellationToken cancellationToken)
    {
        context.SelectTenant(session.TenantId);
        var sales = await context.Sales.AsNoTracking().Where(sale => sale.TenantId == session.TenantId && sale.CashSessionId == session.Id)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var ids = sales.Select(sale => sale.Id).ToArray();
        var payments = await context.SalePayments.AsNoTracking().Where(payment => payment.TenantId == session.TenantId && ids.Contains(payment.SaleId))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var reversals = await context.SalePaymentReversals.AsNoTracking().Where(reversal => reversal.TenantId == session.TenantId && ids.Contains(reversal.SaleId))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var transfers = await context.CashTransfers.AsNoTracking().Where(t => t.TenantId == session.TenantId &&
            (t.SourceCashSessionId == session.Id || (t.Status == CashTransferStatus.Received && t.DestinationCashSessionId == session.Id)))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return new(sales, payments, reversals, transfers);
    }
}
