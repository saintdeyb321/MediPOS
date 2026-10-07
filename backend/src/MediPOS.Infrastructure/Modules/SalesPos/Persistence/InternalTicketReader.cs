using MediPOS.Application.Modules.SalesPos;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.SalesPos.Persistence;

internal sealed class InternalTicketReader(MediPosDbContext context) : IInternalTicketReader
{
    public async Task<InternalTicketSnapshot?> FindAsync(Guid tenantId, Guid branchId, Guid saleId, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        // One statement snapshot avoids mixing a confirmed header with a concurrent void.
        // One excess line/payment makes corrupted oversized history fail validation without an unbounded read.
        return await (from sale in context.Sales.AsNoTracking()
                          .Include(sale => sale.Lines.OrderBy(line => line.Id).Take(Sale.MaximumLines + 1))
                      where sale.TenantId == tenantId && sale.BranchId == branchId && sale.Id == saleId
                      join branch in context.Branches on new { sale.TenantId, Id = sale.BranchId } equals new { branch.TenantId, branch.Id }
                      join legal in context.LegalEntities on new { branch.TenantId, Id = branch.LegalEntityId } equals new { legal.TenantId, legal.Id }
                      join tenant in context.Tenants on sale.TenantId equals tenant.Id
                      join member in context.Memberships on new { sale.TenantId, Id = sale.SellerMembershipId } equals new { member.TenantId, member.Id }
                      join user in context.Users on member.UserId equals user.Id
                      select new InternalTicketSnapshot(sale,
                          context.SalePayments.Where(payment => payment.TenantId == tenantId && payment.SaleId == sale.Id)
                              .OrderBy(payment => payment.Method).ThenBy(payment => payment.Id).Take(6).ToArray(),
                          new InternalTicketCurrentDisplay(tenant.Id, tenant.TradingName, legal.Id, legal.LegalName, legal.Ruc,
                              branch.Id, branch.Name, member.Id, user.Id, user.DisplayName)))
            .AsSingleQuery().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }
}
