using MediPOS.Domain.Modules.ElectronicDocuments;

namespace MediPOS.Application.Modules.ElectronicDocuments;

public sealed record ProviderCapabilities(bool Operational, bool Simulation, bool CanQueryByIdempotencyKey);
public sealed record AuthorityEvidence(string Authority, string EvidenceSha256, string VerificationReference);
public sealed record ElectronicProviderResult(DeliveryOutcome Outcome, string Code, string? Reference = null, AuthorityEvidence? Evidence = null);
public sealed record ElectronicDeliveryClaim(Guid TenantId, Guid DocumentId, Guid Token, string IdempotencyKey, string SnapshotJson, string DocumentNumber);
public sealed record ElectronicDeliveryIdentity(Guid TenantId, Guid PrincipalId);

public interface IElectronicDocumentProvider
{
    ProviderCapabilities Capabilities { get; }
    Task<ElectronicProviderResult> SubmitAsync(ElectronicDeliveryClaim claim, CancellationToken token);
    Task<ElectronicProviderResult> QueryAsync(string idempotencyKey, CancellationToken token);
}
// Production activation must supply a trusted technical principal and evidence verifier; neither is a client input.
public interface IElectronicDeliveryIdentity
{ Task<ElectronicDeliveryIdentity?> AuthorizeAsync(Guid tenantId, CancellationToken token); }
public interface IElectronicAuthorityEvidenceVerifier
{ Task<bool> VerifyAsync(AuthorityEvidence evidence, CancellationToken token); }
public interface IElectronicDeliveryWorkStore
{
    Task<int> RecoverExpiredAsync(ElectronicDeliveryIdentity identity, DateTimeOffset now, int limit, CancellationToken token);
    Task<ElectronicDeliveryClaim?> ClaimAsync(ElectronicDeliveryIdentity identity, DateTimeOffset now, TimeSpan lease, CancellationToken token);
    Task<bool> BeginDispatchAsync(ElectronicDeliveryIdentity identity, ElectronicDeliveryClaim claim, DateTimeOffset now, CancellationToken token);
    Task<bool> CompleteAsync(ElectronicDeliveryIdentity identity, ElectronicDeliveryClaim claim, ElectronicProviderResult result, DateTimeOffset now, CancellationToken token);
}

public static class ElectronicProviderContract
{
    public static ElectronicProviderResult Normalize(ElectronicProviderResult? result, bool simulation, bool verifiedEvidence)
    {
        if (result is null || !Enum.IsDefined(result.Outcome)) return Unknown("provider_result_invalid");
        if (!SafeCode(result.Code, 64) || result.Reference is not null && !SafeCode(result.Reference, 128)) return Unknown("provider_metadata_invalid");
        if ((result.Outcome is DeliveryOutcome.Received or DeliveryOutcome.AuthorityAccepted) && result.Reference is null) return Unknown("provider_reference_missing");
        if (simulation && (result.Outcome is DeliveryOutcome.Received or DeliveryOutcome.AuthorityAccepted or DeliveryOutcome.Simulated))
            return new(DeliveryOutcome.Simulated, "simulation_only", result.Reference);
        if (!simulation && result.Outcome == DeliveryOutcome.Simulated) return Unknown("simulation_not_authority");
        if (result.Outcome == DeliveryOutcome.AuthorityAccepted && (!verifiedEvidence || result.Evidence is null || result.Evidence.EvidenceSha256 is null || result.Evidence.EvidenceSha256.Length != 64 || !result.Evidence.EvidenceSha256.All(char.IsAsciiHexDigitLower)))
            return Unknown("authority_evidence_unverified");
        return result with { Evidence = result.Outcome == DeliveryOutcome.AuthorityAccepted ? result.Evidence : null };
    }
    public static ElectronicProviderResult Unknown(string code) => new(DeliveryOutcome.Indeterminate, code);
    private static bool SafeCode(string? text, int max) => !string.IsNullOrWhiteSpace(text) && text.Length <= max && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-');
}
