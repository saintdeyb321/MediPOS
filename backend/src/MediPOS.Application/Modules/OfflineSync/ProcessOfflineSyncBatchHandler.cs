using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;

namespace MediPOS.Application.Modules.OfflineSync;

public sealed class ProcessOfflineSyncBatchHandler(ResolveAccessContextHandler resolver, ISyncDeviceStore devices,
    ReserveSyncEventReceiptHandler receipts, TimeProvider clock, OfflineSaleProcessor? sales = null)
{
    public async Task<OfflineSyncBatchResult> HandleAsync(OfflineSyncBatchCommand command, CancellationToken token)
    {
        using var observation = MediPOS.SharedKernel.OperationalTelemetry.Begin(MediPOS.SharedKernel.ObservedOperation.OfflineSyncBatch);
        try
        {
            var result = await ExecuteAsync(command, token).ConfigureAwait(false);
            observation.Complete();
            return result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { observation.Complete(MediPOS.SharedKernel.OperationOutcome.Cancelled); throw; }
    }

    private async Task<OfflineSyncBatchResult> ExecuteAsync(OfflineSyncBatchCommand command, CancellationToken token)
    {
        var events = OfflineSyncBatchPolicy.ValidateAndSnapshot(command, token);
        var receivedAt = Domain.Modules.OfflineSync.SyncEvent.ServerTime(clock.GetUtcNow());
        await RequireDeviceAsync(command, token).ConfigureAwait(false);
        var results = new List<OfflineSyncEventResult>(events.Length);
        // Sequential on the scoped DbContext. Each B5.1 reservation owns one Device -> Event transaction,
        // disposed before the next event, so revocation is not held off by a whole-batch device lock.
        foreach (var envelope in events)
        {
            token.ThrowIfCancellationRequested();
            try { results.Add(await ReceiveAsync(command.TenantId, envelope, token).ConfigureAwait(false)); }
            catch (ApplicationErrorException error) when (IsInputError(error.Error))
            {
                results.Add(new(envelope.EventId, "rejected", error.Error.Code, null, null, null, null, false, false));
            }
            MediPOS.SharedKernel.OperationalTelemetry.SyncResult(results[^1].Status, results[^1].IsReplay);
            // Access/device errors, cancellation and persistence failures propagate. Previously committed receipts
            // remain durable; uncertain outcomes are resolved by replay, never by fabricated pending/terminal results.
        }
        await RequireDeviceAsync(command, token).ConfigureAwait(false);
        return new(OfflineSyncBatchPolicy.ProtocolVersion, command.DeviceId, receivedAt, results.AsReadOnly(),
            results.Count(result => result.Status == "pending"), results.Count(result => result.Status == "accepted"),
            results.Count(result => result.Status == "conflict"), results.Count(result => result.Status == "rejected"))
        { BatchRequestId = command.BatchRequestId };
    }

    private async Task RequireDeviceAsync(OfflineSyncBatchCommand command, CancellationToken token)
    {
        await SyncAccess.RequireAsync(resolver, command.TenantId, null, clock, token, allowBranchLookup: true).ConfigureAwait(false);
        var device = await devices.FindAsync(command.TenantId, command.DeviceId, token).ConfigureAwait(false)
            ?? throw SyncAccess.Error("sync.device_not_found", ErrorCategory.NotFound);
        var access = await SyncAccess.RequireAsync(resolver, command.TenantId, device.BranchId, clock, token).ConfigureAwait(false);
        SyncAccess.RequireDevice(device, access);
        if (device.RegisteredByMembershipId != access.MembershipId) throw SyncAccess.Error("sync.device_denied", ErrorCategory.Forbidden);
    }

    private async Task<OfflineSyncEventResult> ReceiveAsync(Guid tenantId, SyncEventEnvelope envelope, CancellationToken token)
    {
        // Receipt-only composition remains available for intake; production authenticated composition processes pending sales.
        // Each commercial result and its receipt transition commit through the same reservation.
        await using var reservation = await receipts.HandleAsync(new(tenantId, envelope), token).ConfigureAwait(false);
        OfflineSyncEventResult result;
        var completed = false;
        if (reservation.IsIdentityConflict)
            result = new(envelope.EventId, "conflict", "sync.identity_conflict", null, null, null, null, false, false);
        else
        {
            var receipt = reservation.Receipt;
            if (receipt.ProcessingStatus is not ("pending" or "accepted" or "conflict" or "rejected") ||
                (receipt.ProcessingStatus != "pending" && (receipt.ResultCode is null || receipt.ProcessedAtUtc is null)))
                throw new InvalidOperationException("Invalid persisted sync receipt state.");
            if (receipt.ProcessingStatus == "pending" && sales is not null)
            {
                var outcome = await sales.ProcessAsync(reservation, token).ConfigureAwait(false);
                await reservation.CompleteAsync(outcome.Status, outcome.Code, outcome.ReferenceId, token).ConfigureAwait(false);
                completed = true;
            }
            result = new(envelope.EventId, receipt.ProcessingStatus, receipt.ResultCode ?? "sync.pending", receipt.Id,
                receipt.ResultReferenceId, receipt.ReceivedAtUtc, receipt.ProcessedAtUtc, !reservation.IsNew, receipt.ProcessingStatus == "pending")
            { ClientOccurredAtUtc = receipt.ClientOccurredAtUtc, ClientLocalDate = receipt.ClientLocalDate, ClientTimeZoneId = receipt.ClientTimeZoneId };
        }
        // Produce the acknowledgment only after successful commit and scope disposal.
        if (!completed) await reservation.CompleteAsync(null, null, null, token).ConfigureAwait(false);
        return result;
    }

    private static bool IsInputError(ApplicationError error) => error.Category == ErrorCategory.Validation && error.Code is
        "sync.payload_invalid" or "sync.payload_too_large" or "sync.event_too_large" or "sync.hash_mismatch" or
        "sync.type_unsupported" or "sync.version_unsupported" or "sync.time_invalid";
}
