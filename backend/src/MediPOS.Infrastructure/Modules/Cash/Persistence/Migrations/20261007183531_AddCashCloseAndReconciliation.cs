using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.Cash.Persistence.Migrations;

/// <inheritdoc />
public partial class AddCashCloseAndReconciliation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.AddColumn<decimal>(
            name: "cash_difference",
            table: "cash_sessions",
            type: "numeric(28,4)",
            precision: 28,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "closed_at",
            table: "cash_sessions",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "closed_by_actor_id",
            table: "cash_sessions",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "counted_cash_amount",
            table: "cash_sessions",
            type: "numeric(28,4)",
            precision: 28,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "expected_cash_amount",
            table: "cash_sessions",
            type: "numeric(28,4)",
            precision: 28,
            scale: 4,
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_cash_sessions_closure",
            table: "cash_sessions",
            sql: "(status = 'open' AND closed_at IS NULL AND closed_by_actor_id IS NULL AND counted_cash_amount IS NULL AND expected_cash_amount IS NULL AND cash_difference IS NULL) OR\n(status = 'closed' AND closed_at IS NOT NULL AND closed_at >= opened_at AND closed_by_actor_id IS NOT NULL\n    AND closed_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid AND counted_cash_amount IS NOT NULL\n    AND counted_cash_amount >= 0 AND counted_cash_amount <= 999999999999999999999999.9999 AND expected_cash_amount IS NOT NULL\n    AND expected_cash_amount >= 0 AND expected_cash_amount <= 999999999999999999999999.9999 AND cash_difference IS NOT NULL\n    AND cash_difference = counted_cash_amount - expected_cash_amount)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted') OR\n(entity_type = 'import_job' AND action = 'catalog.products_imported') OR\n(entity_type = 'cash_session' AND action IN ('cash_session.opened', 'cash_session.closed')) OR\n(entity_type = 'sale' AND action IN ('sale.confirmed', 'sale.voided'))");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            SET LOCAL row_security = off;
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM cash_sessions WHERE status = 'closed')
                    OR EXISTS (SELECT 1 FROM audit_logs WHERE action = 'cash_session.closed') THEN
                    RAISE EXCEPTION 'Cannot downgrade cash close while reconciliation history exists.';
                END IF;
            END $$;
            SET LOCAL row_security = on;
            """);
        migrationBuilder.DropCheckConstraint(
            name: "ck_cash_sessions_closure",
            table: "cash_sessions");

        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.DropColumn(
            name: "cash_difference",
            table: "cash_sessions");

        migrationBuilder.DropColumn(
            name: "closed_at",
            table: "cash_sessions");

        migrationBuilder.DropColumn(
            name: "closed_by_actor_id",
            table: "cash_sessions");

        migrationBuilder.DropColumn(
            name: "counted_cash_amount",
            table: "cash_sessions");

        migrationBuilder.DropColumn(
            name: "expected_cash_amount",
            table: "cash_sessions");

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted') OR\n(entity_type = 'import_job' AND action = 'catalog.products_imported') OR\n(entity_type = 'cash_session' AND action = 'cash_session.opened') OR\n(entity_type = 'sale' AND action IN ('sale.confirmed', 'sale.voided'))");
    }
}
