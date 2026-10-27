using System.Text.Json.Serialization;
using MediPOS.Application.Modules.Purchasing;
using MediPOS.Application.Modules.Purchasing.ConfirmPurchase;
using MediPOS.Application.Modules.Purchasing.CreatePurchase;
using MediPOS.Application.Modules.Purchasing.CreateSupplier;
using MediPOS.Application.Modules.Purchasing.FindPurchasesByDocumentReference;
using MediPOS.Application.Modules.Purchasing.ReplacePurchaseLines;

namespace MediPOS.Api.Tenant;

public static partial class TenantEndpoints
{
    private static void MapPurchasing(RouteGroupBuilder tenants)
    {
        tenants.MapPost("/suppliers", async (HttpContext c, CreateSupplierHandler handler) =>
        {
            var input = await TenantHttp.Read<SupplierRequest>(c);
            return TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), input.Name, input.Ruc, input.Contact, TenantHttp.Actor(c)), c.RequestAborted), 201);
        }).Accepts<SupplierRequest>("application/json").Produces<SupplierDetails>(201).WithName("tenant.suppliers.create").AddEndpointFilter(new RecoverableWriteFilter("supplier"));
        tenants.MapGet("/branches/{branchId}/suppliers", async (HttpContext c, PurchaseDirectoryQueries queries) =>
            TenantHttp.Response(await queries.SuppliersAsync(TenantHttp.Tenant(c), TenantHttp.Branch(c), TenantHttp.Offset(c), TenantHttp.Limit(c), c.RequestAborted)))
            .Produces<IReadOnlyList<SupplierDetails>>().WithName("tenant.suppliers.list").WithQuery<TenantOpenApi.Page>();
        tenants.MapPost("/branches/{branchId}/purchases", async (HttpContext c, CreatePurchaseHandler handler) =>
        {
            var input = await TenantHttp.Read<PurchaseRequest>(c);
            return TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Branch(c), input.SupplierId, input.DocumentReference, TenantHttp.Actor(c)), c.RequestAborted), 201);
        }).Accepts<PurchaseRequest>("application/json").Produces<PurchaseDetails>(201).WithName("tenant.purchases.create");
        tenants.MapGet("/branches/{branchId}/purchases/{purchaseId}", async (HttpContext c, PurchaseDirectoryQueries queries) =>
            TenantHttp.Response(await queries.PurchaseAsync(TenantHttp.Tenant(c), TenantHttp.Branch(c), TenantHttp.Id(c, "purchaseId"), c.RequestAborted)))
            .Produces<PurchaseView>().WithName("tenant.purchases.read");
        // These existing handlers resolve the persisted branch and authorize it themselves.
        tenants.MapPut("/purchases/{purchaseId}/lines", async (HttpContext c, ReplacePurchaseLinesHandler handler) =>
        {
            var input = await TenantHttp.Read<PurchaseLinesRequest>(c);
            return TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Id(c, "purchaseId"), input.Lines, TenantHttp.Actor(c), input.ExpectedRevision), c.RequestAborted));
        }).Accepts<PurchaseLinesRequest>("application/json").Produces<IReadOnlyList<PurchaseLineDetails>>().WithName("tenant.purchases.lines");
        tenants.MapPost("/purchases/{purchaseId}/confirm", async (HttpContext c, ConfirmPurchaseHandler handler) =>
        {
            var input = await TenantHttp.Read<PurchaseConfirmationRequest>(c);
            return TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Id(c, "purchaseId"), TenantHttp.Actor(c), input.ExpectedRevision), c.RequestAborted));
        }).Accepts<PurchaseConfirmationRequest>("application/json").Produces<PurchaseDetails>().WithName("tenant.purchases.confirm");
        tenants.MapGet("/branches/{branchId}/purchases/search", async (HttpContext c, FindPurchasesByDocumentReferenceHandler handler) =>
            TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Query(c, "reference") ?? "", TenantHttp.Branch(c), TenantHttp.Offset(c), TenantHttp.Limit(c)), c.RequestAborted)))
            .Produces<IReadOnlyList<PurchaseDetails>>().WithName("tenant.purchases.search").WithQuery<TenantOpenApi.PurchaseSearch>();
    }
    public sealed record EmptyRequest;
    public sealed record SupplierRequest([property: JsonRequired] string Name, string? Ruc = null, string? Contact = null);
    public sealed record PurchaseRequest(Guid? SupplierId = null, string? DocumentReference = null);
    public sealed record PurchaseLinesRequest([property: JsonRequired] IReadOnlyList<PurchaseLineInput> Lines, [property: JsonRequired] string ExpectedRevision);
    public sealed record PurchaseConfirmationRequest([property: JsonRequired] string ExpectedRevision);
}
