using MediPOS.Application.Errors;

namespace MediPOS.Application.Modules.Catalog;

public static class CatalogErrors
{
    public static readonly ApplicationError CategoryNotFound = new("catalog.category_not_found", ErrorCategory.NotFound, "Category was not found.");
    public static readonly ApplicationError GlobalProductNotFound = new("catalog.global_product_not_found", ErrorCategory.NotFound, "Global product was not found.");
    public static readonly ApplicationError GlobalProductInactive = new("catalog.global_product_inactive", ErrorCategory.Conflict, "An active global product is required.");
    public static readonly ApplicationError BusinessProductNotFound = new("catalog.business_product_not_found", ErrorCategory.NotFound, "Business product was not found for the tenant.");
    public static readonly ApplicationError InternalCodeDuplicate = new("catalog.internal_code_duplicate", ErrorCategory.Conflict, "Internal code already exists for this tenant.");
    public static readonly ApplicationError ConcurrentChange = new("catalog.concurrent_change", ErrorCategory.Conflict, "Business product changed concurrently; reload before retrying.");
}
