namespace MediPOS.Application.Modules.IdentityAccess.OperationalAccess;

public sealed record OperationReceipt(Guid RequestId, string Kind, string ResultJson);
public interface IRecoverableOperations
{
    Task<OperationReceipt> ExecuteAsync(Guid tenantId, Guid branchId, Guid requestId, string kind, string payloadHash,
        Func<CancellationToken, Task<string>> execute, CancellationToken token);
    Task<OperationReceipt> ReadAsync(Guid tenantId, Guid branchId, Guid requestId, CancellationToken token);
}
