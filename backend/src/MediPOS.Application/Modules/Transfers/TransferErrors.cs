using MediPOS.Application.Errors;

namespace MediPOS.Application.Modules.Transfers;

public static class TransferErrors
{
    public static readonly ApplicationError NotFound = new("transfer.not_found", ErrorCategory.NotFound, "Transfer was not found in this tenant.");
    public static readonly ApplicationError InvalidTransition = new("transfer.invalid_transition", ErrorCategory.Conflict, "Transfer state does not allow this action.");
    public static readonly ApplicationError SameBranches = new("transfer.same_branches", ErrorCategory.Validation, "Source and destination must differ.");
    public static readonly ApplicationError Forbidden = new("transfer.forbidden_action", ErrorCategory.Forbidden, "Current operational permission does not allow this transfer action.");
    public static readonly ApplicationError InvalidLines = new("transfer.invalid_lines", ErrorCategory.Validation, "Bounded distinct active product/presentation lines with exact positive quantities are required.");
    public static readonly ApplicationError InvalidAllocation = new("transfer.invalid_allocation", ErrorCategory.Validation, "Allocation must match this transfer, source lot, product and positive quantity.");
    public static readonly ApplicationError QuantityMismatch = new("transfer.allocation_quantity_mismatch", ErrorCategory.Validation, "Selections must completely and exactly cover the transfer lines or allocations.");
    public static readonly ApplicationError InsufficientStock = new("transfer.insufficient_stock", ErrorCategory.Conflict, "Dispatch exceeds available source stock.");
    public static readonly ApplicationError AlreadyDispatched = new("transfer.already_dispatched", ErrorCategory.Conflict, "Transfer has already been dispatched.");
    public static readonly ApplicationError AlreadyReceived = new("transfer.already_received", ErrorCategory.Conflict, "Transfer has already been received.");
    public static readonly ApplicationError CannotCancel = new("transfer.cannot_cancel", ErrorCategory.Conflict, "Only requested or approved transfers may be cancelled.");
    public static readonly ApplicationError InvalidReason = new("transfer.invalid_cancel_reason", ErrorCategory.Validation, "A trimmed cancellation reason of at most 512 characters is required.");
    public static readonly ApplicationError InvalidReceipt = new("transfer.invalid_received_quantity", ErrorCategory.Validation, "Every receipt quantity must be between zero and its dispatched quantity.");
    public static readonly ApplicationError CorruptedHistory = new("transfer.corrupted_history", ErrorCategory.Conflict, "Inconsistent transfer history cannot be repaired by this operation.");
    public static readonly ApplicationError ConcurrentOperation = new("transfer.concurrent_operation", ErrorCategory.Conflict, "Concurrent activity prevented this transition; reload and retry.");
}
