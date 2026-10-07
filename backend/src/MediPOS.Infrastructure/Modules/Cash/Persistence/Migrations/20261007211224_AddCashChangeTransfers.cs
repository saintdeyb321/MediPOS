using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.Cash.Persistence.Migrations;
/// <inheritdoc />
public partial class AddCashChangeTransfers : Migration
{
    private static readonly string[] TenantIdBranchIdIdColumns = ["tenant_id", "branch_id", "id"];
    private static readonly string[] TenantIdIdColumns = ["tenant_id", "id"];
    private static readonly string[] TenantIdDestinationBranchIdDestinationCashSessionIdColumns = ["tenant_id", "destination_branch_id", "destination_cash_session_id"];
    private static readonly string[] TenantIdDestinationBranchIdStatusColumns = ["tenant_id", "destination_branch_id", "status"];
    private static readonly string[] TenantIdDestinationCashSessionIdColumns = ["tenant_id", "destination_cash_session_id"];
    private static readonly string[] TenantIdSourceBranchIdSourceCashSessionIdColumns = ["tenant_id", "source_branch_id", "source_cash_session_id"];
    private static readonly string[] TenantIdSourceCashSessionIdStatusColumns = ["tenant_id", "source_cash_session_id", "status"];
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.AddUniqueConstraint(
            name: "AK_cash_sessions_tenant_id_branch_id_id",
            table: "cash_sessions",
            columns: TenantIdBranchIdIdColumns);

        migrationBuilder.CreateTable(
            name: "cash_transfers",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                source_branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                source_cash_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                destination_branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                destination_cash_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                dispatched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                dispatched_by_actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                received_by_actor_id = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_cash_transfers", x => x.id);
                table.CheckConstraint("ck_cash_transfers_amount", "amount > 0 AND amount <= 99999999999999.9999");
                table.CheckConstraint("ck_cash_transfers_ids", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND dispatched_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.CheckConstraint("ck_cash_transfers_receipt", "(status = 'in_transit' AND destination_cash_session_id IS NULL AND received_at IS NULL AND received_by_actor_id IS NULL) OR\n(status = 'received' AND destination_cash_session_id IS NOT NULL AND destination_cash_session_id <> source_cash_session_id\n    AND received_at IS NOT NULL AND received_at >= dispatched_at AND received_by_actor_id IS NOT NULL\n    AND received_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid)");
                table.ForeignKey(
                    name: "FK_cash_transfers_branches_tenant_id_destination_branch_id",
                    columns: x => new { x.tenant_id, x.destination_branch_id },
                    principalTable: "branches",
                    principalColumns: TenantIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_cash_transfers_branches_tenant_id_source_branch_id",
                    columns: x => new { x.tenant_id, x.source_branch_id },
                    principalTable: "branches",
                    principalColumns: TenantIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_cash_transfers_cash_sessions_tenant_id_destination_branch_i~",
                    columns: x => new { x.tenant_id, x.destination_branch_id, x.destination_cash_session_id },
                    principalTable: "cash_sessions",
                    principalColumns: TenantIdBranchIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_cash_transfers_cash_sessions_tenant_id_source_branch_id_sou~",
                    columns: x => new { x.tenant_id, x.source_branch_id, x.source_cash_session_id },
                    principalTable: "cash_sessions",
                    principalColumns: TenantIdBranchIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted') OR\n(entity_type = 'import_job' AND action = 'catalog.products_imported') OR\n(entity_type = 'cash_session' AND action IN ('cash_session.opened', 'cash_session.closed')) OR\n(entity_type = 'sale' AND action IN ('sale.confirmed', 'sale.voided')) OR\n(entity_type = 'transfer' AND action IN ('transfer.requested', 'transfer.approved', 'transfer.dispatched', 'transfer.received', 'transfer.cancelled')) OR\n(entity_type = 'cash_transfer' AND action IN ('cash_transfer.dispatched', 'cash_transfer.received'))");

        migrationBuilder.CreateIndex(
            name: "IX_cash_transfers_tenant_id_destination_branch_id_destination_~",
            table: "cash_transfers",
            columns: TenantIdDestinationBranchIdDestinationCashSessionIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_cash_transfers_tenant_id_destination_branch_id_status",
            table: "cash_transfers",
            columns: TenantIdDestinationBranchIdStatusColumns);

        migrationBuilder.CreateIndex(
            name: "IX_cash_transfers_tenant_id_destination_cash_session_id",
            table: "cash_transfers",
            columns: TenantIdDestinationCashSessionIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_cash_transfers_tenant_id_source_branch_id_source_cash_sessi~",
            table: "cash_transfers",
            columns: TenantIdSourceBranchIdSourceCashSessionIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_cash_transfers_tenant_id_source_cash_session_id_status",
            table: "cash_transfers",
            columns: TenantIdSourceCashSessionIdStatusColumns);
        migrationBuilder.Sql("""
            CREATE FUNCTION guard_cash_transfer_history() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF TG_OP = 'DELETE' THEN
                    RAISE EXCEPTION 'Cash transfer history cannot be deleted.'
                        USING ERRCODE = '23514', CONSTRAINT = 'ck_cash_transfer_history';
                ELSIF TG_OP = 'INSERT' THEN
                    IF NEW.status <> 'in_transit' THEN
                        RAISE EXCEPTION 'Cash transfers start in transit.'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_cash_transfer_history';
                    END IF;
                ELSIF OLD.status <> 'in_transit' OR NEW.status <> 'received'
                    OR (to_jsonb(OLD) - ARRAY['status','destination_cash_session_id','received_at','received_by_actor_id'])
                        <> (to_jsonb(NEW) - ARRAY['status','destination_cash_session_id','received_at','received_by_actor_id']) THEN
                    RAISE EXCEPTION 'Only one immutable cash transfer receipt is permitted.'
                        USING ERRCODE = '23514', CONSTRAINT = 'ck_cash_transfer_history';
                END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER cash_transfer_history BEFORE INSERT OR UPDATE OR DELETE ON cash_transfers
                FOR EACH ROW EXECUTE FUNCTION guard_cash_transfer_history();
            ALTER TABLE cash_transfers ENABLE ROW LEVEL SECURITY;
            ALTER TABLE cash_transfers FORCE ROW LEVEL SECURITY;
            CREATE POLICY cash_transfers_tenant_isolation ON cash_transfers
                USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid)
                WITH CHECK (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            SET LOCAL row_security = off;
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM cash_transfers) OR EXISTS (SELECT 1 FROM audit_logs WHERE entity_type = 'cash_transfer') THEN
                    RAISE EXCEPTION 'Cannot downgrade while cash transfer history exists.';
                END IF;
            END $$;
            SET LOCAL row_security = on;
            DROP TRIGGER cash_transfer_history ON cash_transfers;
            DROP FUNCTION guard_cash_transfer_history();
            """);
        migrationBuilder.DropTable(
            name: "cash_transfers");

        migrationBuilder.DropUniqueConstraint(
            name: "AK_cash_sessions_tenant_id_branch_id_id",
            table: "cash_sessions");

        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted') OR\n(entity_type = 'import_job' AND action = 'catalog.products_imported') OR\n(entity_type = 'cash_session' AND action IN ('cash_session.opened', 'cash_session.closed')) OR\n(entity_type = 'sale' AND action IN ('sale.confirmed', 'sale.voided')) OR\n(entity_type = 'transfer' AND action IN ('transfer.requested', 'transfer.approved', 'transfer.dispatched', 'transfer.received', 'transfer.cancelled'))");
    }
}
