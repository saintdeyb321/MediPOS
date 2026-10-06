using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.Branches.Persistence.Migrations;

/// <inheritdoc />
public partial class AddLegalEntitiesAndBranches : Migration
{
    private static readonly string[] LegalEntityTenantKeyColumns = ["tenant_id", "id"];
    private static readonly string[] BranchLegalEntityIndexColumns = ["tenant_id", "legal_entity_id"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "legal_entities",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                legal_name = table.Column<string>(type: "text", nullable: false),
                ruc = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_legal_entities", x => x.id);
                table.UniqueConstraint("AK_legal_entities_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_legal_entities_name", "legal_name ~ '[^[:space:]]'");
                table.CheckConstraint("ck_legal_entities_ruc", "ruc ~ '[^[:space:]]'");
                table.ForeignKey(
                    name: "FK_legal_entities_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "tenant_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "branches",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                is_main_hub = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_branches", x => x.id);
                table.CheckConstraint("ck_branches_name", "name ~ '[^[:space:]]'");
                table.ForeignKey(
                    name: "FK_branches_legal_entities_tenant_id_legal_entity_id",
                    columns: x => new { x.tenant_id, x.legal_entity_id },
                    principalTable: "legal_entities",
                    principalColumns: LegalEntityTenantKeyColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_branches_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "tenant_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_branches_tenant_id_legal_entity_id",
            table: "branches",
            columns: BranchLegalEntityIndexColumns);

        migrationBuilder.CreateIndex(
            name: "ux_branches_tenant_main_hub",
            table: "branches",
            column: "tenant_id",
            unique: true,
            filter: "is_main_hub");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "branches");

        migrationBuilder.DropTable(
            name: "legal_entities");
    }
}
