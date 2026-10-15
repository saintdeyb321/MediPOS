using System.Reflection;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Domain.Modules.AuditSupport;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.UnitTests.Api;

public sealed partial class TenantHttpTestState
{
    private void ConfigureAudit(IServiceCollection services) => services.AddScoped<IAuditHistoryReader>(_ =>
    { var proxy = DispatchProxy.Create<IAuditHistoryReader, Port>(); ((Port)(object)proxy).State = this; return proxy; });
    private Task<IReadOnlyList<AuditHistoryItem>> ReadAudit(object?[] args)
    {
        var query = (AuditHistoryQuery)args[0]!;
        var rows = Audits.Where(log => log.TenantId == query.TenantId && log.OccurredAt >= query.FromUtc && log.OccurredAt < query.ToUtc && (!query.EntityId.HasValue || log.EntityId == query.EntityId))
            .OrderByDescending(log => log.OccurredAt).ThenByDescending(log => log.Id).Skip(query.Offset).Take(query.Limit)
            .Select(log => new AuditHistoryItem(log.Id, log.ActorId, AuditCodes.ActionToCode(log.Action), AuditCodes.EntityToCode(log.EntityType), log.EntityId, log.OccurredAt, log.CorrelationId, log.BeforeJson is not null, log.AfterJson is not null)).ToArray();
        AfterPrivateRead?.Invoke(); return Task.FromResult<IReadOnlyList<AuditHistoryItem>>(rows);
    }
}
