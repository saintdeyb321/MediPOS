using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.AuditSupport.Persistence;

internal sealed class AuditHistoryReader(MediPosDbContext context) : IAuditHistoryReader
{
    public async Task<IReadOnlyList<AuditHistoryItem>> ReadAsync(AuditHistoryQuery query, CancellationToken token)
    {
        context.SelectTenant(query.TenantId);
        var rows = await context.AuditLogs.AsNoTracking().Where(log => log.OccurredAt >= query.FromUtc && log.OccurredAt < query.ToUtc && (!query.EntityId.HasValue || log.EntityId == query.EntityId))
            .OrderByDescending(log => log.OccurredAt).ThenByDescending(log => log.Id).Skip(query.Offset).Take(query.Limit)
            .Select(log => new { log.Id, log.ActorId, log.Action, log.EntityType, log.EntityId, log.OccurredAt, log.CorrelationId, HasBefore = log.BeforeJson != null, HasAfter = log.AfterJson != null }).ToListAsync(token).ConfigureAwait(false);
        return rows.Select(log => new AuditHistoryItem(log.Id, log.ActorId, AuditCodes.ActionToCode(log.Action), AuditCodes.EntityToCode(log.EntityType), log.EntityId, log.OccurredAt, log.CorrelationId, log.HasBefore, log.HasAfter)).ToArray();
    }
}
