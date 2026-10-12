using MediPOS.Domain.Modules.ElectronicDocuments;

namespace MediPOS.Application.Modules.ElectronicDocuments;

// Explicit bounded execution; no automatic/hosted external delivery in B7.2.
public sealed class RunElectronicDocumentDelivery(IElectronicDeliveryWorkStore queue, IElectronicDocumentProvider provider,
    IElectronicDeliveryIdentity identitySource, IElectronicAuthorityEvidenceVerifier evidenceVerifier, TimeProvider clock)
{
    public async Task<int> HandleAsync(Guid tenantId, int batchSize, CancellationToken token)
    {
        if (tenantId == Guid.Empty || batchSize is < 1 or > 100) throw ElectronicDocumentErrors.Error("batch_invalid");
        var identity = await identitySource.AuthorizeAsync(tenantId, token).ConfigureAwait(false);
        if (identity is null || identity.TenantId != tenantId || identity.PrincipalId == Guid.Empty) throw ElectronicDocumentErrors.Error("technical_identity_denied", MediPOS.Application.Errors.ErrorCategory.Forbidden);
        await queue.RecoverExpiredAsync(identity, clock.GetUtcNow(), batchSize, token).ConfigureAwait(false);
        if (!provider.Capabilities.Operational) return 0; // Preserve pending work until a provider is explicitly configured.
        var processed = 0;
        for (; processed < batchSize; processed++)
        {
            token.ThrowIfCancellationRequested();
            var claim = await queue.ClaimAsync(identity, clock.GetUtcNow(), TimeSpan.FromMinutes(1), token).ConfigureAwait(false);
            if (claim is null) break;
            if (!await queue.BeginDispatchAsync(identity, claim, clock.GetUtcNow(), token).ConfigureAwait(false)) continue;
            ElectronicProviderResult result;
            try
            {
                result = await provider.SubmitAsync(claim, token).ConfigureAwait(false);
                var verified = !provider.Capabilities.Simulation && result?.Outcome == DeliveryOutcome.AuthorityAccepted && result.Evidence is not null &&
                    await evidenceVerifier.VerifyAsync(result.Evidence, token).ConfigureAwait(false);
                result = ElectronicProviderContract.Normalize(result, provider.Capabilities.Simulation, verified);
            }
            catch (Exception error) when (error is HttpRequestException or IOException or TimeoutException or OperationCanceledException)
            { result = ElectronicProviderContract.Unknown("delivery_transport_unknown"); }
            // A cancelled caller cannot erase a committed dispatch intent. Late responses are fenced by the queue.
            using var diagnostic = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await queue.CompleteAsync(identity, claim, result, clock.GetUtcNow(), diagnostic.Token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        }
        return processed;
    }
}
