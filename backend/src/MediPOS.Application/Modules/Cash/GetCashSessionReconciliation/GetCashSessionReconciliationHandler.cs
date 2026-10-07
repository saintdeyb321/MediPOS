using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.Cash;

namespace MediPOS.Application.Modules.Cash.GetCashSessionReconciliation;

public sealed record GetCashSessionReconciliationQuery(Guid TenantId, Guid BranchId, Guid CashSessionId);

public sealed class GetCashSessionReconciliationHandler(ResolveAccessContextHandler resolver, ICashSessionReconciliationReader reader, TimeProvider clock)
{
    public async Task<CashSessionReconciliationDetails> HandleAsync(GetCashSessionReconciliationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.TenantId == Guid.Empty || query.BranchId == Guid.Empty || query.CashSessionId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var access = await CashOperationalAccess.ResolveAsync(resolver, query.TenantId, query.BranchId, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        var snapshot = await reader.FindAsync(access.TenantId, query.BranchId, query.CashSessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(CashSessionErrors.NotFound);
        CashReconciliationAccess.Require(snapshot.Session, access, closing: false);
        if (snapshot.Session.Status != CashSessionStatus.Closed) throw new ApplicationErrorException(CashSessionErrors.NotClosed);
        var totals = CashReconciliationAccess.Calculate(snapshot.Session, await reader.ReadLedgerAsync(snapshot.Session, cancellationToken).ConfigureAwait(false));
        var expected = CashReconciliationAccess.ExpectedCash(snapshot.Session, totals);
        try
        {
            snapshot.Session.ValidateClosed();
            if (snapshot.Session.ExpectedCashAmount != expected) throw new ArgumentException("Stored reconciliation must match the immutable ledger.");
        }
        catch (ArgumentException) { throw new ApplicationErrorException(CashSessionErrors.CorruptedLedger); }
        return CashSessionReconciliationDetails.From(snapshot.Session, snapshot.Party, totals);
    }
}
