using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.IdentityAccess.Persistence.Migrations;
    /// <inheritdoc />
    public partial class AddInitialOwnerEmailOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_logs_codes",
                table: "audit_logs");

            migrationBuilder.AddColumn<bool>(
                name: "google_email_verified",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "initial_owner_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    invited_by = table.Column<Guid>(type: "uuid", nullable: false),
                    creation_request_key = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    claimed_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    membership_id = table.Column<Guid>(type: "uuid", nullable: true),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_initial_owner_invitations", x => x.id);
                    table.CheckConstraint("ck_initial_owner_email", "length(normalized_email) BETWEEN 3 AND 320 AND normalized_email = lower(btrim(normalized_email)) AND normalized_email LIKE '%@%'");
                    table.CheckConstraint("ck_initial_owner_state", "(status = 'pending' AND claimed_user_id IS NULL AND membership_id IS NULL AND claimed_at IS NULL AND revoked_by IS NULL AND revoked_at IS NULL) OR (status = 'claimed' AND claimed_user_id IS NOT NULL AND membership_id IS NOT NULL AND claimed_at IS NOT NULL AND claimed_at >= created_at AND revoked_by IS NULL AND revoked_at IS NULL) OR (status = 'revoked' AND claimed_user_id IS NULL AND membership_id IS NULL AND claimed_at IS NULL AND revoked_by IS NOT NULL AND revoked_at IS NOT NULL AND revoked_at >= created_at)");
                    table.ForeignKey(
                        name: "FK_initial_owner_invitations_memberships_tenant_id_membership_~",
                        columns: x => new { x.tenant_id, x.membership_id },
                        principalTable: "memberships",
                        principalColumns: ["tenant_id", "id"],
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_initial_owner_invitations_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "tenant_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_initial_owner_invitations_users_claimed_user_id",
                        column: x => x.claimed_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_initial_owner_invitations_users_invited_by",
                        column: x => x.invited_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_initial_owner_invitations_users_revoked_by",
                        column: x => x.revoked_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_logs_codes",
                table: "audit_logs",
                sql: "(entity_type = 'electronic_series' AND action = 'cpe.series_configured') OR\r\n(entity_type = 'electronic_document' AND action IN ('cpe.requested','cpe.reviewed','cpe.delivery_changed')) OR\r\n(entity_type = 'supplier_ubl_import' AND action IN ('supplier_ubl.imported','supplier_ubl.reviewed','supplier_ubl.rejected','supplier_ubl.converted','supplier_ubl.file_rejected')) OR\r\n(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\r\n(entity_type = 'initial_owner_invitation' AND action IN ('initial_owner.invited', 'initial_owner.claimed', 'initial_owner.revoked')) OR\r\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\r\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\r\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\r\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\r\n(entity_type = 'employee_invitation' AND action IN ('employee_invitation.created','employee_invitation.accepted','employee_invitation.approved','employee_invitation.revoked')) OR\r\n(entity_type = 'business_product' AND action IN ('business_product.data_corrected', 'business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\r\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\r\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted') OR\r\n(entity_type = 'import_job' AND action = 'catalog.products_imported') OR\r\n(entity_type = 'cash_session' AND action IN ('cash_session.opened', 'cash_session.closed')) OR\r\n(entity_type = 'sale' AND action IN ('sale.confirmed', 'sale.voided')) OR\r\n(entity_type = 'transfer' AND action IN ('transfer.requested', 'transfer.approved', 'transfer.dispatched', 'transfer.received', 'transfer.cancelled')) OR\r\n(entity_type = 'cash_transfer' AND action IN ('cash_transfer.dispatched', 'cash_transfer.received')) OR\r\n(entity_type = 'tenant_commission_settings' AND action = 'commissions.settings_changed') OR\r\n(entity_type = 'commission_rule' AND action IN ('commission_rule.created', 'commission_rule.deactivated')) OR\r\n(entity_type = 'branch_stock_threshold' AND action = 'stock_threshold.changed') OR\r\n(entity_type = 'sync_device' AND action IN ('sync_device.registered', 'sync_device.revoked')) OR\r\n(entity_type = 'offline_sale_conflict' AND action = 'offline_sale_conflict.resolved')");

            migrationBuilder.CreateIndex(
                name: "IX_initial_owner_invitations_claimed_user_id",
                table: "initial_owner_invitations",
                column: "claimed_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_initial_owner_invitations_invited_by",
                table: "initial_owner_invitations",
                column: "invited_by");

            migrationBuilder.CreateIndex(
                name: "IX_initial_owner_invitations_normalized_email",
                table: "initial_owner_invitations",
                column: "normalized_email",
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "IX_initial_owner_invitations_revoked_by",
                table: "initial_owner_invitations",
                column: "revoked_by");

            migrationBuilder.CreateIndex(
                name: "IX_initial_owner_invitations_tenant_id_membership_id",
                table: "initial_owner_invitations",
                columns: ["tenant_id", "membership_id"]);

            migrationBuilder.CreateIndex(
                name: "ux_initial_owner_current",
                table: "initial_owner_invitations",
                column: "tenant_id",
                unique: true,
                filter: "status IN ('pending','claimed')");

            migrationBuilder.Sql(InitialOwnerInvitationSql.Install);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $owner_downgrade$
                BEGIN
                    IF EXISTS (SELECT 1 FROM initial_owner_invitations)
                        OR EXISTS (SELECT 1 FROM users WHERE google_email_verified)
                        OR EXISTS (SELECT 1 FROM audit_logs WHERE entity_type = 'initial_owner_invitation') THEN
                        RAISE EXCEPTION 'Used Owner onboarding history cannot be downgraded';
                    END IF;
                END $owner_downgrade$;
                """);
            migrationBuilder.Sql(InitialOwnerInvitationSql.Uninstall);
            migrationBuilder.DropTable(
                name: "initial_owner_invitations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_logs_codes",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "google_email_verified",
                table: "users");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_logs_codes",
                table: "audit_logs",
                sql: "(entity_type = 'electronic_series' AND action = 'cpe.series_configured') OR\r\n(entity_type = 'electronic_document' AND action IN ('cpe.requested','cpe.reviewed','cpe.delivery_changed')) OR\r\n(entity_type = 'supplier_ubl_import' AND action IN ('supplier_ubl.imported','supplier_ubl.reviewed','supplier_ubl.rejected','supplier_ubl.converted','supplier_ubl.file_rejected')) OR\r\n(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\r\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\r\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\r\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\r\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\r\n(entity_type = 'employee_invitation' AND action IN ('employee_invitation.created','employee_invitation.accepted','employee_invitation.approved','employee_invitation.revoked')) OR\r\n(entity_type = 'business_product' AND action IN ('business_product.data_corrected', 'business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\r\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\r\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted') OR\r\n(entity_type = 'import_job' AND action = 'catalog.products_imported') OR\r\n(entity_type = 'cash_session' AND action IN ('cash_session.opened', 'cash_session.closed')) OR\r\n(entity_type = 'sale' AND action IN ('sale.confirmed', 'sale.voided')) OR\r\n(entity_type = 'transfer' AND action IN ('transfer.requested', 'transfer.approved', 'transfer.dispatched', 'transfer.received', 'transfer.cancelled')) OR\r\n(entity_type = 'cash_transfer' AND action IN ('cash_transfer.dispatched', 'cash_transfer.received')) OR\r\n(entity_type = 'tenant_commission_settings' AND action = 'commissions.settings_changed') OR\r\n(entity_type = 'commission_rule' AND action IN ('commission_rule.created', 'commission_rule.deactivated')) OR\r\n(entity_type = 'branch_stock_threshold' AND action = 'stock_threshold.changed') OR\r\n(entity_type = 'sync_device' AND action IN ('sync_device.registered', 'sync_device.revoked')) OR\r\n(entity_type = 'offline_sale_conflict' AND action = 'offline_sale_conflict.resolved')");
        }
    }
