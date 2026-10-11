using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.Notifications.Persistence.Migrations;

/// <inheritdoc />
public partial class AddPersistentExpirationNotifications : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "notifications",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                inventory_lot_id = table.Column<Guid>(type: "uuid", nullable: false),
                business_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                title = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                message = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                expiration_date = table.Column<DateOnly>(type: "date", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_notifications", x => x.id);
                table.UniqueConstraint("AK_notifications_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_notifications_text", "title ~ '[^[:space:]]' AND message ~ '[^[:space:]]'");
                table.CheckConstraint("ck_notifications_type", "type = 'lot_expiring_30d'");
                table.ForeignKey(
                    name: "FK_notifications_branches_tenant_id_branch_id",
                    columns: x => new { x.tenant_id, x.branch_id },
                    principalTable: "branches",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_notifications_business_products_tenant_id_business_product_~",
                    columns: x => new { x.tenant_id, x.business_product_id },
                    principalTable: "business_products",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_notifications_inventory_lots_tenant_id_inventory_lot_id_bra~",
                    columns: x => new { x.tenant_id, x.inventory_lot_id, x.branch_id, x.business_product_id },
                    principalTable: "inventory_lots",
                    principalColumns: ["tenant_id", "id", "branch_id", "business_product_id"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "notification_read_states",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_notification_read_states", x => x.id);
                table.ForeignKey(
                    name: "FK_notification_read_states_memberships_tenant_id_membership_id",
                    columns: x => new { x.tenant_id, x.membership_id },
                    principalTable: "memberships",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_notification_read_states_notifications_tenant_id_notificati~",
                    columns: x => new { x.tenant_id, x.notification_id },
                    principalTable: "notifications",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_notification_reads_membership",
            table: "notification_read_states",
            columns: ["tenant_id", "membership_id", "notification_id"]);

        migrationBuilder.CreateIndex(
            name: "ux_notification_read_membership",
            table: "notification_read_states",
            columns: ["tenant_id", "notification_id", "membership_id"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_notifications_branch_created",
            table: "notifications",
            columns: ["tenant_id", "branch_id", "created_at", "id"],
            descending: [false, false, true, true]);

        migrationBuilder.CreateIndex(
            name: "ix_notifications_created",
            table: "notifications",
            columns: ["tenant_id", "created_at", "id"],
            descending: [false, true, true]);

        migrationBuilder.CreateIndex(
            name: "IX_notifications_tenant_id_business_product_id",
            table: "notifications",
            columns: ["tenant_id", "business_product_id"]);

        migrationBuilder.CreateIndex(
            name: "IX_notifications_tenant_id_inventory_lot_id_branch_id_business~",
            table: "notifications",
            columns: ["tenant_id", "inventory_lot_id", "branch_id", "business_product_id"]);

        migrationBuilder.CreateIndex(
            name: "ux_notifications_expiration",
            table: "notifications",
            columns: ["tenant_id", "inventory_lot_id", "type", "expiration_date"],
            unique: true);
        migrationBuilder.Sql(NotificationHistorySql.Up);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(NotificationHistorySql.BeforeDown);
        migrationBuilder.DropTable(
            name: "notification_read_states");

        migrationBuilder.DropTable(
            name: "notifications");
    }
}
