namespace MediPOS.Application.Errors;

public enum ErrorCategory { Validation, NotFound, Forbidden, Conflict }

public sealed record ApplicationError(string Code, ErrorCategory Category, string Message);

public sealed class ApplicationErrorException : Exception
{
    public ApplicationErrorException(ApplicationError error, Exception? innerException = null)
        : base((error ?? throw new ArgumentNullException(nameof(error))).Message, innerException) => Error = error;

    public ApplicationError Error { get; }
}

public static class ApplicationErrors
{
    public static readonly ApplicationError InvalidRequest = new("request.invalid", ErrorCategory.Validation, "The request is invalid.");
    public static readonly ApplicationError ActorRequired = new("audit.actor_required", ErrorCategory.Validation, "A server actor is required.");
    public static readonly ApplicationError TenantNotFound = new("tenant.not_found", ErrorCategory.NotFound, "Tenant was not found.");
    public static readonly ApplicationError LicenseNotFound = new("license.not_found", ErrorCategory.NotFound, "License was not found for the tenant.");
    public static readonly ApplicationError LegalEntityNotFound = new("legal_entity.not_found", ErrorCategory.NotFound, "Legal entity was not found for the tenant.");
    public static readonly ApplicationError BranchNotFound = new("branch.not_found", ErrorCategory.NotFound, "Branch was not found for the tenant.");
    public static readonly ApplicationError UserNotFound = new("user.not_found", ErrorCategory.NotFound, "User was not found.");
    public static readonly ApplicationError MembershipNotFound = new("membership.not_found", ErrorCategory.NotFound, "Membership was not found for the tenant.");
    public static readonly ApplicationError TenantScopeConflict = new("tenant.scope_conflict", ErrorCategory.Conflict, "The tenant cannot change within a data scope.");
    public static readonly ApplicationError LicenseDenied = new("license.operation_denied", ErrorCategory.Forbidden, "The tenant license does not allow this operation.");
    public static readonly ApplicationError LicenseStateConflict = new("license.state_conflict", ErrorCategory.Conflict, "The license state does not allow this change.");
    public static readonly ApplicationError LicenseConcurrency = new("license.concurrent_change", ErrorCategory.Conflict, "License changed concurrently; reload before retrying.");
    public static readonly ApplicationError BranchLimitReached = new("branch.limit_reached", ErrorCategory.Conflict, "The licensed branch limit has been reached.");
    public static readonly ApplicationError MembershipDuplicate = new("membership.active_duplicate", ErrorCategory.Conflict, "An active membership already exists for this user and tenant.");
    public static readonly ApplicationError OwnerLimitReached = new("membership.owner_limit_reached", ErrorCategory.Conflict, "The tenant already has two active Owners.");
    public static readonly ApplicationError MembershipInactive = new("membership.inactive", ErrorCategory.Forbidden, "Inactive memberships retain their configuration.");
}
