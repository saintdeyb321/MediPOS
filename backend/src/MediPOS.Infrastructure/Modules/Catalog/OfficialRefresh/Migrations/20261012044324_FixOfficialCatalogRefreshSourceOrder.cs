using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.Catalog.OfficialRefresh.Migrations;

public partial class FixOfficialCatalogRefreshSourceOrder : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(OfficialCatalogRefreshSourceOrderSql.Up);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(OfficialCatalogRefreshSourceOrderSql.Down);
}
