using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.SharedKernel;

namespace MediPOS.Application.Modules.AuditSupport;

public static class AuditTrail
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Internal commands receive a server actor; future HTTP adapters must derive it from authenticated context.
    public static void RequireActor(Guid actorId)
    {
        if (actorId == Guid.Empty)
            throw new ApplicationErrorException(ApplicationErrors.ActorRequired);
    }

    public static AuditLog Record(Guid tenantId, Guid actorId, AuditAction action, Guid entityId,
        DateTimeOffset occurredAt, string? beforeJson, string? afterJson)
    {
        RequireActor(actorId);
        return AuditLog.Create(tenantId, actorId, action, AuditCodes.EntityFor(action), entityId,
            occurredAt, ServerCorrelation.GetId(), beforeJson, afterJson);
    }

    // Bounded field projections only. Entities, credentials and external profiles are never serialized.
    public static string LicenseState(License license) => JsonSerializer.Serialize(new
    {
        licenseId = license.Id,
        status = license.Status switch
        {
            LicenseStatus.Trial => "trial",
            LicenseStatus.Active => "active",
            LicenseStatus.Grace => "grace",
            LicenseStatus.Suspended => "suspended",
            LicenseStatus.Cancelled => "cancelled",
            LicenseStatus.PurgePending => "purge_pending",
            LicenseStatus.Purged => "purged",
            _ => throw new ArgumentOutOfRangeException(nameof(license)),
        },
        startsAt = license.StartsAt,
        expiresAt = license.ExpiresAt,
        maxBranches = license.MaxBranches,
    }, JsonOptions);

    public static string TenantCreated(Tenant tenant) => JsonSerializer.Serialize(new
    {
        tradingName = tenant.TradingName,
        license = JsonSerializer.Deserialize<JsonElement>(LicenseState(tenant.License)),
    }, JsonOptions);

    public static string LegalEntityCreated(LegalEntity entity) =>
        JsonSerializer.Serialize(new { legalName = entity.LegalName, ruc = entity.Ruc }, JsonOptions);

    public static string BranchCreated(Branch branch) =>
        JsonSerializer.Serialize(new { legalEntityId = branch.LegalEntityId, name = branch.Name, isMainHub = branch.IsMainHub }, JsonOptions);

    public static string MainHub(Guid? branchId) => JsonSerializer.Serialize(new { branchId }, JsonOptions);

    public static string MembershipState(Membership membership) => JsonSerializer.Serialize(new
    {
        userId = membership.UserId,
        role = TenantRoleCodes.ToCode(membership.Role),
        isActive = membership.IsActive,
        deactivatedAt = membership.DeactivatedAt,
    }, JsonOptions);

    public static string BranchAssignments(IEnumerable<Guid> branchIds) =>
        JsonSerializer.Serialize(new { branchIds = branchIds.Order().ToArray() }, JsonOptions);

    public static string WorkSchedule(IEnumerable<WorkSchedule> windows) => JsonSerializer.Serialize(new
    {
        windows = windows.OrderBy(value => WorkDayCodes.ToCode(value.DayOfWeek), StringComparer.Ordinal)
            .ThenBy(value => value.StartTime).ThenBy(value => value.EndTime)
            .Select(value => new { day = WorkDayCodes.ToCode(value.DayOfWeek), startTime = value.StartTime, endTime = value.EndTime }).ToArray(),
    }, JsonOptions);
}
