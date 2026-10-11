using System.Text.Json;

namespace MediPOS.Domain.Modules.OfflineSync;

public sealed class SyncEvent
{
    public const string OfflineSaleV1 = "offline_sale.v1";
    private SyncEvent() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid DeviceId { get; private set; }
    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public int SchemaVersion { get; private set; }
    public string PayloadHash { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public DateTimeOffset ClientOccurredAtUtc { get; private set; }
    public DateOnly ClientLocalDate { get; private set; }
    public string ClientTimeZoneId { get; private set; } = string.Empty;
    public DateTimeOffset ReceivedAtUtc { get; private set; }
    public string ProcessingStatus { get; private set; } = "pending";
    public DateTimeOffset? ProcessedAtUtc { get; private set; }
    public string? ResultCode { get; private set; }
    public Guid? ResultReferenceId { get; private set; }

    public static SyncEvent Receive(Guid tenantId, Guid deviceId, Guid eventId, string type, int version,
        string payload, string hash, DateTimeOffset clientTime, DateOnly localDate, string timeZoneId, DateTimeOffset received)
    {
        if (tenantId == Guid.Empty || deviceId == Guid.Empty || eventId == Guid.Empty) throw new ArgumentException("sync.identity_invalid");
        if (type != OfflineSaleV1) throw new ArgumentException("sync.type_unsupported");
        if (version != 1) throw new ArgumentException("sync.version_unsupported");
        if (clientTime == default || clientTime.Offset != TimeSpan.Zero || clientTime.Ticks % 10 != 0 || localDate == default)
            throw new ArgumentException("sync.time_invalid");
        if (string.IsNullOrWhiteSpace(timeZoneId) || timeZoneId.Length > 100) throw new ArgumentException("sync.time_invalid");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        if (DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clientTime, zone).DateTime) != localDate)
            throw new ArgumentException("sync.time_invalid");
        var canonical = SyncPayload.Canonicalize(payload);
        if (hash != canonical.Hash) throw new ArgumentException("sync.hash_mismatch");
        // The complete client envelope, not merely its payload, is bounded to 64 KiB in canonical UTF-8.
        using var document = JsonDocument.Parse(canonical.Json);
        if (JsonSerializer.SerializeToUtf8Bytes(new
        {
            eventId,
            deviceId,
            eventType = type,
            schemaVersion = version,
            clientOccurredAtUtc = clientTime,
            clientLocalDate = localDate,
            clientTimeZoneId = timeZoneId,
            payload = document.RootElement,
            payloadHash = hash,
        }).Length > SyncPayload.MaximumBytes) throw new ArgumentException("sync.event_too_large");
        return new SyncEvent
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            DeviceId = deviceId,
            EventId = eventId,
            EventType = type,
            SchemaVersion = version,
            Payload = canonical.Json,
            PayloadHash = canonical.Hash,
            ClientOccurredAtUtc = clientTime,
            ClientLocalDate = localDate,
            ClientTimeZoneId = timeZoneId,
            ReceivedAtUtc = ServerTime(received)
        };
    }

    public bool HasSameIdentity(SyncEvent other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return TenantId == other.TenantId && EventId == other.EventId && DeviceId == other.DeviceId &&
            EventType == other.EventType && SchemaVersion == other.SchemaVersion && PayloadHash == other.PayloadHash &&
            ClientOccurredAtUtc == other.ClientOccurredAtUtc && ClientLocalDate == other.ClientLocalDate && ClientTimeZoneId == other.ClientTimeZoneId;
    }

    public void Finish(string status, string code, Guid? referenceId, DateTimeOffset now)
    {
        if (ProcessingStatus != "pending") throw new InvalidOperationException("sync.event_finalized");
        if (status is not ("accepted" or "conflict" or "rejected")) throw new ArgumentException("sync.status_invalid");
        if (string.IsNullOrWhiteSpace(code) || code.Length > 80 || code.Any(character =>
                !(char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '.' or '_')) || referenceId == Guid.Empty)
            throw new ArgumentException("sync.result_invalid");
        var instant = ServerTime(now);
        if (instant < ReceivedAtUtc) throw new ArgumentException("sync.time_invalid");
        ProcessingStatus = status; ResultCode = code; ResultReferenceId = referenceId; ProcessedAtUtc = instant;
    }

    public static DateTimeOffset ServerTime(DateTimeOffset now)
    {
        if (now == default || now.Offset != TimeSpan.Zero) throw new ArgumentException("sync.time_invalid");
        return new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero);
    }
}
