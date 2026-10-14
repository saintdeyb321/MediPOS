namespace MediPOS.Application.Modules.TenancyLicensing.CreateTenant;

// The reservation and the existing creation store must share one database transaction.
public interface ITenantCreationRequests
{
    Task<ITenantCreationRequest> BeginAsync(Guid actorId, Guid key, byte[] payloadHash, CancellationToken cancellationToken);
}

public interface ITenantCreationRequest : IAsyncDisposable
{
    LicenseDetails? OriginalResult { get; }
    Task CompleteAsync(LicenseDetails result, CancellationToken cancellationToken);
}

public sealed record TenantCreationResult(LicenseDetails License, bool Replayed);
