using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.OfflineSync;

namespace MediPOS.Application.Modules.OfflineSync;

public sealed class GetOfflineSaleConflictsHandler(ResolveAccessContextHandler resolver, IOfflineSaleConflictStore store, TimeProvider clock)
{
    public async Task<OfflineSaleConflictPage> HandleAsync(GetOfflineSaleConflictsCommand command, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(command);
        var filter = command.Filter;
        if (filter is null || filter.BranchId == Guid.Empty || filter.Offset < 0 || filter.Limit is < 1 or > 100 ||
            filter.Status is not (null or "open" or "resolved") || (filter.Type is not null && !OfflineSaleConflict.IsType(filter.Type)) ||
            (filter.FromUtc.HasValue && filter.FromUtc.Value.Offset != TimeSpan.Zero) || (filter.ToUtc.HasValue && filter.ToUtc.Value.Offset != TimeSpan.Zero) ||
            (filter.FromUtc.HasValue && filter.ToUtc.HasValue && filter.FromUtc >= filter.ToUtc)) throw SyncAccess.Error("sync.conflict_filter_invalid");
        var access = await SyncAccess.RequireAsync(resolver, command.TenantId, filter.BranchId, clock, token).ConfigureAwait(false);
        var result = await store.ReadAsync(access.TenantId, access.Role == TenantRole.Owner ? null : access.MembershipId, filter, token).ConfigureAwait(false);
        var current = await SyncAccess.RequireAsync(resolver, command.TenantId, filter.BranchId, clock, token).ConfigureAwait(false);
        if (current.MembershipId != access.MembershipId || current.Role != access.Role) throw SyncAccess.Error("sync.conflict_denied", ErrorCategory.Forbidden);
        return result;
    }
}

// B5.3 resolutions intentionally create no sale, payment or stock operation. Only Owner resolves.
public sealed class ResolveOfflineSaleConflictHandler(ResolveAccessContextHandler resolver, IOfflineSaleConflictStore store, TimeProvider clock)
{
    public async Task<OfflineSaleConflict> HandleAsync(ResolveOfflineSaleConflictCommand command, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.ConflictId == Guid.Empty) throw SyncAccess.Error("sync.conflict_resolution_invalid");
        await SyncAccess.RequireAsync(resolver, command.TenantId, null, clock, token, allowBranchLookup: true).ConfigureAwait(false);
        var found = await store.FindAsync(command.TenantId, command.ConflictId, token).ConfigureAwait(false)
            ?? throw SyncAccess.Error("sync.conflict_not_found", ErrorCategory.NotFound);
        var access = await SyncAccess.RequireAsync(resolver, command.TenantId, found.BranchId, clock, token).ConfigureAwait(false);
        if (access.Role != TenantRole.Owner) throw SyncAccess.Error("sync.conflict_denied", ErrorCategory.Forbidden);
        await using var scope = await store.BeginResolutionAsync(command.TenantId, found.Id, token).ConfigureAwait(false);
        var conflict = scope.Conflict;
        if (conflict.TenantId != access.TenantId || conflict.BranchId != access.BranchId) throw SyncAccess.Error("sync.conflict_denied", ErrorCategory.Forbidden);
        bool changed;
        try { changed = conflict.Resolve(access.MembershipId, command.ResolutionCode, command.Note, clock.GetUtcNow()); }
        catch (ArgumentException) { throw SyncAccess.Error("sync.conflict_resolution_invalid"); }
        catch (InvalidOperationException) { throw SyncAccess.Error("sync.conflict_already_resolved", ErrorCategory.Conflict); }
        var current = await SyncAccess.RequireAsync(resolver, command.TenantId, found.BranchId, clock, token).ConfigureAwait(false);
        if (current.Role != TenantRole.Owner || current.MembershipId != access.MembershipId) throw SyncAccess.Error("sync.conflict_denied", ErrorCategory.Forbidden);
        var audit = changed ? AuditTrail.Record(conflict.TenantId, current.UserId, AuditAction.OfflineSaleConflictResolved, conflict.Id, conflict.ResolvedAtUtc!.Value,
            JsonSerializer.Serialize(new { status = "open" }), JsonSerializer.Serialize(new { conflict.SyncEventId, conflict.BranchId, conflict.ResolutionCode, status = conflict.Status })) : null;
        await scope.CompleteAsync(audit, token).ConfigureAwait(false);
        return conflict;
    }
}
