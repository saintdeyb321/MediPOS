using System.Data;
using MediPOS.Application.Modules.OfflineSync;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.OfflineSync;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MediPOS.Infrastructure.Modules.OfflineSync.Persistence;

internal sealed class SyncDeviceStore(MediPosDbContext context) : ISyncDeviceStore
{
    public Task<SyncDevice?> FindAsync(Guid tenantId, Guid deviceId, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        return context.SyncDevices.AsNoTracking().SingleOrDefaultAsync(device => device.TenantId == tenantId && device.Id == deviceId, token);
    }

    public async Task<ISyncDeviceScope> BeginAsync(Guid tenantId, Guid? deviceId, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        SyncPersistence.RequireClean(context);
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token).ConfigureAwait(false);
        try
        {
            var rows = deviceId.HasValue ? await context.SyncDevices.FromSqlInterpolated($"SELECT * FROM sync_devices WHERE tenant_id = {tenantId} AND id = {deviceId.Value} FOR UPDATE")
                .ToArrayAsync(token).ConfigureAwait(false) : [];
            return new DeviceScope(context, transaction, rows.SingleOrDefault());
        }
        catch { await transaction.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private sealed class DeviceScope(MediPosDbContext context, IDbContextTransaction transaction, SyncDevice? device)
        : SyncPersistence(context, transaction), ISyncDeviceScope
    {
        public SyncDevice? Device => device;
        public async Task SaveAsync(SyncDevice changed, AuditLog audit, CancellationToken token)
        {
            RequireOpen();
            if (device is not null && !ReferenceEquals(device, changed)) throw new InvalidOperationException("Device must be locked in this scope.");
            if (Context.Entry(changed).State == EntityState.Detached) Context.SyncDevices.Add(changed);
            Context.AddAudit(audit, changed.TenantId, device is null ? AuditAction.SyncDeviceRegistered : AuditAction.SyncDeviceRevoked, changed.Id);
            await Context.SaveAuditedChangesAsync(token).ConfigureAwait(false);
        }
    }
}

internal sealed class SyncEventInbox(MediPosDbContext context) : ISyncEventInbox
{
    public async Task<ISyncEventScope> BeginAsync(Guid tenantId, Guid deviceId, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        SyncPersistence.RequireClean(context);
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token).ConfigureAwait(false);
        try
        {
            // SHARE permits independent events on one device; revocation's UPDATE waits for active reservations.
            var rows = await context.SyncDevices.FromSqlInterpolated($"SELECT * FROM sync_devices WHERE tenant_id = {tenantId} AND id = {deviceId} FOR SHARE")
                .AsNoTracking().ToArrayAsync(token).ConfigureAwait(false);
            return new InboxScope(context, transaction, tenantId, rows.SingleOrDefault());
        }
        catch { await transaction.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private sealed class InboxScope(MediPosDbContext context, IDbContextTransaction transaction, Guid tenantId, SyncDevice? device)
        : SyncPersistence(context, transaction), ISyncEventScope
    {
        private SyncEvent? _reserved;
        public SyncDevice? Device => device;
        public async Task<SyncEvent> ReserveOrReadAsync(SyncEvent candidate, CancellationToken token)
        {
            RequireOpen();
            if (_reserved is not null || device is null || device.Status != "active" || candidate.TenantId != tenantId || candidate.DeviceId != device.Id)
                throw new InvalidOperationException("One event on an active scoped device is required.");
            // UNIQUE arbitrates concurrent identities. Separate SELECT uses a new ReadCommitted snapshot after the winner commits.
            await Context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO sync_events (id, tenant_id, device_id, event_id, event_type, schema_version, payload_hash, payload,
                    client_occurred_at_utc, client_local_date, client_time_zone_id, received_at_utc, processing_status)
                VALUES ({candidate.Id}, {candidate.TenantId}, {candidate.DeviceId}, {candidate.EventId}, {candidate.EventType},
                    {candidate.SchemaVersion}, {candidate.PayloadHash}, {candidate.Payload}, {candidate.ClientOccurredAtUtc},
                    {candidate.ClientLocalDate}, {candidate.ClientTimeZoneId}, {candidate.ReceivedAtUtc}, 'pending')
                ON CONFLICT (tenant_id, event_id) DO NOTHING
                """, token).ConfigureAwait(false);
            var rows = await Context.SyncEvents.FromSqlInterpolated($"SELECT * FROM sync_events WHERE tenant_id = {tenantId} AND event_id = {candidate.EventId} FOR UPDATE")
                .ToArrayAsync(token).ConfigureAwait(false);
            _reserved = rows.Single();
            return _reserved;
        }

        public async Task SaveResultAsync(SyncEvent syncEvent, CancellationToken token)
        {
            RequireOpen();
            if (!ReferenceEquals(_reserved, syncEvent)) throw new InvalidOperationException("Finalize only the event locked by this scope.");
            await Context.SaveChangesAsync(token).ConfigureAwait(false);
        }
    }
}

internal abstract class SyncPersistence(MediPosDbContext context, IDbContextTransaction transaction) : IAsyncDisposable
{
    private bool _completed;
    protected MediPosDbContext Context { get; } = context;
    internal static void RequireClean(MediPosDbContext context)
    {
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Open sync transactions before other mutations.");
        foreach (var entry in context.ChangeTracker.Entries<SyncDevice>().ToArray()) entry.State = EntityState.Detached;
        foreach (var entry in context.ChangeTracker.Entries<SyncEvent>().ToArray()) entry.State = EntityState.Detached;
    }
    protected void RequireOpen()
    {
        if (_completed || Context.Database.CurrentTransaction != transaction) throw new InvalidOperationException("An open sync transaction is required.");
    }
    public async Task CompleteAsync(CancellationToken token)
    {
        RequireOpen();
        if (Context.ChangeTracker.HasChanges()) throw new InvalidOperationException("Persist all changes before completing the transaction.");
        await transaction.CommitAsync(token).ConfigureAwait(false); _completed = true;
    }
    public async ValueTask DisposeAsync()
    {
        await transaction.DisposeAsync().ConfigureAwait(false);
        if (!_completed) Context.ChangeTracker.Clear();
    }
}
