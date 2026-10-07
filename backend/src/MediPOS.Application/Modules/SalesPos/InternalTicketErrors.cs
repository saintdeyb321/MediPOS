using MediPOS.Application.Errors;

namespace MediPOS.Application.Modules.SalesPos;

public static class InternalTicketErrors
{
    public static readonly ApplicationError NotFinal = new("internal_ticket.sale_not_final", ErrorCategory.Conflict, "Draft sales have no final internal ticket.");
    public static readonly ApplicationError Forbidden = new("internal_ticket.forbidden", ErrorCategory.Forbidden, "Internal ticket requires Owner access or the sale's own operational seller.");
    public static readonly ApplicationError CorruptedHistory = new("internal_ticket.corrupted_history", ErrorCategory.Conflict, "Inconsistent sale history cannot produce an internal ticket.");
}
