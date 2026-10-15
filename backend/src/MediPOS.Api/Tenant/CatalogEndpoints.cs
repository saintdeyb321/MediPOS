using System.Text.Json.Serialization;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.Catalog.CreateBusinessProductFromGlobal;
using MediPOS.Application.Modules.Catalog.CreateLocalBusinessProduct;
using MediPOS.Application.Modules.Catalog.ProductImport;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Application.Modules.Catalog.SearchProducts;
using MediPOS.Application.Modules.Catalog.SetBusinessProductStatus;
using MediPOS.Application.Modules.Catalog.UpdateBusinessProductPrices;
using MediPOS.Domain.Modules.Catalog;

namespace MediPOS.Api.Tenant;

public static partial class TenantEndpoints
{
    private static void MapCatalog(RouteGroupBuilder tenants)
    {
        tenants.MapGet("/categories", async (HttpContext c, CatalogDirectoryQueries queries) => TenantHttp.Response(await queries.CategoriesAsync(TenantHttp.Tenant(c), TenantHttp.Offset(c), TenantHttp.Limit(c), c.RequestAborted)))
            .Produces<IReadOnlyList<CategoryDetails>>().WithName("tenant.categories.list").WithQuery<TenantOpenApi.Page>();
        tenants.MapGet("/global-products", async (HttpContext c, CatalogDirectoryQueries queries) => TenantHttp.Response((await queries.GlobalProductsAsync(TenantHttp.Tenant(c), TenantHttp.Offset(c), TenantHttp.Limit(c), c.RequestAborted)).Select(GlobalProductResponse.From).ToArray()))
            .Produces<GlobalProductResponse[]>().WithName("tenant.global_products.list").WithQuery<TenantOpenApi.Page>();
        tenants.MapGet("/products", async (HttpContext c, CatalogDirectoryQueries queries) => TenantHttp.Response((await queries.ProductsAsync(TenantHttp.Tenant(c), TenantHttp.Offset(c), TenantHttp.Limit(c), c.RequestAborted)).Select(ProductResponse.From).ToArray()))
            .Produces<ProductResponse[]>().WithName("tenant.products.list").WithQuery<TenantOpenApi.Page>();
        tenants.MapGet("/branches/{branchId}/products/{productId}", async (HttpContext c, CatalogDirectoryQueries queries) =>
        {
            var result = await queries.ProductAsync(TenantHttp.Tenant(c), TenantHttp.Branch(c), TenantHttp.Id(c, "productId"), c.RequestAborted);
            return TenantHttp.Response(new ProductWithUnitsResponse(ProductResponse.From(result.Product), result.Units));
        }).Produces<ProductWithUnitsResponse>().WithName("tenant.products.read");
        tenants.MapGet("/branches/{branchId}/products/search", async (HttpContext c, SearchProductsHandler handler) =>
            TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Branch(c), TenantHttp.Query(c, "q") ?? "", TenantHttp.BoundedCount(c, "limit", 20, 1, 50)), c.RequestAborted)))
            .Produces<SearchProductsResult>().WithName("tenant.products.search").WithQuery<TenantOpenApi.Search>();
        tenants.MapPost("/products/local", async (HttpContext c, CreateLocalBusinessProductHandler handler) =>
        {
            var input = await TenantHttp.Read<LocalProductRequest>(c);
            return TenantHttp.Response(ProductResponse.From(await handler.HandleAsync(new(TenantHttp.Tenant(c), input.InternalCode, input.ProductType, input.Name, input.CategoryId,
                input.BrandOrLaboratory, input.Barcode, input.Medicine, input.RetailPrice, input.WholesalePrice, TenantHttp.Actor(c), input.IsActive), c.RequestAborted)), 201);
        }).Accepts<LocalProductRequest>("application/json").Produces<ProductResponse>(201).WithName("tenant.products.create_local");
        tenants.MapPost("/products/from-global", async (HttpContext c, CreateBusinessProductFromGlobalHandler handler) =>
        {
            var input = await TenantHttp.Read<GlobalProductRequest>(c);
            return TenantHttp.Response(ProductResponse.From(await handler.HandleAsync(new(TenantHttp.Tenant(c), input.GlobalProductId, input.InternalCode, input.RetailPrice, input.WholesalePrice, TenantHttp.Actor(c), input.IsActive), c.RequestAborted)), 201);
        }).Accepts<GlobalProductRequest>("application/json").Produces<ProductResponse>(201).WithName("tenant.products.create_from_global");
        tenants.MapPut("/products/{productId}/prices", async (HttpContext c, UpdateBusinessProductPricesHandler handler) =>
        {
            var input = await TenantHttp.Read<PricesRequest>(c);
            return TenantHttp.Response(ProductResponse.From(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Id(c, "productId"), input.RetailPrice, input.WholesalePrice, TenantHttp.Actor(c)), c.RequestAborted)));
        }).Accepts<PricesRequest>("application/json").Produces<ProductResponse>().WithName("tenant.products.prices");
        tenants.MapPut("/products/{productId}/status", async (HttpContext c, SetBusinessProductStatusHandler handler) =>
        {
            var input = await TenantHttp.Read<ProductStatusRequest>(c);
            return TenantHttp.Response(ProductResponse.From(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Id(c, "productId"), input.IsActive, TenantHttp.Actor(c)), c.RequestAborted)));
        }).Accepts<ProductStatusRequest>("application/json").Produces<ProductResponse>().WithName("tenant.products.status");
        tenants.MapPut("/products/{productId}/units", async (HttpContext c, ReplaceProductUnitsHandler handler) =>
        {
            var input = await TenantHttp.Read<UnitsRequest>(c);
            return TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Id(c, "productId"), input.Units, TenantHttp.Actor(c)), c.RequestAborted));
        }).Accepts<UnitsRequest>("application/json").Produces<IReadOnlyList<ProductUnitDetails>>().WithName("tenant.products.units");
    }

    public sealed record LocalProductRequest([property: JsonRequired] string InternalCode, [property: JsonRequired] ProductType ProductType,
        [property: JsonRequired] string Name, [property: JsonRequired] Guid CategoryId, [property: JsonRequired] string BrandOrLaboratory,
        [property: JsonRequired] decimal RetailPrice, string? Barcode = null, MedicineInput? Medicine = null, decimal? WholesalePrice = null, bool IsActive = true);
    public sealed record GlobalProductRequest([property: JsonRequired] Guid GlobalProductId, [property: JsonRequired] string InternalCode,
        [property: JsonRequired] decimal RetailPrice, decimal? WholesalePrice = null, bool IsActive = true);
    public sealed record PricesRequest([property: JsonRequired] decimal RetailPrice, decimal? WholesalePrice = null);
    public sealed record ProductStatusRequest([property: JsonRequired] bool IsActive);
    public sealed record UnitsRequest([property: JsonRequired] IReadOnlyList<ProductUnitDefinition> Units);
    public sealed record MedicineResponse(IReadOnlyList<string> ActiveIngredients, string NormalizedStrength, string DosageForm, string? Route, string? SanitaryRegistration,
        IReadOnlyList<MedicineComponentInput> Components)
    {
        internal static MedicineResponse? From(MedicineData? value) => value is null ? null : new(value.ActiveIngredients, value.NormalizedStrength, value.DosageForm, value.Route, value.SanitaryRegistration,
            value.Components.Select(component => new MedicineComponentInput(component.Ingredient, component.StrengthNormalized)).ToArray());
    }
    public sealed record ProductResponse(Guid Id, Guid TenantId, Guid? GlobalProductId, string InternalCode, string Name, ProductType ProductType, Guid CategoryId,
        string BrandOrLaboratory, string? Barcode, decimal RetailPrice, decimal? WholesalePrice, bool IsActive, DateTimeOffset CreatedAt, MedicineResponse? Medicine)
    {
        internal static ProductResponse From(BusinessProductDetails value) => new(value.Id, value.TenantId, value.GlobalProductId, value.InternalCode, value.Name, value.ProductType,
            value.CategoryId, value.BrandOrLaboratory, value.Barcode, value.RetailPrice, value.WholesalePrice, value.IsActive, value.CreatedAt, MedicineResponse.From(value.Medicine));
    }
    public sealed record GlobalProductResponse(Guid Id, ProductType ProductType, string Name, Guid CategoryId, string BrandOrLaboratory, string? Barcode, MedicineResponse? Medicine)
    {
        internal static GlobalProductResponse From(GlobalProductDetails value) => new(value.Id, value.ProductType, value.Name, value.CategoryId, value.BrandOrLaboratory, value.Barcode, MedicineResponse.From(value.Medicine));
    }
    public sealed record ProductWithUnitsResponse(ProductResponse Product, IReadOnlyList<ProductUnitDetails> Units);
}
