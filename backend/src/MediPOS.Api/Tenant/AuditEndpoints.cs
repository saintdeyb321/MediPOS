using MediPOS.Application.Modules.AuditSupport;

namespace MediPOS.Api.Tenant;

public static partial class TenantEndpoints
{
    private static void MapAudit(RouteGroupBuilder tenants) => tenants.MapGet("/audit", async (HttpContext c, GetAuditHistoryHandler handler) =>
        TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.OptionalUtc(c, "fromUtc"), TenantHttp.OptionalUtc(c, "toUtc"), TenantHttp.OptionalId(c, "entityId"), TenantHttp.Offset(c), TenantHttp.Limit(c)), c.RequestAborted)))
        .Produces<IReadOnlyList<AuditHistoryItem>>().WithName("tenant.audit.list").WithQuery<TenantOpenApi.AuditHistory>();
}
