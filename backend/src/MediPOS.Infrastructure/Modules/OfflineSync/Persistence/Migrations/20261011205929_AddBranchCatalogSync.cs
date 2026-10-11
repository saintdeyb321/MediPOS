using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.OfflineSync.Persistence.Migrations;
/// <inheritdoc />
public partial class AddBranchCatalogSync : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "branch_catalog_sync_states",
            columns: table => new
            {
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                current_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                minimum_retained_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                cursor_key = table.Column<byte[]>(type: "bytea", nullable: false, defaultValueSql: "decode(replace(gen_random_uuid()::text, '-', '') || replace(gen_random_uuid()::text, '-', ''), 'hex')")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_branch_catalog_sync_states", x => new { x.tenant_id, x.branch_id });
                table.CheckConstraint("ck_branch_catalog_state", "current_version >= 0 AND minimum_retained_version = 0 AND octet_length(cursor_key) = 32");
                table.ForeignKey(
                    name: "FK_branch_catalog_sync_states_branches_tenant_id_branch_id",
                    columns: x => new { x.tenant_id, x.branch_id },
                    principalTable: "branches",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "branch_catalog_changes",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                transaction_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                entity_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "product"),
                operation = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "pending"),
                schema_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                generated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                projection = table.Column<string>(type: "jsonb", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_branch_catalog_changes", x => x.id);
                table.CheckConstraint("ck_branch_catalog_change", "entity_type = 'product' AND schema_version = 1 AND entity_id <> '00000000-0000-0000-0000-000000000000'::uuid AND transaction_id ~ '^[0-9]{1,20}$' AND version >= 0");
                table.CheckConstraint("ck_branch_catalog_projection", "(version = 0 AND operation = 'pending' AND generated_at_utc IS NULL AND projection IS NULL) OR\n(version > 0 AND generated_at_utc IS NOT NULL AND isfinite(generated_at_utc) AND\n  ((operation = 'upsert' AND projection IS NOT NULL AND jsonb_typeof(projection) = 'object') OR (operation = 'tombstone' AND projection IS NULL)))");
                table.ForeignKey(
                    name: "FK_branch_catalog_changes_branch_catalog_sync_states_tenant_id~",
                    columns: x => new { x.tenant_id, x.branch_id },
                    principalTable: "branch_catalog_sync_states",
                    principalColumns: ["tenant_id", "branch_id"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_branch_catalog_changes_tenant_id_branch_id_transaction_id_e~",
            table: "branch_catalog_changes",
            columns: ["tenant_id", "branch_id", "transaction_id", "entity_id"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_branch_catalog_changes_tenant_id_branch_id_version",
            table: "branch_catalog_changes",
            columns: ["tenant_id", "branch_id", "version"],
            unique: true,
            filter: "version > 0");

        migrationBuilder.CreateIndex(
            name: "IX_branch_catalog_changes_transaction_id_tenant_id_branch_id_v~",
            table: "branch_catalog_changes",
            columns: ["transaction_id", "tenant_id", "branch_id", "version"]);
        migrationBuilder.Sql(BranchCatalogProjectionSql.Up);
        migrationBuilder.Sql(BranchCatalogSyncSql.Up);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(BranchCatalogSyncSql.Down);
        migrationBuilder.DropTable(
            name: "branch_catalog_changes");

        migrationBuilder.DropTable(
            name: "branch_catalog_sync_states");
    }
}
