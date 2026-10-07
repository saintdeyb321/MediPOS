using MediPOS.Application.Errors;

namespace MediPOS.Application.Modules.SalesPos;

public static class SalesPosErrors
{
    public static readonly ApplicationError CashSessionRequired = new("sale.open_cash_session_required", ErrorCategory.Conflict, "An open cash session is required.");
    public static readonly ApplicationError CashSessionMismatch = new("sale.cash_session_mismatch", ErrorCategory.Conflict, "Cash session ownership must match the draft and authenticated seller.");
    public static readonly ApplicationError DraftNotFound = new("sale.draft_not_found", ErrorCategory.NotFound, "Sale draft was not found in the selected branch.");
    public static readonly ApplicationError SellerRequired = new("sale.seller_required", ErrorCategory.Forbidden, "This draft belongs to another seller.");
    public static readonly ApplicationError NotDraft = new("sale.not_draft", ErrorCategory.Conflict, "Only draft sales can be edited.");
    public static readonly ApplicationError ConcurrentEdit = new("sale.concurrent_edit", ErrorCategory.Conflict, "The draft changed; reload before editing.");
    public static readonly ApplicationError ProductUnavailable = new("sale.product_unavailable", ErrorCategory.Validation, "An active product from the selected tenant is required.");
    public static readonly ApplicationError UnitUnavailable = new("sale.unit_unavailable", ErrorCategory.Validation, "An active presentation belonging to the selected product is required.");
    public static readonly ApplicationError WholesaleUnavailable = new("sale.wholesale_unavailable", ErrorCategory.Validation, "Wholesale price is unavailable for this product.");
    public static readonly ApplicationError InvalidLine = new("sale.invalid_line", ErrorCategory.Validation, "Line quantity, conversion or amount is invalid or cannot be represented exactly.");
    public static readonly ApplicationError DuplicateLines = new("sale.duplicate_lines", ErrorCategory.Validation, "Product, presentation and price kind cannot repeat.");
    public static readonly ApplicationError InvalidTotal = new("sale.invalid_total", ErrorCategory.Validation, "Draft total exceeds the supported amount range.");
}
