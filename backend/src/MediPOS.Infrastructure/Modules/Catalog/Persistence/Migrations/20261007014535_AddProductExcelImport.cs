using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.Catalog.Persistence.Migrations;

/// <inheritdoc />
public partial class AddProductExcelImport : Migration
{
    private static readonly string[] TenantIdIdColumns = ["tenant_id", "id"];
    private static readonly string[] TenantCreatedColumns = ["tenant_id", "created_at"];
    private static readonly string[] TenantProductColumns = ["tenant_id", "business_product_id"];
    private static readonly string[] TenantJobRowColumns = ["tenant_id", "import_job_id", "row_number"];
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.CreateTable(
            name: "import_jobs",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                file_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                template_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                total_rows = table.Column<int>(type: "integer", nullable: false),
                valid_rows = table.Column<int>(type: "integer", nullable: false),
                invalid_rows = table.Column<int>(type: "integer", nullable: false),
                imported_rows = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                actor_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_import_jobs", x => x.id);
                table.UniqueConstraint("AK_import_jobs_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_import_jobs_actor", "actor_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.CheckConstraint("ck_import_jobs_completion", "(status = 'processing' AND completed_at IS NULL) OR (status IN ('completed', 'failed') AND completed_at IS NOT NULL AND completed_at >= created_at)");
                table.CheckConstraint("ck_import_jobs_counts", "total_rows BETWEEN 1 AND 5000 AND valid_rows = imported_rows AND imported_rows >= 0 AND invalid_rows >= 0 AND imported_rows + invalid_rows <= total_rows AND (status <> 'completed' OR imported_rows + invalid_rows = total_rows)");
                table.CheckConstraint("ck_import_jobs_status", "status IN ('processing', 'completed', 'failed')");
                table.CheckConstraint("ck_import_jobs_text", "length(btrim(file_name)) > 0 AND length(btrim(template_version)) > 0");
                table.ForeignKey(
                    name: "FK_import_jobs_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "tenant_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "import_row_results",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                import_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                row_number = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                business_product_id = table.Column<Guid>(type: "uuid", nullable: true),
                error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                error_message = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_import_row_results", x => x.id);
                table.CheckConstraint("ck_import_row_results_outcome", "(status = 'imported' AND business_product_id IS NOT NULL AND error_code IS NULL AND error_message IS NULL)\nOR (status = 'invalid' AND business_product_id IS NULL AND error_code IS NOT NULL\n    AND length(btrim(error_code)) > 0 AND error_message IS NOT NULL AND length(btrim(error_message)) > 0)");
                table.CheckConstraint("ck_import_row_results_row", "row_number BETWEEN 4 AND 5003");
                table.ForeignKey(
                    name: "FK_import_row_results_business_products_tenant_id_business_pro~",
                    columns: x => new { x.tenant_id, x.business_product_id },
                    principalTable: "business_products",
                    principalColumns: TenantIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_import_row_results_import_jobs_tenant_id_import_job_id",
                    columns: x => new { x.tenant_id, x.import_job_id },
                    principalTable: "import_jobs",
                    principalColumns: TenantIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted') OR\n(entity_type = 'import_job' AND action = 'catalog.products_imported')");

        migrationBuilder.CreateIndex(
            name: "IX_import_jobs_tenant_id_created_at",
            table: "import_jobs",
            columns: TenantCreatedColumns);

        migrationBuilder.CreateIndex(
            name: "IX_import_row_results_tenant_id_business_product_id",
            table: "import_row_results",
            columns: TenantProductColumns);

        migrationBuilder.CreateIndex(
            name: "IX_import_row_results_tenant_id_import_job_id_row_number",
            table: "import_row_results",
            columns: TenantJobRowColumns,
            unique: true);

        migrationBuilder.Sql("""
                ALTER TABLE import_jobs ENABLE ROW LEVEL SECURITY;
                ALTER TABLE import_jobs FORCE ROW LEVEL SECURITY;
                CREATE POLICY import_jobs_select ON import_jobs FOR SELECT
                    USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
                CREATE POLICY import_jobs_insert ON import_jobs FOR INSERT
                    WITH CHECK (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
                CREATE POLICY import_jobs_update ON import_jobs FOR UPDATE
                    USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid AND status = 'processing')
                    WITH CHECK (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
                ALTER TABLE import_row_results ENABLE ROW LEVEL SECURITY;
                ALTER TABLE import_row_results FORCE ROW LEVEL SECURITY;
                CREATE POLICY import_row_results_select ON import_row_results FOR SELECT
                    USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
                CREATE POLICY import_row_results_insert ON import_row_results FOR INSERT
                    WITH CHECK (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid AND EXISTS (
                        SELECT 1 FROM import_jobs job WHERE job.tenant_id = import_row_results.tenant_id
                            AND job.id = import_row_results.import_job_id AND job.status = 'processing'));
                """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "import_row_results");

        migrationBuilder.DropTable(
            name: "import_jobs");

        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted')");
    }
}
