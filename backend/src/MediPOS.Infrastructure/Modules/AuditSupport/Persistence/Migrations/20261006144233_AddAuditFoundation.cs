using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.AuditSupport.Persistence.Migrations;

/// <inheritdoc />
public partial class AddAuditFoundation : Migration
{
    private static readonly string[] EntityIndex = ["tenant_id", "entity_type", "entity_id", "occurred_at"];
    private static readonly string[] TimeIndex = ["tenant_id", "occurred_at", "id"];
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "audit_logs",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                entity_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                correlation_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                before_json = table.Column<string>(type: "jsonb", nullable: true),
                after_json = table.Column<string>(type: "jsonb", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_audit_logs", x => x.id);
                table.CheckConstraint("ck_audit_logs_after", "after_json IS NULL OR jsonb_typeof(after_json) = 'object'");
                table.CheckConstraint("ck_audit_logs_before", "before_json IS NULL OR jsonb_typeof(before_json) = 'object'");
                table.CheckConstraint("ck_audit_logs_codes", "(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated'))");
                table.CheckConstraint("ck_audit_logs_correlation", "correlation_id ~ '^[0-9a-f]{32}$' AND correlation_id <> repeat('0', 32)");
                table.CheckConstraint("ck_audit_logs_identifiers", "actor_id <> '00000000-0000-0000-0000-000000000000'::uuid AND entity_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.ForeignKey(
                    name: "FK_audit_logs_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "tenant_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_audit_logs_tenant_id_entity_type_entity_id_occurred_at",
            table: "audit_logs",
            columns: EntityIndex);

        migrationBuilder.CreateIndex(
            name: "IX_audit_logs_tenant_id_occurred_at_id",
            table: "audit_logs",
            columns: TimeIndex);

        migrationBuilder.Sql("""
            ALTER TABLE audit_logs ENABLE ROW LEVEL SECURITY;
            ALTER TABLE audit_logs FORCE ROW LEVEL SECURITY;
            CREATE POLICY audit_logs_tenant_read ON audit_logs FOR SELECT
                USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
            CREATE POLICY audit_logs_tenant_insert ON audit_logs FOR INSERT
                WITH CHECK (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
            REVOKE UPDATE, DELETE, TRUNCATE ON audit_logs FROM PUBLIC;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit_logs");
    }
}
