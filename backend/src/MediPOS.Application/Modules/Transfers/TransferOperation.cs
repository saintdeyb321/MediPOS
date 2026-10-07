using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Transfers;

namespace MediPOS.Application.Modules.Transfers;

internal static class TransferOperation
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public static async Task<TransferSnapshot> FindAsync(ResolveAccessContextHandler resolver, ITransferReader reader,
        Guid tenantId, Guid id, TimeProvider clock, CancellationToken token)
    {
        if (id == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await TransferAccess.TenantAsync(resolver, tenantId, clock.GetUtcNow(), token).ConfigureAwait(false);
        var snapshot = await reader.FindAsync(tenantId, id, token).ConfigureAwait(false) ?? throw new ApplicationErrorException(TransferErrors.NotFound);
        if (snapshot.Transfer.Id != id || snapshot.Transfer.TenantId != tenantId) throw new ApplicationErrorException(TransferErrors.NotFound);
        TransferAccess.Validate(snapshot);
        return snapshot;
    }
    public static async Task<ITransferScope> LockAsync(ResolveAccessContextHandler resolver, ITransferReader reader, ITransferTransaction transactions,
        Guid tenantId, Guid id, TransferEventType action, TimeProvider clock, CancellationToken token)
    {
        var before = await FindAsync(resolver, reader, tenantId, id, clock, token).ConfigureAwait(false);
        await TransferAccess.ActionAsync(resolver, before, action, clock.GetUtcNow(), token).ConfigureAwait(false);
        var scope = await transactions.BeginAsync(tenantId, id, token).ConfigureAwait(false);
        try
        {
            if (scope.Snapshot.Transfer.Id != id || scope.Snapshot.Transfer.TenantId != tenantId) throw new ApplicationErrorException(TransferErrors.NotFound);
            TransferAccess.Validate(scope.Snapshot);
            await TransferAccess.ActionAsync(resolver, scope.Snapshot, action, clock.GetUtcNow(), token).ConfigureAwait(false);
            return scope;
        }
        catch { await scope.DisposeAsync().ConfigureAwait(false); throw; }
    }
    public static AuditLog Audit(Transfer t, TransferEvent e, TransferStatus? before, object after) =>
        AuditTrail.Record(t.TenantId, e.ActorId, e.EventType switch
        {
            TransferEventType.Requested => AuditAction.TransferRequested,
            TransferEventType.Approved => AuditAction.TransferApproved,
            TransferEventType.Dispatched => AuditAction.TransferDispatched,
            TransferEventType.Received => AuditAction.TransferReceived,
            TransferEventType.Cancelled => AuditAction.TransferCancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(e)),
        }, t.Id, e.OccurredAt, before.HasValue ? JsonSerializer.Serialize(new { status = TransferStatusCodes.ToCode(before.Value) }, JsonOptions) : null,
            JsonSerializer.Serialize(after, JsonOptions));
    public static TransferDetails Result(ITransferScope scope, TransferEvent e, IReadOnlyList<TransferLotAllocation>? added = null) =>
        TransferDetails.From(scope.Snapshot with { Events = [.. scope.Snapshot.Events, e], Allocations = added ?? scope.Snapshot.Allocations });
}
