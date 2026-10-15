using System.Text.Json.Serialization;
using MediPOS.Application.Modules.Inventory;
using MediPOS.Application.Modules.Inventory.AdjustStock;
using MediPOS.Application.Modules.Inventory.GetExpiringLots;
using MediPOS.Application.Modules.Inventory.SetBranchProductStockThreshold;

namespace MediPOS.Api.Tenant;

public static partial class TenantEndpoints
{
    private static void MapInventory(RouteGroupBuilder tenants)
    {
        tenants.MapGet("/branches/{branchId}/inventory/expiring-lots", async (HttpContext c, InventoryDirectoryQueries queries) =>
            TenantHttp.Response(await queries.ExpiringAsync(TenantHttp.Tenant(c), TenantHttp.Branch(c), TenantHttp.OptionalId(c, "productId"), TenantHttp.Offset(c), TenantHttp.Limit(c), c.RequestAborted)))
            .Produces<IReadOnlyList<ExpiringLotDetails>>().WithName("tenant.inventory.expiring_lots").WithQuery<TenantOpenApi.ProductPage>();
        tenants.MapPut("/branches/{branchId}/products/{productId}/stock-threshold", async (HttpContext c, SetBranchProductStockThresholdHandler handler) =>
        {
            var input = await TenantHttp.Read<StockThresholdRequest>(c);
            return TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Branch(c), TenantHttp.Id(c, "productId"), input.MinimumStockBase), c.RequestAborted));
        }).Accepts<StockThresholdRequest>("application/json").Produces<BranchStockThresholdDetails>().WithName("tenant.inventory.set_threshold");
        tenants.MapGet("/branches/{branchId}/inventory/stock", async (HttpContext c, InventoryDirectoryQueries queries) =>
            TenantHttp.Response(await queries.StockAsync(TenantHttp.Tenant(c), TenantHttp.Branch(c), TenantHttp.OptionalId(c, "productId"), TenantHttp.Offset(c), TenantHttp.Limit(c), c.RequestAborted)))
            .Produces<IReadOnlyList<ProductStockDetails>>().WithName("tenant.inventory.stock").WithQuery<TenantOpenApi.ProductPage>();
        tenants.MapGet("/branches/{branchId}/inventory/lots", async (HttpContext c, InventoryDirectoryQueries queries) =>
            TenantHttp.Response(await queries.LotsAsync(TenantHttp.Tenant(c), TenantHttp.Branch(c), TenantHttp.OptionalId(c, "productId"), TenantHttp.Offset(c), TenantHttp.Limit(c), c.RequestAborted)))
            .Produces<IReadOnlyList<LotStockDetails>>().WithName("tenant.inventory.lots").WithQuery<TenantOpenApi.ProductPage>();
        tenants.MapPost("/inventory/lots/{lotId}/adjustments", async (HttpContext c, AdjustStockHandler handler) =>
        {
            var input = await TenantHttp.Read<StockAdjustmentRequest>(c);
            return TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Id(c, "lotId"), input.QuantityDeltaBase, input.Reason, TenantHttp.Actor(c)), c.RequestAborted));
        }).Accepts<StockAdjustmentRequest>("application/json").Produces<AdjustStockResult>().WithName("tenant.inventory.adjust");
    }
    public sealed record StockAdjustmentRequest([property: JsonRequired] decimal QuantityDeltaBase, [property: JsonRequired] string Reason);
    public sealed record StockThresholdRequest([property: JsonRequired] decimal MinimumStockBase);
}
