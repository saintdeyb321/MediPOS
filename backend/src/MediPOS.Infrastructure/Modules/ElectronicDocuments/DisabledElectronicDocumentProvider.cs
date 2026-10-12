using MediPOS.Application.Modules.ElectronicDocuments;
using MediPOS.Domain.Modules.ElectronicDocuments;

namespace MediPOS.Infrastructure.Modules.ElectronicDocuments;

internal sealed class DisabledElectronicDocumentProvider : IElectronicDocumentProvider
{
    public ProviderCapabilities Capabilities { get; } = new(false, false, false);
    public Task<ElectronicProviderResult> SubmitAsync(ElectronicDeliveryClaim claim, CancellationToken token)
    { token.ThrowIfCancellationRequested(); return Task.FromResult(new ElectronicProviderResult(DeliveryOutcome.TemporaryNotReceived, "provider_not_configured")); }
    public Task<ElectronicProviderResult> QueryAsync(string idempotencyKey, CancellationToken token)
    { token.ThrowIfCancellationRequested(); return Task.FromResult(ElectronicProviderContract.Unknown("provider_query_unavailable")); }
}
internal sealed class DisabledElectronicDeliveryIdentity : IElectronicDeliveryIdentity
{
    public Task<ElectronicDeliveryIdentity?> AuthorizeAsync(Guid tenantId, CancellationToken token)
    { token.ThrowIfCancellationRequested(); return Task.FromResult<ElectronicDeliveryIdentity?>(null); }
}
internal sealed class DisabledElectronicEvidenceVerifier : IElectronicAuthorityEvidenceVerifier
{
    public Task<bool> VerifyAsync(AuthorityEvidence evidence, CancellationToken token)
    { token.ThrowIfCancellationRequested(); return Task.FromResult(false); }
}
