using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.OfflineSync;

namespace MediPOS.Application.Modules.OfflineSync;

public sealed record OfflineSaleConflictFilter(Guid? BranchId = null, string? Status = null, string? Type = null,
    DateTimeOffset? FromUtc = null, DateTimeOffset? ToUtc = null, int Offset = 0, int Limit = 50);
public sealed record OfflineSaleConflictDetails(Guid Id, Guid SyncEventId, Guid ClientEventId, Guid DeviceId, Guid BranchId,
    string ConflictType, string ConflictCode, string Status, DateTimeOffset ClientOccurredAtUtc, DateOnly ClientLocalDate,
    string ClientTimeZoneId, DateTimeOffset ReceivedAtUtc, DateTimeOffset DetectedAtUtc, DateTimeOffset? ProcessedAtUtc,
    DateTimeOffset? ResolvedAtUtc, Guid? ResolvedByMembershipId, string? ResolutionCode, string? ResolutionNote,
    IReadOnlyList<OfflineSaleLine> Lines);
public sealed record OfflineSaleConflictPage(IReadOnlyList<OfflineSaleConflictDetails> Items, int Offset, int Limit, bool HasMore);
public sealed record GetOfflineSaleConflictsCommand(Guid TenantId, OfflineSaleConflictFilter Filter);
public sealed record ResolveOfflineSaleConflictCommand(Guid TenantId, Guid ConflictId, string ResolutionCode, string Note);

public interface IOfflineSaleConflictStore
{
    // Called in the pending receipt's existing transaction; does not commit.
    Task AddAsync(OfflineSaleConflict conflict, CancellationToken token);
    Task<OfflineSaleConflict?> FindAsync(Guid tenantId, Guid id, CancellationToken token);
    Task<OfflineSaleConflictPage> ReadAsync(Guid tenantId, Guid? requiredMembershipId, OfflineSaleConflictFilter filter, CancellationToken token);
    Task<IOfflineSaleConflictResolution> BeginResolutionAsync(Guid tenantId, Guid id, CancellationToken token);
}
public interface IOfflineSaleConflictResolution : IAsyncDisposable
{
    OfflineSaleConflict Conflict { get; }
    Task CompleteAsync(AuditLog? audit, CancellationToken token);
}
