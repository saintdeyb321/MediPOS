using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.SalesPos;

namespace MediPOS.Application.Modules.SalesPos.CreateSaleDraft;

public sealed record CreateSaleDraftCommand(Guid TenantId, Guid BranchId);

public sealed class CreateSaleDraftHandler(ResolveAccessContextHandler resolver, IFindOpenCashSession cashSessions,
    ISaleDraftStore store, TimeProvider clock)
{
    public async Task<SaleDraftDetails> HandleAsync(CreateSaleDraftCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = clock.GetUtcNow();
        var access = await SaleDraftAccess.ResolveAsync(resolver, command.TenantId, command.BranchId, now, cancellationToken).ConfigureAwait(false);
        var cash = await SaleDraftAccess.RequireOpenCashAsync(cashSessions, access, null, cancellationToken).ConfigureAwait(false);
        var sale = Sale.CreateDraft(access.TenantId, command.BranchId, access.MembershipId, cash.CashSessionId, now);
        var version = await store.AddAsync(sale, cancellationToken).ConfigureAwait(false);
        return SaleDraftDetails.From(sale, version);
    }
}
