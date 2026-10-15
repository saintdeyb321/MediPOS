using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;

namespace MediPOS.Application.Modules.AuditSupport;

// Metadata only. Internal before/after JSON, provider payloads and identities are never generic HTTP output.
public sealed record AuditHistoryItem(Guid AuditId, Guid ActorId, string Action, string EntityType, Guid EntityId, DateTimeOffset OccurredAt, string CorrelationId, bool HasBefore, bool HasAfter);
public sealed record AuditHistoryQuery(Guid TenantId, DateTimeOffset? FromUtc = null, DateTimeOffset? ToUtc = null, Guid? EntityId = null, int Offset = 0, int Limit = 50);
public interface IAuditHistoryReader
{
    Task<IReadOnlyList<AuditHistoryItem>> ReadAsync(AuditHistoryQuery query, CancellationToken token);
}
public sealed class GetAuditHistoryHandler(IAuditHistoryReader reader, ITenantOperationAuthorizer authorization, TimeProvider clock)
{
    public async Task<IReadOnlyList<AuditHistoryItem>> HandleAsync(AuditHistoryQuery query, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(query);
        var end = query.ToUtc ?? clock.GetUtcNow(); var start = query.FromUtc ?? end.AddDays(-31);
        if (query.EntityId == Guid.Empty || query.Offset is < 0 or > 10000 || query.Limit is < 1 or > 100 || start.Offset != TimeSpan.Zero || end.Offset != TimeSpan.Zero || start >= end || end - start > TimeSpan.FromDays(31))
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await authorization.RequireAsync(query.TenantId, null, null, TenantOperation.Administration, token).ConfigureAwait(false);
        var result = await reader.ReadAsync(query with { FromUtc = start, ToUtc = end }, token).ConfigureAwait(false);
        await authorization.RequireAsync(query.TenantId, null, null, TenantOperation.Administration, token).ConfigureAwait(false);
        return result;
    }
}
