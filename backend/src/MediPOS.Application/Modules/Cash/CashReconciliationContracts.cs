using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Cash;

namespace MediPOS.Application.Modules.Cash;

public sealed record CashSessionParty(Guid BranchId, string BranchName, Guid MembershipId, Guid UserId, string DisplayName);
public sealed record CashReconciliationSnapshot(CashSession Session, CashSessionParty Party);
public sealed record CashSessionReconciliationDetails(Guid CashSessionId, Guid TenantId, CashSessionParty EmployeeBranch,
    decimal OpeningAmount, CashPaymentTotals PaymentTotals, decimal NetSalesAmount, decimal ExpectedCashAmount,
    decimal CountedCashAmount, decimal CashDifference, DateTimeOffset OpenedAt, DateTimeOffset ClosedAt, Guid ClosedByActorId)
{
    public CashTransferTotals CashTransfers { get; init; } = CashTransferTotals.Zero;
    public static CashSessionReconciliationDetails From(CashSession session, CashSessionParty party, CashPaymentTotals totals, CashTransferTotals? transfers = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.ValidateClosed();
        return new(session.Id, session.TenantId, party, session.OpeningAmount, totals, totals.NetSalesAmount(), session.ExpectedCashAmount!.Value,
            session.CountedCashAmount!.Value, session.CashDifference!.Value, session.OpenedAt, session.ClosedAt!.Value, session.ClosedByActorId!.Value)
        { CashTransfers = transfers ?? CashTransferTotals.Zero };
    }
}

public interface ICashSessionReconciliationReader
{
    Task<CashReconciliationSnapshot?> FindAsync(Guid tenantId, Guid branchId, Guid sessionId, CancellationToken cancellationToken);
    Task<CashPaymentLedger> ReadLedgerAsync(CashSession session, CancellationToken cancellationToken);
}

public interface ICashCloseTransaction
{
    Task<ICashCloseScope> BeginAsync(Guid tenantId, Guid branchId, Guid sessionId, CancellationToken cancellationToken);
}

// One concrete cash row lock, followed by its financial ledger reads and audited close in the same transaction.
public interface ICashCloseScope : IAsyncDisposable
{
    CashSession Session { get; }
    CashSessionParty Party { get; }
    Task<CashPaymentLedger> ReadLedgerAsync(CancellationToken cancellationToken);
    Task CompleteAsync(CashPaymentTotals totals, AuditLog audit, CancellationToken cancellationToken);
}
