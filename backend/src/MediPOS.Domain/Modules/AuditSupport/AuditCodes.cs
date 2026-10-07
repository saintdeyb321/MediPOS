namespace MediPOS.Domain.Modules.AuditSupport;

public enum AuditAction
{
    TenantCreated, LicenseRenewed, LicenseSuspended, LicenseReactivated, TenantPurgeRequested,
    LegalEntityCreated, BranchCreated, BranchMainHubChanged,
    MembershipCreated, MembershipBranchesReplaced, MembershipScheduleReplaced, MembershipDeactivated,
    BusinessProductCreated, BusinessProductPriceChanged, BusinessProductStatusChanged,
    BusinessProductUnitsChanged, PurchaseConfirmed, InventoryAdjusted, CatalogProductsImported, CashSessionOpened, SaleConfirmed, SaleVoided,
}

public enum AuditEntityType { Tenant, License, LegalEntity, Branch, Membership, BusinessProduct, Purchase, InventoryLot, ImportJob, CashSession, Sale }

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
        AuditAction.BusinessProductCreated => "business_product.created",
        AuditAction.BusinessProductPriceChanged => "business_product.price_changed",
        AuditAction.BusinessProductStatusChanged => "business_product.status_changed",
        AuditAction.BusinessProductUnitsChanged => "business_product.units_changed",
        AuditAction.PurchaseConfirmed => "purchase.confirmed",
        AuditAction.InventoryAdjusted => "inventory.adjusted",
        AuditAction.CatalogProductsImported => "catalog.products_imported",
        AuditAction.CashSessionOpened => "cash_session.opened",
        AuditAction.SaleConfirmed => "sale.confirmed",
        AuditAction.SaleVoided => "sale.voided",
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
        "business_product.created" => AuditAction.BusinessProductCreated,
        "business_product.price_changed" => AuditAction.BusinessProductPriceChanged,
        "business_product.status_changed" => AuditAction.BusinessProductStatusChanged,
        "business_product.units_changed" => AuditAction.BusinessProductUnitsChanged,
        "purchase.confirmed" => AuditAction.PurchaseConfirmed,
        "inventory.adjusted" => AuditAction.InventoryAdjusted,
        "catalog.products_imported" => AuditAction.CatalogProductsImported,
        "cash_session.opened" => AuditAction.CashSessionOpened,
        "sale.confirmed" => AuditAction.SaleConfirmed,
        "sale.voided" => AuditAction.SaleVoided,
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
        AuditAction.BusinessProductCreated or AuditAction.BusinessProductPriceChanged or
            AuditAction.BusinessProductStatusChanged or AuditAction.BusinessProductUnitsChanged => AuditEntityType.BusinessProduct,
        AuditAction.PurchaseConfirmed => AuditEntityType.Purchase,
        AuditAction.InventoryAdjusted => AuditEntityType.InventoryLot,
        AuditAction.CatalogProductsImported => AuditEntityType.ImportJob,
        AuditAction.CashSessionOpened => AuditEntityType.CashSession,
        AuditAction.SaleConfirmed or AuditAction.SaleVoided => AuditEntityType.Sale,
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    public static string EntityToCode(AuditEntityType entityType) => entityType switch
    {
        AuditEntityType.Tenant => "tenant",
        AuditEntityType.License => "license",
        AuditEntityType.LegalEntity => "legal_entity",
        AuditEntityType.Branch => "branch",
        AuditEntityType.Membership => "membership",
        AuditEntityType.BusinessProduct => "business_product",
        AuditEntityType.Purchase => "purchase",
        AuditEntityType.InventoryLot => "inventory_lot",
        AuditEntityType.ImportJob => "import_job",
        AuditEntityType.CashSession => "cash_session",
        AuditEntityType.Sale => "sale",
        _ => throw new ArgumentOutOfRangeException(nameof(entityType)),
    };

    public static AuditEntityType EntityFromCode(string code) => code switch
    {
        "tenant" => AuditEntityType.Tenant,
        "license" => AuditEntityType.License,
        "legal_entity" => AuditEntityType.LegalEntity,
        "branch" => AuditEntityType.Branch,
        "membership" => AuditEntityType.Membership,
        "business_product" => AuditEntityType.BusinessProduct,
        "purchase" => AuditEntityType.Purchase,
        "inventory_lot" => AuditEntityType.InventoryLot,
        "import_job" => AuditEntityType.ImportJob,
        "cash_session" => AuditEntityType.CashSession,
        "sale" => AuditEntityType.Sale,
        _ => throw new InvalidOperationException("Unknown persisted audit entity type."),
    };
}
