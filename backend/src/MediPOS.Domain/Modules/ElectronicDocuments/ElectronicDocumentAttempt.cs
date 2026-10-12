namespace MediPOS.Domain.Modules.ElectronicDocuments;

public sealed class ElectronicDocumentAttempt
{
    private ElectronicDocumentAttempt() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid DocumentId { get; private set; }
    public int Number { get; private set; }
    public string Phase { get; private set; } = "";
    public string IdempotencyKey { get; private set; } = "";
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? FinishedAtUtc { get; private set; }
    public string Outcome { get; private set; } = "";
    public string? ErrorCode { get; private set; }
    public string? ProviderReference { get; private set; }
    // Each attempt has immutable started/completed events; crashes never require updating history.
    public static ElectronicDocumentAttempt Record(ElectronicDocument document, bool completed, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(document); ElectronicDocumentRules.Utc(now);
        ElectronicDocumentRules.Require(document.AttemptCount > 0 && document.AttemptStartedAtUtc <= now, "attempt_invalid");
        return new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = document.TenantId,
            DocumentId = document.Id,
            Number = document.AttemptCount,
            Phase = completed ? "completed" : "started",
            IdempotencyKey = document.IdempotencyKey,
            StartedAtUtc = document.AttemptStartedAtUtc!.Value,
            FinishedAtUtc = completed ? now : null,
            Outcome = document.Status,
            ErrorCode = completed ? document.LastErrorCode : null,
            ProviderReference = completed ? document.ProviderReference : null
        };
    }
}
