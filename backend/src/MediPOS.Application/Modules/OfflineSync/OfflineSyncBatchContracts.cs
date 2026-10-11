namespace MediPOS.Application.Modules.OfflineSync;

// TenantId is a selection checked against OperationalAccess, never an authorization claim.
// BatchRequestId is tracing only; EventId remains the durable idempotency identity.
public sealed record OfflineSyncBatchCommand(Guid TenantId, Guid DeviceId, int ProtocolVersion,
    IReadOnlyList<SyncEventEnvelope>? Events, Guid? BatchRequestId = null);

// pending acknowledges durable reception only; accepted means a previously confirmed business operation.
// rejected with ReceiptId == null is input validation, not a terminal SyncEvent in the inbox.
// IsReplay is true only for an identical persisted identity; incompatible identities disclose no receipt metadata.
// Retryable means the same immutable event may be polled/retried (pending); terminal outcomes are immutable.
public sealed record OfflineSyncEventResult(Guid EventId, string Status, string ResultCode, Guid? ReceiptId,
    Guid? ResultReferenceId, DateTimeOffset? ReceivedAtUtc, DateTimeOffset? ProcessedAtUtc, bool IsReplay, bool Retryable);

// Results retain outbox order, including repeated EventId values. Counters count results, not distinct identities.
// There is no incremental reception cursor: UUIDv7 does not order commits. Retry using original EventId values.
// If authentication, cancellation or infrastructure failure aborts a batch, earlier committed receipts survive.
// No success response is synthesized for an uncertain commit; replay resolves it through the original identity.
public sealed record OfflineSyncBatchResult(int ProtocolVersion, Guid DeviceId, DateTimeOffset ServerReceivedAtUtc,
    IReadOnlyList<OfflineSyncEventResult> Results, int PendingCount, int AcceptedCount, int ConflictCount, int RejectedCount)
{
    public Guid? BatchRequestId { get; init; }
    public string? NextCursor { get; init; }
    public bool ReceiptCursorAvailable { get; init; }
}
