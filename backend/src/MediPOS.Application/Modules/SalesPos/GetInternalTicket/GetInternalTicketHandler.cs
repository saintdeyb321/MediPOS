using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.SalesPos;

namespace MediPOS.Application.Modules.SalesPos.GetInternalTicket;

public sealed record GetInternalTicketQuery(Guid TenantId, Guid BranchId, Guid SaleId);

public sealed class GetInternalTicketHandler(ResolveAccessContextHandler resolver, IInternalTicketReader reader, TimeProvider clock)
{
    public async Task<InternalTicket> HandleAsync(GetInternalTicketQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.TenantId == Guid.Empty || query.BranchId == Guid.Empty || query.SaleId == Guid.Empty)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var access = await SaleDraftAccess.ResolveAsync(resolver, query.TenantId, query.BranchId, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        if (access.Role is not (TenantRole.Owner or TenantRole.Pharmacist or TenantRole.Cashier))
            throw new ApplicationErrorException(InternalTicketErrors.Forbidden);
        var snapshot = await reader.FindAsync(access.TenantId, query.BranchId, query.SaleId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(SalesPosErrors.SaleNotFound);
        if (snapshot.Sale.TenantId != access.TenantId || snapshot.Sale.BranchId != access.BranchId)
            throw new ApplicationErrorException(SalesPosErrors.SaleNotFound);
        if (access.Role != TenantRole.Owner && snapshot.Sale.SellerMembershipId != access.MembershipId)
            throw new ApplicationErrorException(InternalTicketErrors.Forbidden);
        if (snapshot.Sale.Status == SaleStatus.Draft) throw new ApplicationErrorException(InternalTicketErrors.NotFinal);
        try { return InternalTicket.From(snapshot); }
        catch (Exception error) when (error is ArgumentException or ArithmeticException or InvalidOperationException)
        { throw new ApplicationErrorException(InternalTicketErrors.CorruptedHistory); }
    }
}
