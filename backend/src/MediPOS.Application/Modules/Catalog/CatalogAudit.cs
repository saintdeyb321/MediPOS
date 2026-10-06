using System.Text.Json;
using MediPOS.Domain.Modules.Catalog;

namespace MediPOS.Application.Modules.Catalog;

public static class CatalogAudit
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Created(BusinessProduct product) => JsonSerializer.Serialize(new
    {
        product.GlobalProductId,
        product.InternalCode,
        product.Name,
        productType = ProductTypeCodes.ToCode(product.ProductType),
        product.CategoryId,
        product.BrandOrLaboratory,
        product.Barcode,
        product.RetailPrice,
        product.WholesalePrice,
        product.IsActive,
        medicine = product.Medicine is { } data ? new
        {
            data.ActiveIngredients,
            data.NormalizedStrength,
            data.DosageForm,
            data.Route,
            data.SanitaryRegistration,
        } : null,
    }, Options);

    public static string Prices(BusinessProduct product) => JsonSerializer.Serialize(new { product.RetailPrice, product.WholesalePrice }, Options);
    public static string Status(BusinessProduct product) => JsonSerializer.Serialize(new { product.IsActive }, Options);
}
