using System.Globalization;
using System.Text.Json;

namespace MediPOS.Domain.Modules.ElectronicDocuments;

public sealed class ElectronicDocument
{
    private ElectronicDocument() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid LegalEntityId { get; private set; }
    public Guid SaleId { get; private set; }
    public Guid SellerMembershipId { get; private set; }
    public Guid RequestedByMembershipId { get; private set; }
    public Guid SeriesId { get; private set; }
    public string DocumentType { get; private set; } = "";
    public string SeriesCode { get; private set; } = "";
    public long SequentialNumber { get; private set; }
    public string DocumentNumber { get; private set; } = "";
    public string SnapshotJson { get; private set; } = "";
    public string RequestFingerprint { get; private set; } = "";
    public string Currency { get; private set; } = "";
    public decimal Total { get; private set; }
    public string Status { get; private set; } = "";
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset? SubmittedAtUtc { get; private set; }
    public string? ProviderReference { get; private set; }
    public string? ProviderStatus { get; private set; }
    public string? LastErrorCode { get; private set; }
    public string? EvidenceSha256 { get; private set; }
    public int AttemptCount { get; private set; }
    public Guid? ClaimToken { get; private set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; private set; }
    public DateTimeOffset? AttemptStartedAtUtc { get; private set; }
    public DateTimeOffset? NextAttemptAtUtc { get; private set; }
    public uint Version { get; private set; }
    public string IdempotencyKey => "medipos-cpe-" + Id.ToString("N");

    public static ElectronicDocument Create(ElectronicDocumentSeries series, Guid branch, Guid sale, Guid seller,
        Guid requester, string snapshot, string fingerprint, string currency, decimal total, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(series);
        foreach (var id in new[] { branch, sale, seller, requester }) ElectronicDocumentRules.Id(id);
        ElectronicDocumentRules.Utc(now); ElectronicDocumentRules.Money(total);
        ElectronicDocumentRules.Require(total > 0 && currency == "PEN", "currency_or_total_invalid");
        ElectronicDocumentRules.Require(fingerprint is not null && fingerprint.Length == 64 && fingerprint.All(char.IsAsciiHexDigitLower), "fingerprint_invalid");
        ElectronicDocumentRules.Require(snapshot is not null && snapshot.Length <= 524288, "snapshot_invalid");
        try
        {
            using var json = JsonDocument.Parse(snapshot!);
            ElectronicDocumentRules.Require(json.RootElement.ValueKind == JsonValueKind.Object, "snapshot_invalid");
        }
        catch (JsonException) { throw new ElectronicDocumentException("snapshot_invalid"); }
        var number = series.Allocate(now);
        return new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = series.TenantId,
            LegalEntityId = series.LegalEntityId,
            BranchId = branch,
            SaleId = sale,
            SellerMembershipId = seller,
            RequestedByMembershipId = requester,
            SeriesId = series.Id,
            SeriesCode = series.Code,
            SequentialNumber = number,
            DocumentType = series.DocumentType,
            DocumentNumber = series.Code + "-" + number.ToString(CultureInfo.InvariantCulture),
            SnapshotJson = snapshot!,
            RequestFingerprint = fingerprint!,
            Currency = currency,
            Total = total,
            Status = ElectronicDocumentStates.Pending,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            NextAttemptAtUtc = now
        };
    }
    // Pending/retry -> reserved -> dispatching -> typed outcome. Only expired pre-dispatch claims can be reclaimed.
    public Guid Claim(DateTimeOffset now, TimeSpan lease)
    {
        Time(now); ElectronicDocumentRules.Require(lease >= TimeSpan.FromSeconds(10) && lease <= TimeSpan.FromMinutes(5), "lease_invalid");
        ElectronicDocumentRules.Require((Status is ElectronicDocumentStates.Pending or ElectronicDocumentStates.Retry) && NextAttemptAtUtc <= now && AttemptCount < DeliveryPolicy.MaximumAttempts, "claim_invalid");
        var expires = Add(now, lease);
        Status = ElectronicDocumentStates.Reserved; ClaimToken = Guid.CreateVersion7(); LeaseExpiresAtUtc = expires; UpdatedAtUtc = now;
        AttemptStartedAtUtc = now; NextAttemptAtUtc = null; AttemptCount++; return ClaimToken.Value;
    }
    public void BeginDispatch(Guid claim, bool saleConfirmed, DateTimeOffset now)
    {
        Fence(claim, now); ElectronicDocumentRules.Require(Status == ElectronicDocumentStates.Reserved, "dispatch_invalid");
        if (!saleConfirmed) { Finish(ElectronicDocumentStates.Manual, "sale_voided_before_dispatch", now); return; }
        // Durable dispatch intent is the cutover. A later commercial void requires a separate fiscal correction.
        Status = ElectronicDocumentStates.Dispatching; SubmittedAtUtc ??= now; UpdatedAtUtc = now;
    }
    public void Complete(Guid claim, DeliveryOutcome outcome, string? reference, string code, string? evidence, DateTimeOffset now)
    {
        Fence(claim, now); ElectronicDocumentRules.Require(Status == ElectronicDocumentStates.Dispatching && Enum.IsDefined(outcome), "completion_invalid");
        ElectronicDocumentRules.Text(code, 64, "provider_code_invalid");
        ElectronicDocumentRules.Require(code.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-'), "provider_code_invalid");
        if (reference is not null) ElectronicDocumentRules.Text(reference, 128, "provider_reference_invalid");
        if (outcome is DeliveryOutcome.Received or DeliveryOutcome.AuthorityAccepted) ElectronicDocumentRules.Require(reference is not null, "provider_reference_required");
        if (outcome == DeliveryOutcome.AuthorityAccepted)
            ElectronicDocumentRules.Require(evidence is not null && evidence.Length == 64 && evidence.All(char.IsAsciiHexDigitLower), "authority_evidence_required");
        var state = outcome switch
        {
            DeliveryOutcome.Received => ElectronicDocumentStates.Received,
            DeliveryOutcome.AuthorityAccepted => ElectronicDocumentStates.Accepted,
            DeliveryOutcome.Rejected => ElectronicDocumentStates.Rejected,
            DeliveryOutcome.Simulated => ElectronicDocumentStates.Simulated,
            DeliveryOutcome.TemporaryNotReceived when AttemptCount < DeliveryPolicy.MaximumAttempts => ElectronicDocumentStates.Retry,
            DeliveryOutcome.TemporaryNotReceived => ElectronicDocumentStates.Manual,
            _ => ElectronicDocumentStates.Uncertain,
        };
        var next = state == ElectronicDocumentStates.Retry ? Add(now, DeliveryPolicy.Backoff(AttemptCount)) : (DateTimeOffset?)null;
        ProviderReference = reference; ProviderStatus = outcome switch { DeliveryOutcome.Received => "received", DeliveryOutcome.AuthorityAccepted => "authority_accepted", DeliveryOutcome.Rejected => "rejected", DeliveryOutcome.TemporaryNotReceived => "not_received", DeliveryOutcome.Simulated => "simulation", _ => "indeterminate" }; EvidenceSha256 = outcome == DeliveryOutcome.AuthorityAccepted ? evidence : null;
        Finish(state, outcome is DeliveryOutcome.Received or DeliveryOutcome.AuthorityAccepted or DeliveryOutcome.Simulated ? null : code, now);
        NextAttemptAtUtc = next;
    }
    public void RecoverExpired(DateTimeOffset now)
    {
        Time(now); ElectronicDocumentRules.Require((Status is ElectronicDocumentStates.Reserved or ElectronicDocumentStates.Dispatching) && LeaseExpiresAtUtc <= now, "lease_not_expired");
        var beforeDispatch = Status == ElectronicDocumentStates.Reserved;
        var next = beforeDispatch && AttemptCount < DeliveryPolicy.MaximumAttempts ? Add(now, DeliveryPolicy.Backoff(AttemptCount)) : (DateTimeOffset?)null;
        Finish(beforeDispatch && AttemptCount < DeliveryPolicy.MaximumAttempts ? ElectronicDocumentStates.Retry : beforeDispatch ? ElectronicDocumentStates.Manual : ElectronicDocumentStates.Uncertain,
            beforeDispatch ? "lease_expired_before_dispatch" : "delivery_unknown_after_dispatch", now);
        NextAttemptAtUtc = next;
    }
    public void Review(bool retry, DateTimeOffset now)
    {
        Time(now);
        // Owner cannot assert acceptance or blindly retry an uncertain/received/terminal result.
        ElectronicDocumentRules.Require(Status is ElectronicDocumentStates.Pending or ElectronicDocumentStates.Retry or ElectronicDocumentStates.Manual or ElectronicDocumentStates.Uncertain or ElectronicDocumentStates.Received, "review_invalid");
        if (retry) ElectronicDocumentRules.Require(Status == ElectronicDocumentStates.Retry && AttemptCount < DeliveryPolicy.MaximumAttempts, "retry_unsafe");
        Finish(retry ? ElectronicDocumentStates.Retry : ElectronicDocumentStates.Manual, retry ? "owner_rescheduled" : "owner_review_required", now);
        if (retry) NextAttemptAtUtc = now;
    }
    private void Fence(Guid claim, DateTimeOffset now)
    { Time(now); ElectronicDocumentRules.Require(claim != Guid.Empty && claim == ClaimToken && now < LeaseExpiresAtUtc, "claim_stale"); }
    private void Time(DateTimeOffset now)
    { ElectronicDocumentRules.Utc(now); ElectronicDocumentRules.Require(now >= UpdatedAtUtc, "time_invalid"); }
    private static DateTimeOffset Add(DateTimeOffset now, TimeSpan duration)
    { try { return now.Add(duration); } catch (ArgumentOutOfRangeException) { throw new ElectronicDocumentException("time_range_invalid"); } }
    private void Finish(string state, string? code, DateTimeOffset now)
    { Status = state; LastErrorCode = code; UpdatedAtUtc = now; ClaimToken = null; LeaseExpiresAtUtc = null; NextAttemptAtUtc = null; }
}
