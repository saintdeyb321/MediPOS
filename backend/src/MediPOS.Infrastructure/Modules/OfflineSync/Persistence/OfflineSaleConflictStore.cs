using System.Data;
using MediPOS.Application.Modules.OfflineSync;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.OfflineSync;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MediPOS.Infrastructure.Modules.OfflineSync.Persistence;

internal sealed class OfflineSaleConflictStore(MediPosDbContext context) : IOfflineSaleConflictStore
{
    public async Task AddAsync(OfflineSaleConflict conflict, CancellationToken token)
    {
        context.SelectTenant(conflict.TenantId);
        if (context.Database.CurrentTransaction is null) throw new InvalidOperationException("Conflict belongs to the receipt transaction.");
        context.OfflineSaleConflicts.Add(conflict);
        await context.SaveChangesAsync(token).ConfigureAwait(false);
    }
    public Task<OfflineSaleConflict?> FindAsync(Guid tenantId, Guid id, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        return context.OfflineSaleConflicts.AsNoTracking().SingleOrDefaultAsync(value => value.TenantId == tenantId && value.Id == id, token);
    }
    public async Task<OfflineSaleConflictPage> ReadAsync(Guid tenantId, Guid? requiredMembershipId, OfflineSaleConflictFilter filter, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        var query = from conflict in context.OfflineSaleConflicts.AsNoTracking()
                    join receipt in context.SyncEvents.AsNoTracking() on new { conflict.TenantId, Id = conflict.SyncEventId } equals new { receipt.TenantId, receipt.Id }
                    join device in context.SyncDevices.AsNoTracking() on new { conflict.TenantId, Id = conflict.DeviceId } equals new { device.TenantId, device.Id }
                    where conflict.TenantId == tenantId && (!requiredMembershipId.HasValue || device.RegisteredByMembershipId == requiredMembershipId) &&
                        (!filter.BranchId.HasValue || conflict.BranchId == filter.BranchId) && (filter.Status == null || conflict.Status == filter.Status) &&
                        (filter.Type == null || conflict.ConflictType == filter.Type) && (!filter.FromUtc.HasValue || conflict.DetectedAtUtc >= filter.FromUtc) &&
                        (!filter.ToUtc.HasValue || conflict.DetectedAtUtc < filter.ToUtc)
                    orderby conflict.DetectedAtUtc descending, conflict.Id descending
                    select new { Conflict = conflict, Receipt = receipt };
        var rows = await query.Skip(filter.Offset).Take(filter.Limit + 1).ToArrayAsync(token).ConfigureAwait(false);
        var details = rows.Take(filter.Limit).Select(row => new OfflineSaleConflictDetails(row.Conflict.Id, row.Receipt.Id,
            row.Receipt.EventId, row.Receipt.DeviceId, row.Conflict.BranchId, row.Conflict.ConflictType, row.Conflict.ConflictCode,
            row.Conflict.Status, row.Receipt.ClientOccurredAtUtc, row.Receipt.ClientLocalDate, row.Receipt.ClientTimeZoneId,
            row.Receipt.ReceivedAtUtc, row.Conflict.DetectedAtUtc, row.Receipt.ProcessedAtUtc, row.Conflict.ResolvedAtUtc,
            row.Conflict.ResolvedByMembershipId, row.Conflict.ResolutionCode, row.Conflict.ResolutionNote,
            OfflineSalePayloadPolicy.Read(row.Receipt.Payload).Lines)).ToArray();
        return new(details, filter.Offset, filter.Limit, rows.Length > filter.Limit);
    }
    public async Task<IOfflineSaleConflictResolution> BeginResolutionAsync(Guid tenantId, Guid id, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        SyncPersistence.RequireClean(context);
        foreach (var entry in context.ChangeTracker.Entries<OfflineSaleConflict>().ToArray()) entry.State = EntityState.Detached;
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token).ConfigureAwait(false);
        try
        {
            var rows = await context.OfflineSaleConflicts.FromSqlInterpolated($"SELECT * FROM offline_sale_conflicts WHERE tenant_id = {tenantId} AND id = {id} FOR UPDATE").ToArrayAsync(token).ConfigureAwait(false);
            return new ResolutionScope(context, transaction, rows.Single());
        }
        catch { await transaction.DisposeAsync().ConfigureAwait(false); throw; }
    }
    private sealed class ResolutionScope(MediPosDbContext context, IDbContextTransaction transaction, OfflineSaleConflict conflict)
        : SyncPersistence(context, transaction), IOfflineSaleConflictResolution
    {
        public OfflineSaleConflict Conflict => conflict;
        public async Task CompleteAsync(AuditLog? audit, CancellationToken token)
        {
            RequireOpen();
            if (audit is not null) Context.AddAudit(audit, conflict.TenantId, AuditAction.OfflineSaleConflictResolved, conflict.Id);
            await Context.SaveAuditedChangesAsync(token).ConfigureAwait(false);
            await base.CompleteAsync(token).ConfigureAwait(false);
        }
    }
}
