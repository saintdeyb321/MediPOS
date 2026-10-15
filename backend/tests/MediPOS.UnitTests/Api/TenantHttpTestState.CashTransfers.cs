using System.Reflection;
using MediPOS.Application.Modules.Cash;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Cash;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.UnitTests.Api;

public sealed partial class TenantHttpTestState
{
    internal List<CashTransfer> CashTransfers { get; } = [];
    private void ConfigureCashTransfers(IServiceCollection services)
    {
        Type[] ports = [typeof(ICashTransferReader), typeof(ICashTransferTransaction)];
        foreach (var port in ports)
            services.AddScoped(port, _ => { var proxy = DispatchProxy.Create(port, typeof(Port)); ((Port)proxy).State = this; return proxy; });
    }
    private bool InvokeCashTransfers(Type type, string method, object?[] args, out object? result)
    {
        result = null;
        if (type == typeof(ICashTransferReader))
        {
            result = method == "FindAsync" ? Task.FromResult(CashTransfers.SingleOrDefault(transfer => transfer.TenantId == (Guid)args[0]! && transfer.Id == (Guid)args[1]!)) :
                Task.FromResult<IReadOnlyList<CashTransfer>>(CashTransfers.Where(t => t.Status == CashTransferStatus.InTransit && (args[1] is null || t.DestinationBranchId == (Guid)args[1]!)).Skip((int)args[2]!).Take((int)args[3]!).ToArray()); return true;
        }
        if (type == typeof(ICashTransferTransaction))
        {
            result = method == "BeginDispatchAsync" ? Task.FromResult<ICashTransferDispatchScope>(new CashDispatchScope(this, Cash.Single(s => s.Id == (Guid)args[2]!))) :
                Task.FromResult<ICashTransferReceiveScope>(new CashReceiveScope(this, CashTransfers.Single(t => t.Id == (Guid)args[1]!))); return true;
        }
        return false;
    }
    private sealed class CashDispatchScope(TenantHttpTestState state, CashSession session) : ICashTransferDispatchScope
    {
        public CashSession Session => session;
        public Task<CashPaymentLedger> ReadLedgerAsync(CancellationToken cancellationToken) => Task.FromResult(state.Ledger(session));
        public Task CompleteAsync(CashTransfer transfer, AuditLog audit, CancellationToken cancellationToken) => Save(() => { state.CashTransfers.Add(transfer); state.Audits.Add(audit); });
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class CashReceiveScope(TenantHttpTestState state, CashTransfer transfer) : ICashTransferReceiveScope
    {
        public CashTransfer Transfer => transfer;
        public Task<CashSession> LockDestinationAsync(Guid sessionId, CancellationToken cancellationToken) => Task.FromResult(state.Cash.Single(s => s.Id == sessionId));
        public Task CompleteAsync(AuditLog audit, CancellationToken cancellationToken) => Save(() => state.Audits.Add(audit));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
