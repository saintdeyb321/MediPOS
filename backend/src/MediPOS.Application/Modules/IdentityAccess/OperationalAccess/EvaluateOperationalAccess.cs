using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.Application.Modules.IdentityAccess.OperationalAccess;

public sealed record AccessContext(
    Guid UserId, Guid TenantId, Guid MembershipId, TenantRole Role, Guid? BranchId,
    bool MembershipIsActive, bool BranchAssigned, LicenseStatus? LicenseStatus,
    DateTimeOffset? LicenseStartsAt, DateTimeOffset? LicenseExpiresAt, bool WithinSchedule,
    DateTimeOffset EvaluatedAtUtc);

public sealed record OperationalAccessResult(bool IsAllowed, string Code, AccessContext? Context)
{
    public static OperationalAccessResult Denied(string code, AccessContext? context = null) => new(false, code, context);
}

// Authorization policy only. The caller supplies a trusted server instant, never a client timestamp.
public static class EvaluateOperationalAccess
{
    private static readonly TimeZoneInfo Lima = TimeZoneInfo.FindSystemTimeZoneById("America/Lima");

    public static OperationalAccessResult Evaluate(
        Guid userId, Guid tenantId, Guid? branchId, DateTimeOffset at, OperationalAccessSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.UserExists || userId == Guid.Empty)
            return OperationalAccessResult.Denied("access.user_missing");
        var membership = snapshot.Membership;
        if (membership is null)
            return OperationalAccessResult.Denied("access.membership_missing");
        if (tenantId == Guid.Empty || membership.TenantId != tenantId || membership.UserId != userId)
            return OperationalAccessResult.Denied("access.tenant_mismatch");

        var local = TimeZoneInfo.ConvertTime(at, Lima);
        var time = TimeOnly.FromDateTime(local.DateTime);
        var withinSchedule = snapshot.Schedule.Any(window =>
            window.TenantId == tenantId && window.MembershipId == membership.Id && window.Contains(local.DayOfWeek, time));
        var license = snapshot.License;
        var context = new AccessContext(userId, tenantId, membership.Id, membership.Role, branchId, membership.IsActive,
            snapshot.BranchAssigned, license?.Status, license?.StartsAt, license?.ExpiresAt, withinSchedule, at.ToUniversalTime());
        if (!membership.IsActive)
            return OperationalAccessResult.Denied("access.membership_inactive", context);
        if (license is null || license.TenantId != tenantId || !license.AllowsOperation(at))
            return OperationalAccessResult.Denied("access.license_denied", context);
        if (!Enum.IsDefined(membership.Role))
            return OperationalAccessResult.Denied("access.role_denied", context);
        if (branchId.HasValue && !snapshot.BranchBelongsToTenant)
            return OperationalAccessResult.Denied("access.branch_denied", context);
        if (membership.Role == TenantRole.Owner)
            return new OperationalAccessResult(true, "access.allowed", context);
        if (!branchId.HasValue)
            return OperationalAccessResult.Denied("access.branch_required", context);
        if (!snapshot.BranchAssigned)
            return OperationalAccessResult.Denied("access.branch_unassigned", context);
        if (!withinSchedule)
            return OperationalAccessResult.Denied("access.schedule_denied", context);
        return new OperationalAccessResult(true, "access.allowed", context);
    }
}
