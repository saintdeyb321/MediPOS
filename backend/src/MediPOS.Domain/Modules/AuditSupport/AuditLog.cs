using System.Text.Json;

namespace MediPOS.Domain.Modules.AuditSupport;

public sealed class AuditLog
{
    private AuditLog() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ActorId { get; private set; }
    public AuditAction Action { get; private set; }
    public AuditEntityType EntityType { get; private set; }
    public Guid EntityId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public string? BeforeJson { get; private set; }
    public string? AfterJson { get; private set; }

    public static AuditLog Create(
        Guid tenantId, Guid actorId, AuditAction action, AuditEntityType entityType, Guid entityId,
        DateTimeOffset occurredAt, string correlationId, string? beforeJson, string? afterJson)
    {
        if (tenantId == Guid.Empty || actorId == Guid.Empty || entityId == Guid.Empty)
            throw new ArgumentException("Tenant, actor and entity identifiers are required.");
        if (!Enum.IsDefined(entityType))
            throw new ArgumentOutOfRangeException(nameof(entityType));
        if (AuditCodes.EntityFor(action) != entityType)
            throw new ArgumentException("The audit entity type must match the action.", nameof(entityType));
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (correlationId.Length != 32 || correlationId.All(value => value == '0') ||
            correlationId.Any(value => !char.IsAsciiHexDigitLower(value)))
            throw new ArgumentException("A valid server trace identifier is required.", nameof(correlationId));
        ValidateSnapshot(beforeJson);
        ValidateSnapshot(afterJson);
        return new AuditLog
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            ActorId = actorId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            OccurredAt = occurredAt.ToUniversalTime(),
            CorrelationId = correlationId,
            BeforeJson = beforeJson,
            AfterJson = afterJson,
        };
    }

    private static void ValidateSnapshot(string? json)
    {
        if (json is null)
            return;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("An audit snapshot must be a JSON object.");
        }
        catch (JsonException)
        {
            throw new ArgumentException("An audit snapshot must be valid JSON.", nameof(json));
        }
    }
}
