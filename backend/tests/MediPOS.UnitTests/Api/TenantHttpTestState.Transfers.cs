using System.Reflection;
using MediPOS.Application.Modules.Transfers;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.Transfers;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.UnitTests.Api;

public sealed partial class TenantHttpTestState
{
    internal List<Transfer> Transfers { get; } = [];
    internal List<TransferEvent> TransferEvents { get; } = [];
    internal List<TransferLotAllocation> TransferAllocations { get; } = [];
    internal Action? AfterTransferLock { get; set; }
    private void ConfigureTransfers(IServiceCollection services)
    {
        Type[] ports = [typeof(ITransferRequestStore), typeof(ITransferReader), typeof(ITransferTransaction), typeof(ITransferDirectoryReader)];
        foreach (var port in ports)
            services.AddScoped(port, _ => { var proxy = DispatchProxy.Create(port, typeof(Port)); ((Port)proxy).State = this; return proxy; });
    }
    private bool InvokeTransfers(Type type, string method, object?[] args, out object? result)
    {
        result = null;
        if (type == typeof(ITransferDirectoryReader))
        {
            var query = (GetTransfersQuery)args[0]!;
            result = Task.FromResult<IReadOnlyList<TransferSummary>>(Transfers.Where(t => (!query.BranchId.HasValue || t.SourceBranchId == query.BranchId || t.DestinationBranchId == query.BranchId) && (!query.Status.HasValue || t.Status == query.Status)).Skip(query.Offset).Take(query.Limit).Select(t => new TransferSummary(t.Id, t.SourceBranchId, "Origen", t.DestinationBranchId, "Destino", t.Status, t.RequestedAt, t.UpdatedAt)).ToArray()); AfterPrivateRead?.Invoke(); return true;
        }
        if (type == typeof(ITransferRequestStore))
        {
            if (method == "LoadAsync") result = Task.FromResult(new TransferRequestData(Branches.Any(b => b.Id == (Guid)args[1]!) && Branches.Any(b => b.Id == (Guid)args[2]!), Products.ToArray(), Units.ToArray(), "Origen", "Destino"));
            else result = Save(() => { Transfers.Add((Transfer)args[0]!); TransferEvents.Add((TransferEvent)args[1]!); Audits.Add((AuditLog)args[2]!); });
            return true;
        }
        if (type == typeof(ITransferReader))
        {
            var transfer = Transfers.SingleOrDefault(t => t.TenantId == (Guid)args[0]! && t.Id == (Guid)args[1]!);
            result = Task.FromResult(transfer is null ? null : TransferSnapshot(transfer)); return true;
        }
        if (type == typeof(ITransferTransaction))
        { result = Task.FromResult<ITransferScope>(new TransferScope(this, TransferSnapshot(Transfers.Single(t => t.Id == (Guid)args[1]!)))); return true; }
        return false;
    }
    private TransferSnapshot TransferSnapshot(Transfer transfer) => new(transfer, TransferEvents.Where(e => e.TransferId == transfer.Id).ToArray(), TransferAllocations.Where(a => a.TransferId == transfer.Id).ToArray(), "Origen", "Destino");
    private sealed class TransferScope(TenantHttpTestState state, TransferSnapshot snapshot) : ITransferScope
    {
        public TransferSnapshot Snapshot => snapshot;
        public Task<IReadOnlyList<InventoryLot>> LockSourceLotsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
        { state.AfterTransferLock?.Invoke(); return Task.FromResult<IReadOnlyList<InventoryLot>>(state.Lots.Where(lot => ids.Contains(lot.Id)).ToArray()); }
        public Task CompleteAsync(IReadOnlyList<TransferLotAllocation> newAllocations, IReadOnlyList<InventoryLot> newLots, IReadOnlyList<StockMovement> movements, TransferEvent transition, AuditLog audit, CancellationToken cancellationToken)
        {
            foreach (var movement in movements.Where(m => m.MovementType == StockMovementType.TransferDispatch)) state.Lots.Single(lot => lot.Id == movement.InventoryLotId).ApplyTransferDispatch(movement);
            state.TransferAllocations.AddRange(newAllocations); state.Lots.AddRange(newLots); state.Movements.AddRange(movements); state.TransferEvents.Add(transition); state.Audits.Add(audit);
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
