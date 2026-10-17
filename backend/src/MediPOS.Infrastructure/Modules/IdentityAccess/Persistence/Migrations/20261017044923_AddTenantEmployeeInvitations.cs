using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.IdentityAccess.Persistence.Migrations;

/// <inheritdoc />
public partial class AddTenantEmployeeInvitations : Migration
{
    private static readonly string[] TenantMembershipKey = ["tenant_id", "id"];
    private static readonly string[] TenantCreatedKey = ["tenant_id", "created_at", "id"];
    private static readonly string[] TenantResultKey = ["tenant_id", "result_membership_id"];
    private static readonly string[] IntentionKey = ["tenant_id", "issuer_membership_id", "client_request_id"];
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.CreateTable(
            name: "employee_invitations",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                issuer_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                client_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                secret_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                accepted_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                result_membership_id = table.Column<Guid>(type: "uuid", nullable: true),
                approved_from_version = table.Column<long>(type: "bigint", nullable: true),
                revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                revoked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                revocation_reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_employee_invitations", x => x.id);
                table.CheckConstraint("ck_employee_invitations_acceptance", "(accepted_user_id IS NULL AND accepted_at IS NULL) OR (accepted_user_id IS NOT NULL AND accepted_at IS NOT NULL AND accepted_at >= created_at AND accepted_at < expires_at)");
                table.CheckConstraint("ck_employee_invitations_hash", "octet_length(secret_hash) = 32");
                table.CheckConstraint("ck_employee_invitations_identifiers", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND client_request_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.CheckConstraint("ck_employee_invitations_lifetime", "expires_at = created_at + interval '15 minutes'");
                table.CheckConstraint("ck_employee_invitations_role", "role IN ('owner','pharmacist','cashier')");
                table.CheckConstraint("ck_employee_invitations_state", "(status = 'pending_acceptance' AND accepted_user_id IS NULL AND approved_at IS NULL AND result_membership_id IS NULL AND approved_from_version IS NULL AND revoked_at IS NULL AND revoked_by_user_id IS NULL AND revocation_reason IS NULL) OR\n(status = 'accepted_pending_approval' AND accepted_user_id IS NOT NULL AND approved_at IS NULL AND result_membership_id IS NULL AND approved_from_version IS NULL AND revoked_at IS NULL AND revoked_by_user_id IS NULL AND revocation_reason IS NULL) OR\n(status = 'approved' AND accepted_user_id IS NOT NULL AND approved_at IS NOT NULL AND approved_at >= accepted_at AND approved_at < expires_at AND result_membership_id IS NOT NULL AND approved_from_version IS NOT NULL AND approved_from_version > 0 AND revoked_at IS NULL AND revoked_by_user_id IS NULL AND revocation_reason IS NULL) OR\n(status = 'revoked' AND approved_at IS NULL AND result_membership_id IS NULL AND approved_from_version IS NULL AND revoked_at IS NOT NULL AND revoked_at >= created_at AND revoked_by_user_id IS NOT NULL AND revocation_reason IS NOT NULL AND length(btrim(revocation_reason)) BETWEEN 1 AND 256)");
                table.ForeignKey(
                    name: "FK_employee_invitations_memberships_tenant_id_issuer_membershi~",
                    columns: x => new { x.tenant_id, x.issuer_membership_id },
                    principalTable: "memberships",
                    principalColumns: TenantMembershipKey,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_employee_invitations_memberships_tenant_id_result_membershi~",
                    columns: x => new { x.tenant_id, x.result_membership_id },
                    principalTable: "memberships",
                    principalColumns: TenantMembershipKey,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_employee_invitations_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "tenant_id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_employee_invitations_users_accepted_user_id",
                    column: x => x.accepted_user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_employee_invitations_users_revoked_by_user_id",
                    column: x => x.revoked_by_user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'electronic_series' AND action = 'cpe.series_configured') OR\n(entity_type = 'electronic_document' AND action IN ('cpe.requested','cpe.reviewed','cpe.delivery_changed')) OR\n(entity_type = 'supplier_ubl_import' AND action IN ('supplier_ubl.imported','supplier_ubl.reviewed','supplier_ubl.rejected','supplier_ubl.converted','supplier_ubl.file_rejected')) OR\n(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'employee_invitation' AND action IN ('employee_invitation.created','employee_invitation.accepted','employee_invitation.approved','employee_invitation.revoked')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted') OR\n(entity_type = 'import_job' AND action = 'catalog.products_imported') OR\n(entity_type = 'cash_session' AND action IN ('cash_session.opened', 'cash_session.closed')) OR\n(entity_type = 'sale' AND action IN ('sale.confirmed', 'sale.voided')) OR\n(entity_type = 'transfer' AND action IN ('transfer.requested', 'transfer.approved', 'transfer.dispatched', 'transfer.received', 'transfer.cancelled')) OR\n(entity_type = 'cash_transfer' AND action IN ('cash_transfer.dispatched', 'cash_transfer.received')) OR\n(entity_type = 'tenant_commission_settings' AND action = 'commissions.settings_changed') OR\n(entity_type = 'commission_rule' AND action IN ('commission_rule.created', 'commission_rule.deactivated')) OR\n(entity_type = 'branch_stock_threshold' AND action = 'stock_threshold.changed') OR\n(entity_type = 'sync_device' AND action IN ('sync_device.registered', 'sync_device.revoked')) OR\n(entity_type = 'offline_sale_conflict' AND action = 'offline_sale_conflict.resolved')");

        migrationBuilder.CreateIndex(
            name: "IX_employee_invitations_accepted_user_id",
            table: "employee_invitations",
            column: "accepted_user_id");

        migrationBuilder.CreateIndex(
            name: "IX_employee_invitations_revoked_by_user_id",
            table: "employee_invitations",
            column: "revoked_by_user_id");

        migrationBuilder.CreateIndex(
            name: "IX_employee_invitations_tenant_id_created_at_id",
            table: "employee_invitations",
            columns: TenantCreatedKey);

        migrationBuilder.CreateIndex(
            name: "IX_employee_invitations_tenant_id_result_membership_id",
            table: "employee_invitations",
            columns: TenantResultKey);

        migrationBuilder.CreateIndex(
            name: "ux_employee_invitations_intention",
            table: "employee_invitations",
            columns: IntentionKey,
            unique: true);
        migrationBuilder.Sql(EmployeeInvitationSql.Install);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Never erase invitation/audit history to roll back an application deployment.
        migrationBuilder.Sql("""
                DO $invitation_downgrade$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = current_user AND (rolsuper OR rolbypassrls)) THEN
                        RAISE EXCEPTION 'A fully visible administrative history check is required for schema downgrade';
                    END IF;
                    IF EXISTS (SELECT 1 FROM employee_invitations) OR EXISTS (SELECT 1 FROM audit_logs WHERE entity_type = 'employee_invitation') THEN
                        RAISE EXCEPTION 'Invitation history exists; automatic schema downgrade is forbidden';
                    END IF;
                END;
                $invitation_downgrade$;
                """);
        migrationBuilder.Sql(EmployeeInvitationSql.Uninstall);
        migrationBuilder.DropTable(
            name: "employee_invitations");

        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'electronic_series' AND action = 'cpe.series_configured') OR\n(entity_type = 'electronic_document' AND action IN ('cpe.requested','cpe.reviewed','cpe.delivery_changed')) OR\n(entity_type = 'supplier_ubl_import' AND action IN ('supplier_ubl.imported','supplier_ubl.reviewed','supplier_ubl.rejected','supplier_ubl.converted','supplier_ubl.file_rejected')) OR\n(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted') OR\n(entity_type = 'import_job' AND action = 'catalog.products_imported') OR\n(entity_type = 'cash_session' AND action IN ('cash_session.opened', 'cash_session.closed')) OR\n(entity_type = 'sale' AND action IN ('sale.confirmed', 'sale.voided')) OR\n(entity_type = 'transfer' AND action IN ('transfer.requested', 'transfer.approved', 'transfer.dispatched', 'transfer.received', 'transfer.cancelled')) OR\n(entity_type = 'cash_transfer' AND action IN ('cash_transfer.dispatched', 'cash_transfer.received')) OR\n(entity_type = 'tenant_commission_settings' AND action = 'commissions.settings_changed') OR\n(entity_type = 'commission_rule' AND action IN ('commission_rule.created', 'commission_rule.deactivated')) OR\n(entity_type = 'branch_stock_threshold' AND action = 'stock_threshold.changed') OR\n(entity_type = 'sync_device' AND action IN ('sync_device.registered', 'sync_device.revoked')) OR\n(entity_type = 'offline_sale_conflict' AND action = 'offline_sale_conflict.resolved')");
    }
}
