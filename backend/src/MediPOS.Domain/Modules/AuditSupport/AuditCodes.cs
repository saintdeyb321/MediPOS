namespace MediPOS.Domain.Modules.AuditSupport;

public enum AuditAction
{
    TenantCreated, LicenseRenewed, LicenseSuspended, LicenseReactivated, TenantPurgeRequested,
    LegalEntityCreated, BranchCreated, BranchMainHubChanged,
    MembershipCreated, MembershipBranchesReplaced, MembershipScheduleReplaced, MembershipDeactivated,
}

public enum AuditEntityType { Tenant, License, LegalEntity, Branch, Membership }

public static class AuditCodes
{
    public static string ActionToCode(AuditAction action) => action switch
    {
        AuditAction.TenantCreated => "tenant.created",
        AuditAction.LicenseRenewed => "license.renewed",
        AuditAction.LicenseSuspended => "license.suspended",
        AuditAction.LicenseReactivated => "license.reactivated",
        AuditAction.TenantPurgeRequested => "tenant.purge_requested",
        AuditAction.LegalEntityCreated => "legal_entity.created",
        AuditAction.BranchCreated => "branch.created",
        AuditAction.BranchMainHubChanged => "branch.main_hub_changed",
        AuditAction.MembershipCreated => "membership.created",
        AuditAction.MembershipBranchesReplaced => "membership.branches_replaced",
        AuditAction.MembershipScheduleReplaced => "membership.schedule_replaced",
        AuditAction.MembershipDeactivated => "membership.deactivated",
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    public static AuditAction ActionFromCode(string code) => code switch
    {
        "tenant.created" => AuditAction.TenantCreated,
        "license.renewed" => AuditAction.LicenseRenewed,
        "license.suspended" => AuditAction.LicenseSuspended,
        "license.reactivated" => AuditAction.LicenseReactivated,
        "tenant.purge_requested" => AuditAction.TenantPurgeRequested,
        "legal_entity.created" => AuditAction.LegalEntityCreated,
        "branch.created" => AuditAction.BranchCreated,
        "branch.main_hub_changed" => AuditAction.BranchMainHubChanged,
        "membership.created" => AuditAction.MembershipCreated,
        "membership.branches_replaced" => AuditAction.MembershipBranchesReplaced,
        "membership.schedule_replaced" => AuditAction.MembershipScheduleReplaced,
        "membership.deactivated" => AuditAction.MembershipDeactivated,
        _ => throw new InvalidOperationException("Unknown persisted audit action."),
    };

    public static AuditEntityType EntityFor(AuditAction action) => action switch
    {
        AuditAction.TenantCreated or AuditAction.TenantPurgeRequested => AuditEntityType.Tenant,
        AuditAction.LicenseRenewed or AuditAction.LicenseSuspended or AuditAction.LicenseReactivated => AuditEntityType.License,
        AuditAction.LegalEntityCreated => AuditEntityType.LegalEntity,
        AuditAction.BranchCreated or AuditAction.BranchMainHubChanged => AuditEntityType.Branch,
        AuditAction.MembershipCreated or AuditAction.MembershipBranchesReplaced or
            AuditAction.MembershipScheduleReplaced or AuditAction.MembershipDeactivated => AuditEntityType.Membership,
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    public static string EntityToCode(AuditEntityType entityType) => entityType switch
    {
        AuditEntityType.Tenant => "tenant",
        AuditEntityType.License => "license",
        AuditEntityType.LegalEntity => "legal_entity",
        AuditEntityType.Branch => "branch",
        AuditEntityType.Membership => "membership",
        _ => throw new ArgumentOutOfRangeException(nameof(entityType)),
    };

    public static AuditEntityType EntityFromCode(string code) => code switch
    {
        "tenant" => AuditEntityType.Tenant,
        "license" => AuditEntityType.License,
        "legal_entity" => AuditEntityType.LegalEntity,
        "branch" => AuditEntityType.Branch,
        "membership" => AuditEntityType.Membership,
        _ => throw new InvalidOperationException("Unknown persisted audit entity type."),
    };
}
