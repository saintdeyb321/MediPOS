using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediPOS.Api.Modules.OfflineSync;
using MediPOS.Application.Modules.OfflineSync;
using MediPOS.Application.Tenancy;

namespace MediPOS.Api.Tenant;

public static partial class TenantEndpoints
{
    private static void MapSync(RouteGroupBuilder tenants)
    {
        tenants.MapGet("/branches/{branchId}/sync/devices", async (HttpContext c, GetSyncDevicesHandler handler) =>
            TenantHttp.Response(await handler.HandleAsync(TenantHttp.Tenant(c), TenantHttp.Branch(c), TenantHttp.Offset(c), TenantHttp.Limit(c), c.RequestAborted)))
            .Produces<IReadOnlyList<SyncDeviceDetails>>().WithName("tenant.sync.devices").WithQuery<TenantOpenApi.Page>();
        tenants.MapPost("/branches/{branchId}/sync/devices", async (HttpContext c, RegisterSyncDeviceHandler handler) =>
        {
            var input = await TenantHttp.Read<RegisterDeviceRequest>(c);
            return TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Branch(c), input.DeviceLabel), c.RequestAborted), 201);
        }).Accepts<RegisterDeviceRequest>("application/json").Produces<SyncDeviceDetails>(201).WithName("tenant.sync.register_device");
        tenants.MapPost("/sync/devices/{deviceId}/revoke", async (HttpContext c, RevokeSyncDeviceHandler handler) =>
        {
            await TenantHttp.Read<EmptyRequest>(c);
            return TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Id(c, "deviceId")), c.RequestAborted));
        }).Accepts<EmptyRequest>("application/json").Produces<SyncDeviceDetails>().WithName("tenant.sync.revoke_device");
        tenants.MapPost("/sync/batches", async (HttpContext c, ProcessOfflineSyncBatchHandler handler, ITenantDataContext selection) =>
        {
            var input = await TenantHttp.Read<OfflineSyncBatchRequest>(c);
            selection.SelectTenant(TenantHttp.Tenant(c)); // Selection only; the real handler authorizes user/device/branch again.
            return TenantHttp.Response(await handler.HandleAsync(OfflineSyncBatchHttpMapping.ToCommand(input, selection), c.RequestAborted));
        }).Accepts<OfflineSyncBatchRequest>("application/json").Produces<OfflineSyncBatchResult>()
            .WithMetadata(new TenantRequestPolicy(OfflineSyncBatchHttpMapping.MaximumBodyBytes, false, Sync: true)).WithName("tenant.sync.batch");
        tenants.MapGet("/sync/conflicts", async (HttpContext c, GetOfflineSaleConflictsHandler handler, ITenantDataContext selection) =>
        {
            selection.SelectTenant(TenantHttp.Tenant(c));
            var filter = new OfflineSaleConflictFilter(TenantHttp.OptionalId(c, "branchId"), TenantHttp.Query(c, "status"), TenantHttp.Query(c, "type"), TenantHttp.OptionalUtc(c, "fromUtc"), TenantHttp.OptionalUtc(c, "toUtc"), TenantHttp.Offset(c), TenantHttp.Limit(c));
            return TenantHttp.Response(await handler.HandleAsync(OfflineSaleConflictHttpMapping.Query(filter, selection), c.RequestAborted));
        }).Produces<OfflineSaleConflictPage>().WithName("tenant.sync.conflicts").WithQuery<SyncConflictQuery>();
        tenants.MapPost("/sync/conflicts/{conflictId}/resolve", async (HttpContext c, ResolveOfflineSaleConflictHandler handler, ITenantDataContext selection) =>
        {
            var input = await TenantHttp.Read<ResolveOfflineSaleConflictRequest>(c); selection.SelectTenant(TenantHttp.Tenant(c));
            var result = await handler.HandleAsync(OfflineSaleConflictHttpMapping.Resolve(TenantHttp.Id(c, "conflictId"), input, selection), c.RequestAborted);
            return TenantHttp.Response(new ConflictResolutionResponse(result.Id, result.Status, result.ResolutionCode, result.ResolvedAtUtc, result.ResolvedByMembershipId));
        }).Accepts<ResolveOfflineSaleConflictRequest>("application/json").Produces<ConflictResolutionResponse>().WithName("tenant.sync.resolve_conflict");
        tenants.MapGet("/sync/devices/{deviceId}/catalog/snapshot", async (HttpContext c, GetBranchCatalogSnapshotHandler handler, ITenantDataContext selection) =>
        {
            selection.SelectTenant(TenantHttp.Tenant(c));
            var input = new BranchCatalogRequest(TenantHttp.Id(c, "deviceId"), TenantHttp.Query(c, "cursor"), TenantHttp.BoundedCount(c, "pageSize", 100, 1, BranchCatalogPolicy.MaximumPageSize));
            return TenantHttp.Response(await handler.HandleAsync(BranchCatalogHttpMapping.Snapshot(input, selection) with { BranchId = TenantHttp.OptionalId(c, "branchId") }, c.RequestAborted));
        }).Produces<BranchCatalogPage>().WithName("tenant.sync.catalog_snapshot").WithQuery<SyncCatalogQuery>();
        tenants.MapGet("/sync/devices/{deviceId}/catalog/changes", async (HttpContext c, GetBranchCatalogChangesHandler handler, ITenantDataContext selection) =>
        {
            selection.SelectTenant(TenantHttp.Tenant(c));
            var input = new BranchCatalogRequest(TenantHttp.Id(c, "deviceId"), TenantHttp.Query(c, "cursor"), TenantHttp.BoundedCount(c, "pageSize", 100, 1, BranchCatalogPolicy.MaximumPageSize));
            return TenantHttp.Response(await handler.HandleAsync(BranchCatalogHttpMapping.Changes(input, selection) with { BranchId = TenantHttp.OptionalId(c, "branchId") }, c.RequestAborted));
        }).Produces<BranchCatalogPage>().WithName("tenant.sync.catalog_changes").WithQuery<SyncCatalogQuery>();
    }
    public sealed record RegisterDeviceRequest([property: JsonRequired] string DeviceLabel);
    public sealed record ConflictResolutionResponse(Guid ConflictId, string Status, string? ResolutionCode, DateTimeOffset? ResolvedAtUtc, Guid? ResolvedByMembershipId);
    public sealed class SyncCatalogQuery { public string? Cursor { get; init; } [Range(1, BranchCatalogPolicy.MaximumPageSize)] public int? PageSize { get; init; } public Guid? BranchId { get; init; } }
    public sealed class SyncConflictQuery : TenantOpenApi.Page { public Guid? BranchId { get; init; } public string? Status { get; init; } public string? Type { get; init; } public DateTimeOffset? FromUtc { get; init; } public DateTimeOffset? ToUtc { get; init; } }
}
