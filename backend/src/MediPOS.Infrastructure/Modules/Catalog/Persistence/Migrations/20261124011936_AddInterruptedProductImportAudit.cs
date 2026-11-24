using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.Catalog.Persistence.Migrations;

/// <inheritdoc />
public partial class AddInterruptedProductImportAudit : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'electronic_series' AND action = 'cpe.series_configured') OR\r\n(entity_type = 'electronic_document' AND action IN ('cpe.requested','cpe.reviewed','cpe.delivery_changed')) OR\r\n(entity_type = 'supplier_ubl_import' AND action IN ('supplier_ubl.imported','supplier_ubl.reviewed','supplier_ubl.rejected','supplier_ubl.converted','supplier_ubl.file_rejected')) OR\r\n(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\r\n(entity_type = 'initial_owner_invitation' AND action IN ('initial_owner.invited', 'initial_owner.claimed', 'initial_owner.revoked')) OR\r\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\r\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\r\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\r\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\r\n(entity_type = 'employee_invitation' AND action IN ('employee_invitation.created','employee_invitation.accepted','employee_invitation.approved','employee_invitation.revoked')) OR\r\n(entity_type = 'business_product' AND action IN ('business_product.data_corrected', 'business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\r\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\r\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted') OR\r\n(entity_type = 'import_job' AND action IN ('catalog.products_imported', 'catalog.product_import_interrupted')) OR\r\n(entity_type = 'cash_session' AND action IN ('cash_session.opened', 'cash_session.closed')) OR\r\n(entity_type = 'sale' AND action IN ('sale.confirmed', 'sale.voided')) OR\r\n(entity_type = 'transfer' AND action IN ('transfer.requested', 'transfer.approved', 'transfer.dispatched', 'transfer.received', 'transfer.cancelled')) OR\r\n(entity_type = 'cash_transfer' AND action IN ('cash_transfer.dispatched', 'cash_transfer.received')) OR\r\n(entity_type = 'tenant_commission_settings' AND action = 'commissions.settings_changed') OR\r\n(entity_type = 'commission_rule' AND action IN ('commission_rule.created', 'commission_rule.deactivated')) OR\r\n(entity_type = 'branch_stock_threshold' AND action = 'stock_threshold.changed') OR\r\n(entity_type = 'sync_device' AND action IN ('sync_device.registered', 'sync_device.revoked')) OR\r\n(entity_type = 'offline_sale_conflict' AND action = 'offline_sale_conflict.resolved')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            LOCK TABLE public.audit_logs IN ACCESS EXCLUSIVE MODE;
            DO $guard$
            BEGIN
                IF row_security_active('public.audit_logs'::regclass) THEN
                    RAISE EXCEPTION 'Downgrade to schema 39 requires authority to inspect every audit row.';
                END IF;
                IF EXISTS (SELECT 1 FROM public.audit_logs WHERE action = 'catalog.product_import_interrupted') THEN
                    RAISE EXCEPTION 'Downgrade to schema 39 would invalidate interrupted import audit history.';
                END IF;
            END;
            $guard$;
            """);

        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'electronic_series' AND action = 'cpe.series_configured') OR\r\n(entity_type = 'electronic_document' AND action IN ('cpe.requested','cpe.reviewed','cpe.delivery_changed')) OR\r\n(entity_type = 'supplier_ubl_import' AND action IN ('supplier_ubl.imported','supplier_ubl.reviewed','supplier_ubl.rejected','supplier_ubl.converted','supplier_ubl.file_rejected')) OR\r\n(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\r\n(entity_type = 'initial_owner_invitation' AND action IN ('initial_owner.invited', 'initial_owner.claimed', 'initial_owner.revoked')) OR\r\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\r\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\r\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\r\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\r\n(entity_type = 'employee_invitation' AND action IN ('employee_invitation.created','employee_invitation.accepted','employee_invitation.approved','employee_invitation.revoked')) OR\r\n(entity_type = 'business_product' AND action IN ('business_product.data_corrected', 'business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\r\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\r\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted') OR\r\n(entity_type = 'import_job' AND action = 'catalog.products_imported') OR\r\n(entity_type = 'cash_session' AND action IN ('cash_session.opened', 'cash_session.closed')) OR\r\n(entity_type = 'sale' AND action IN ('sale.confirmed', 'sale.voided')) OR\r\n(entity_type = 'transfer' AND action IN ('transfer.requested', 'transfer.approved', 'transfer.dispatched', 'transfer.received', 'transfer.cancelled')) OR\r\n(entity_type = 'cash_transfer' AND action IN ('cash_transfer.dispatched', 'cash_transfer.received')) OR\r\n(entity_type = 'tenant_commission_settings' AND action = 'commissions.settings_changed') OR\r\n(entity_type = 'commission_rule' AND action IN ('commission_rule.created', 'commission_rule.deactivated')) OR\r\n(entity_type = 'branch_stock_threshold' AND action = 'stock_threshold.changed') OR\r\n(entity_type = 'sync_device' AND action IN ('sync_device.registered', 'sync_device.revoked')) OR\r\n(entity_type = 'offline_sale_conflict' AND action = 'offline_sale_conflict.resolved')");
    }
}
