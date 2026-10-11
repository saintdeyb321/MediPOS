using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.OfflineSync;

namespace MediPOS.Application.Modules.OfflineSync;

public sealed class GetBranchCatalogSnapshotHandler(BranchCatalogSyncQueries queries)
{
    public Task<BranchCatalogPage> HandleAsync(GetBranchCatalogSnapshotCommand command, CancellationToken token)
    { ArgumentNullException.ThrowIfNull(command); return queries.ReadAsync(command.TenantId, command.DeviceId, command.BranchId, command.Cursor, command.PageSize, true, token); }
}
public sealed class GetBranchCatalogChangesHandler(BranchCatalogSyncQueries queries)
{
    public Task<BranchCatalogPage> HandleAsync(GetBranchCatalogChangesCommand command, CancellationToken token)
    { ArgumentNullException.ThrowIfNull(command); return queries.ReadAsync(command.TenantId, command.DeviceId, command.BranchId, command.Cursor, command.PageSize, false, token); }
}

public sealed class BranchCatalogSyncQueries(ResolveAccessContextHandler resolver, ISyncDeviceStore devices, IBranchCatalogSyncReader reader, TimeProvider clock)
{
    public async Task<BranchCatalogPage> ReadAsync(Guid tenant, Guid deviceId, Guid? requestedBranch, string? encoded, int pageSize, bool snapshot, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (tenant == Guid.Empty || deviceId == Guid.Empty || requestedBranch == Guid.Empty) throw SyncAccess.Error("sync.identity_invalid");
        if (pageSize is < 1 or > BranchCatalogPolicy.MaximumPageSize) throw SyncAccess.Error("sync.catalog_page_size_invalid");
        var device = await RequireDeviceAsync(tenant, deviceId, requestedBranch, token).ConfigureAwait(false);
        var now = SyncEvent.ServerTime(clock.GetUtcNow()); BranchCatalogPage page;
        // End the repeatable-read snapshot BEFORE rechecking access: an old MVCC view cannot validate revocation.
        await using (var session = await reader.OpenAsync(tenant, device.BranchId, token).ConfigureAwait(false))
        {
            var cursor = encoded is null ? null : session.DecodeCursor(encoded);
            if ((encoded is not null && (cursor is null || !BranchCatalogPolicy.IsValid(cursor, tenant, device.BranchId, deviceId, pageSize,
                session.CurrentVersion, session.MinimumRetainedVersion, now))) || (!snapshot && cursor is null)) page = Resync(device.BranchId, now);
            else if (snapshot) page = await SnapshotAsync(session, cursor, tenant, device, pageSize, now, token).ConfigureAwait(false);
            else page = await ChangesAsync(session, cursor!, device.BranchId, pageSize, now, token).ConfigureAwait(false);
        }
        await RequireDeviceAsync(tenant, deviceId, device.BranchId, token).ConfigureAwait(false);
        var finishedAt = SyncEvent.ServerTime(clock.GetUtcNow());
        return BranchCatalogPolicy.BusinessDate(finishedAt) == BranchCatalogPolicy.BusinessDate(now) ? page : Resync(device.BranchId, finishedAt);
    }

    private async Task<SyncDevice> RequireDeviceAsync(Guid tenant, Guid deviceId, Guid? branch, CancellationToken token)
    {
        await SyncAccess.RequireAsync(resolver, tenant, null, clock, token, allowBranchLookup: true).ConfigureAwait(false);
        var device = await devices.FindAsync(tenant, deviceId, token).ConfigureAwait(false) ?? throw SyncAccess.Error("sync.device_not_found", ErrorCategory.NotFound);
        if (branch.HasValue && branch != device.BranchId) throw SyncAccess.Error("sync.device_denied", ErrorCategory.Forbidden);
        var access = await SyncAccess.RequireAsync(resolver, tenant, device.BranchId, clock, token).ConfigureAwait(false);
        SyncAccess.RequireDevice(device, access);
        if (device.RegisteredByMembershipId != access.MembershipId) throw SyncAccess.Error("sync.device_denied", ErrorCategory.Forbidden);
        return device;
    }

    private static async Task<BranchCatalogPage> SnapshotAsync(IBranchCatalogReadSession session, BranchCatalogCursor? cursor,
        Guid tenant, SyncDevice device, int size, DateTimeOffset now, CancellationToken token)
    {
        if (cursor is not null && (cursor.Kind != "snapshot" || cursor.ThroughVersion != session.CurrentVersion)) return Resync(device.BranchId, now);
        cursor ??= new(1, tenant, device.BranchId, device.Id, "snapshot", 0, session.CurrentVersion, null, size,
            BranchCatalogPolicy.BusinessDate(now), now, now.AddDays(7));
        var rows = await session.SnapshotAsync(cursor.AfterProductId, size + 1, token).ConfigureAwait(false);
        var hasMore = rows.Count > size; var selected = rows.Take(size).ToArray();
        var next = hasMore ? cursor with { AfterProductId = selected[^1].EntityId } :
            cursor with { Kind = "checkpoint", AfterProductId = null, AfterVersion = cursor.ThroughVersion };
        return new(1, device.BranchId, cursor.WindowAtUtc, selected.Select(row => BranchCatalogPolicy.Project(row with { Version = cursor.ThroughVersion }, cursor.BusinessDate, cursor.WindowAtUtc)).ToArray(),
            session.EncodeCursor(next), hasMore, true, false, cursor.WindowAtUtc);
    }

    private static async Task<BranchCatalogPage> ChangesAsync(IBranchCatalogReadSession session, BranchCatalogCursor cursor, Guid branch,
        int size, DateTimeOffset now, CancellationToken token)
    {
        if (cursor.Kind is not ("checkpoint" or "delta")) return Resync(branch, now);
        // A checkpoint opens a new committed window; a delta continuation pins its original upper bound.
        // New commits on a checkpoint retry can extend the immutable prefix, never replace or skip it.
        if (cursor.Kind == "checkpoint") cursor = cursor with { Kind = "delta", ThroughVersion = session.CurrentVersion, WindowAtUtc = now, ExpiresAtUtc = now.AddDays(7) };
        var rows = await session.ChangesAsync(cursor.AfterVersion, cursor.ThroughVersion, size + 1, token).ConfigureAwait(false);
        var expected = cursor.AfterVersion;
        foreach (var row in rows) if (row.Version != ++expected || row.Version > cursor.ThroughVersion) return Resync(branch, now);
        if (rows.Count == 0 && cursor.AfterVersion < cursor.ThroughVersion) return Resync(branch, now);
        var hasMore = rows.Count > size; var selected = rows.Take(size).ToArray();
        var after = selected.Length == 0 ? cursor.AfterVersion : selected[^1].Version;
        if (!hasMore && after != cursor.ThroughVersion) return Resync(branch, now);
        var next = cursor with { AfterVersion = after, Kind = hasMore ? "delta" : "checkpoint" };
        return new(1, branch, cursor.WindowAtUtc, selected.Select(row => BranchCatalogPolicy.Project(row, cursor.BusinessDate, cursor.WindowAtUtc)).ToArray(),
            session.EncodeCursor(next), hasMore, false, false, cursor.WindowAtUtc);
    }
    private static BranchCatalogPage Resync(Guid branch, DateTimeOffset now) => new(1, branch, now, [], null, false, false, true, now) { ResultCode = BranchCatalogPolicy.ResyncCode };
}
