using System.Text.Json;

namespace MediPOS.Application.Modules.OfflineSync;

public static class OfflineSyncBatchPolicy
{
    public const int ProtocolVersion = 1;
    public const int MaximumEvents = 50;
    public const int MaximumRequestBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static SyncEventEnvelope[] ValidateAndSnapshot(OfflineSyncBatchCommand command, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (command is null || command.Events is null || command.Events.Count == 0) throw SyncAccess.Error("sync.batch_invalid");
        if (command.Events.Count > MaximumEvents) throw SyncAccess.Error("sync.batch_too_many_events");
        if (command.TenantId == Guid.Empty || command.DeviceId == Guid.Empty || command.BatchRequestId == Guid.Empty)
            throw SyncAccess.Error("sync.identity_invalid");
        if (command.ProtocolVersion != ProtocolVersion) throw SyncAccess.Error("sync.version_unsupported");
        var events = command.Events.ToArray();
        long characters = 0;
        foreach (var envelope in events)
        {
            token.ThrowIfCancellationRequested();
            if (envelope is null) throw SyncAccess.Error("sync.batch_invalid");
            if (envelope.EventId == Guid.Empty || envelope.DeviceId == Guid.Empty) throw SyncAccess.Error("sync.identity_invalid");
            if (envelope.DeviceId != command.DeviceId) throw SyncAccess.Error("sync.batch_device_mismatch");
            characters += (long)(envelope.Payload?.Length ?? 0) + (envelope.PayloadHash?.Length ?? 0) +
                (envelope.EventType?.Length ?? 0) + (envelope.ClientTimeZoneId?.Length ?? 0);
            if (characters > MaximumRequestBytes) throw SyncAccess.Error("sync.batch_too_large");
        }
        // Bound the logical v1 JSON request, including envelope fields and JSON escaping, without allocating a whole request buffer.
        // This size check never canonicalizes payloads or changes hashes. Invalid Unicode is rejected individually by B5.1.
        // A future authenticated HTTP route must also cap the actual incoming body to 4 MiB (including whitespace).
        using var size = new RequestSizeStream(token);
        JsonSerializer.Serialize(size, command with { Events = events }, JsonOptions);
        return events;
    }

    private sealed class RequestSizeStream(CancellationToken token) : Stream
    {
        private long _length;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _length;
        public override long Position { get => _length; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            token.ThrowIfCancellationRequested(); _length += buffer.Length;
            if (_length > MaximumRequestBytes) throw SyncAccess.Error("sync.batch_too_large");
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
