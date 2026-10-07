using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Cash;

namespace MediPOS.Application.Modules.Cash;

public sealed record CashTransferDetails(Guid Id, Guid TenantId, Guid SourceBranchId, Guid SourceCashSessionId,
    Guid DestinationBranchId, Guid? DestinationCashSessionId, decimal Amount, string Status, DateTimeOffset DispatchedAt,
    Guid DispatchedByActorId, DateTimeOffset? ReceivedAt, Guid? ReceivedByActorId)
{
    public static CashTransferDetails From(CashTransfer transfer)
    {
        transfer.Validate();
        return new(transfer.Id, transfer.TenantId, transfer.SourceBranchId, transfer.SourceCashSessionId, transfer.DestinationBranchId,
            transfer.DestinationCashSessionId, transfer.Amount, CashTransferStatusCodes.ToCode(transfer.Status), transfer.DispatchedAt,
            transfer.DispatchedByActorId, transfer.ReceivedAt, transfer.ReceivedByActorId);
    }
}
public sealed record PendingCashTransfers(IReadOnlyList<CashTransferDetails> Items, bool HasMore);
public interface ICashTransferReader
{
    Task<CashTransfer?> FindAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<CashTransfer>> PendingAsync(Guid tenantId, Guid? destinationBranchId, int offset, int limit, CancellationToken cancellationToken);
}
public interface ICashTransferTransaction
{
    Task<ICashTransferDispatchScope> BeginDispatchAsync(Guid tenantId, Guid branchId, Guid sessionId, Guid destinationBranchId, CancellationToken cancellationToken);
    Task<ICashTransferReceiveScope> BeginReceiveAsync(Guid tenantId, Guid transferId, CancellationToken cancellationToken);
}
public interface ICashTransferDispatchScope : IAsyncDisposable
{
    CashSession Session { get; }
    Task<CashPaymentLedger> ReadLedgerAsync(CancellationToken cancellationToken);
    Task CompleteAsync(CashTransfer transfer, AuditLog audit, CancellationToken cancellationToken);
}
public interface ICashTransferReceiveScope : IAsyncDisposable
{
    CashTransfer Transfer { get; }
    Task<CashSession> LockDestinationAsync(Guid sessionId, CancellationToken cancellationToken);
    Task CompleteAsync(AuditLog audit, CancellationToken cancellationToken);
}
