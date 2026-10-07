using MediPOS.Application.Errors;

namespace MediPOS.Application.Modules.SalesPos;

public static class SalesPosErrors
{
    public static readonly ApplicationError SaleNotFound = new("sale.not_found", ErrorCategory.NotFound, "Sale was not found in the selected tenant and branch.");
    public static readonly ApplicationError NotVoidable = new("sale.not_voidable", ErrorCategory.Conflict, "Only confirmed sales can be voided.");
    public static readonly ApplicationError AlreadyVoided = new("sale.already_voided", ErrorCategory.Conflict, "Sale has already been voided.");
    public static readonly ApplicationError CashSessionClosed = new("sale.cash_session_closed", ErrorCategory.Conflict, "The original cash session must remain open to void this sale.");
    public static readonly ApplicationError ForbiddenVoid = new("sale.forbidden_void", ErrorCategory.Forbidden, "Void requires Owner permission or the sale's own seller membership.");
    public static readonly ApplicationError InsufficientCash = new("sale.insufficient_cash", ErrorCategory.Conflict, "Current expected cash is insufficient for this cash refund.");
    public static readonly ApplicationError CorruptedHistory = new("sale.corrupted_history", ErrorCategory.Conflict, "Sale history is inconsistent; void cannot repair it.");
    public static readonly ApplicationError ReversalAlreadyExists = new("sale.reversal_already_exists", ErrorCategory.Conflict, "An original sale effect has already been reversed.");
    public static readonly ApplicationError InvalidVoidReason = new("sale.invalid_void_reason", ErrorCategory.Validation, "A nonblank void reason of at most 512 characters is required.");
    public static readonly ApplicationError InvalidPayments = new("sale.invalid_payments", ErrorCategory.Validation, "Positive, exactly representable payments with valid methods are required.");
    public static readonly ApplicationError DuplicatePaymentMethod = new("sale.duplicate_payment_method", ErrorCategory.Validation, "Each payment method may occur only once.");
    public static readonly ApplicationError PaymentTotalMismatch = new("sale.payment_total_mismatch", ErrorCategory.Validation, "Payments must equal the sale total exactly.");
    public static readonly ApplicationError InconsistentDraft = new("sale.inconsistent_draft", ErrorCategory.Conflict, "Draft quantities, snapshots or totals are inconsistent.");
    public static readonly ApplicationError CheckoutWindowChanged = new("sale.checkout_window_changed", ErrorCategory.Conflict, "The checkout date policy changed during a lock wait; retry confirmation.");
    public static readonly ApplicationError CashSessionRequired = new("sale.open_cash_session_required", ErrorCategory.Conflict, "An open cash session is required.");
    public static readonly ApplicationError CashSessionMismatch = new("sale.cash_session_mismatch", ErrorCategory.Conflict, "Cash session ownership must match the draft and authenticated seller.");
    public static readonly ApplicationError DraftNotFound = new("sale.draft_not_found", ErrorCategory.NotFound, "Sale draft was not found in the selected branch.");
    public static readonly ApplicationError SellerRequired = new("sale.seller_required", ErrorCategory.Forbidden, "This draft belongs to another seller.");
    public static readonly ApplicationError NotDraft = new("sale.not_draft", ErrorCategory.Conflict, "Only draft sales can be edited.");
    public static readonly ApplicationError ConcurrentEdit = new("sale.concurrent_edit", ErrorCategory.Conflict, "Concurrent activity prevented this operation; reload the sale and retry.");
    public static readonly ApplicationError ProductUnavailable = new("sale.product_unavailable", ErrorCategory.Validation, "An active product from the selected tenant is required.");
    public static readonly ApplicationError UnitUnavailable = new("sale.unit_unavailable", ErrorCategory.Validation, "An active presentation belonging to the selected product is required.");
    public static readonly ApplicationError WholesaleUnavailable = new("sale.wholesale_unavailable", ErrorCategory.Validation, "Wholesale price is unavailable for this product.");
    public static readonly ApplicationError InvalidLine = new("sale.invalid_line", ErrorCategory.Validation, "Line quantity, conversion or amount is invalid or cannot be represented exactly.");
    public static readonly ApplicationError DuplicateLines = new("sale.duplicate_lines", ErrorCategory.Validation, "Product, presentation and price kind cannot repeat.");
    public static readonly ApplicationError InvalidTotal = new("sale.invalid_total", ErrorCategory.Validation, "Draft total exceeds the supported amount range.");
}
