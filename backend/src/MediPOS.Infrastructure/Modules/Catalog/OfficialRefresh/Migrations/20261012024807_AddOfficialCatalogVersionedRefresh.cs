using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.Catalog.OfficialRefresh.Migrations;

/// <inheritdoc />
public partial class AddOfficialCatalogVersionedRefresh : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_curation_audit",
            table: "official_catalog_platform_audits");

        migrationBuilder.CreateTable(
            name: "official_catalog_refreshes",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                global_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                source_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                source_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                official_identity = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                import_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                staging_row_id = table.Column<Guid>(type: "uuid", nullable: true),
                curation_id = table.Column<Guid>(type: "uuid", nullable: true),
                source_checksum_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                row_checksum_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                previous_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                proposed_snapshot = table.Column<string>(type: "jsonb", nullable: true),
                changed_fields = table.Column<string>(type: "jsonb", nullable: false),
                change_classification = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                product_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                base_product_version = table.Column<long>(type: "bigint", nullable: false),
                applied_product_version = table.Column<long>(type: "bigint", nullable: true),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false),
                approved_version = table.Column<long>(type: "bigint", nullable: true),
                created_by_actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                reviewed_by_actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                reviewed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                reason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                referred_official_identity = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                applied_by_actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                applied_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_official_catalog_refreshes", x => x.id);
                table.CheckConstraint("ck_refresh_application", "(status = 'applied' AND applied_product_version = base_product_version + 1 AND applied_product_version IS NOT NULL AND applied_by_actor_id IS NOT NULL AND applied_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid AND applied_at_utc IS NOT NULL AND isfinite(applied_at_utc) AND applied_at_utc >= reviewed_at_utc AND change_classification IN ('unchanged','metadata_changed')) OR (status <> 'applied' AND applied_product_version IS NULL AND applied_by_actor_id IS NULL AND applied_at_utc IS NULL)");
                table.CheckConstraint("ck_refresh_approval", "(status = 'approved' AND approved_version = version AND approved_version IS NOT NULL) OR (status = 'applied' AND approved_version = version - 1 AND approved_version IS NOT NULL) OR (status NOT IN ('approved','applied') AND approved_version IS NULL)");
                table.CheckConstraint("ck_refresh_data", "jsonb_typeof(previous_snapshot) = 'object' AND octet_length(previous_snapshot::text) <= 524288 AND (proposed_snapshot IS NULL OR (jsonb_typeof(proposed_snapshot) = 'object' AND octet_length(proposed_snapshot::text) <= 524288)) AND jsonb_typeof(changed_fields) = 'array' AND jsonb_array_length(changed_fields) <= 16");
                table.CheckConstraint("ck_refresh_identity", "length(btrim(source_code)) > 0 AND length(btrim(source_version)) > 0 AND length(btrim(official_identity)) > 0 AND source_checksum_sha256 ~ '^[0-9a-f]{64}$' AND product_fingerprint ~ '^[0-9a-f]{64}$' AND ((curation_id IS NULL AND staging_row_id IS NULL AND row_checksum_sha256 IS NULL AND proposed_snapshot IS NULL AND change_classification = 'missing_from_source') OR (curation_id IS NOT NULL AND staging_row_id IS NOT NULL AND row_checksum_sha256 ~ '^[0-9a-f]{64}$' AND proposed_snapshot IS NOT NULL))");
                table.CheckConstraint("ck_refresh_referral", "(status = 'referred' AND referred_official_identity IS NOT NULL AND length(btrim(referred_official_identity)) > 0 AND referred_official_identity <> official_identity AND change_classification IN ('clinically_significant_change','identity_conflict')) OR (status <> 'referred' AND referred_official_identity IS NULL)");
                table.CheckConstraint("ck_refresh_review", "(version = 1 AND reviewed_by_actor_id IS NULL AND reviewed_at_utc IS NULL AND reason IS NULL) OR (version > 1 AND reviewed_by_actor_id IS NOT NULL AND reviewed_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid AND reviewed_at_utc IS NOT NULL AND isfinite(reviewed_at_utc) AND reviewed_at_utc >= created_at_utc AND reason IS NOT NULL AND length(btrim(reason)) > 0)");
                table.CheckConstraint("ck_refresh_status", "status IN ('pending_review','requires_review','approved','rejected','kept_previous','referred','applied') AND change_classification IN ('unchanged','metadata_changed','clinically_significant_change','identity_conflict','missing_from_source','requires_review') AND version >= 1 AND base_product_version >= 0 AND created_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid AND isfinite(created_at_utc)");
                table.ForeignKey(
                    name: "FK_official_catalog_refreshes_global_products_global_product_id",
                    column: x => x.global_product_id,
                    principalTable: "global_products",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_official_catalog_refreshes_official_catalog_curations_curat~",
                    column: x => x.curation_id,
                    principalTable: "official_catalog_curations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_official_catalog_refreshes_official_catalog_import_batches_~",
                    column: x => x.import_batch_id,
                    principalTable: "official_catalog_import_batches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_official_catalog_refreshes_official_catalog_publications_pu~",
                    column: x => x.publication_id,
                    principalTable: "official_catalog_publications",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_official_catalog_refreshes_official_catalog_staging_rows_st~",
                    column: x => x.staging_row_id,
                    principalTable: "official_catalog_staging_rows",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "official_catalog_version_history",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                refresh_id = table.Column<Guid>(type: "uuid", nullable: false),
                global_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                source_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                source_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                official_identity = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                import_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                staging_row_id = table.Column<Guid>(type: "uuid", nullable: true),
                curation_id = table.Column<Guid>(type: "uuid", nullable: true),
                previous_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                proposed_snapshot = table.Column<string>(type: "jsonb", nullable: true),
                approved_snapshot = table.Column<string>(type: "jsonb", nullable: true),
                changed_fields = table.Column<string>(type: "jsonb", nullable: false),
                change_classification = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                reviewed_by_actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                reviewed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                applied_by_actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                applied_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false),
                base_product_version = table.Column<long>(type: "bigint", nullable: false),
                applied_product_version = table.Column<long>(type: "bigint", nullable: true),
                action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                reason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                correlation_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                referred_official_identity = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_official_catalog_version_history", x => x.id);
                table.CheckConstraint("ck_refresh_history_data", "jsonb_typeof(previous_snapshot) = 'object' AND octet_length(previous_snapshot::text) <= 524288 AND (proposed_snapshot IS NULL OR (jsonb_typeof(proposed_snapshot) = 'object' AND octet_length(proposed_snapshot::text) <= 524288)) AND (approved_snapshot IS NULL OR (jsonb_typeof(approved_snapshot) = 'object' AND octet_length(approved_snapshot::text) <= 524288)) AND jsonb_typeof(changed_fields) = 'array' AND jsonb_array_length(changed_fields) <= 16");
                table.CheckConstraint("ck_refresh_history_event", "version >= 1 AND base_product_version >= 0 AND actor_id <> '00000000-0000-0000-0000-000000000000'::uuid AND isfinite(at_utc) AND length(btrim(reason)) > 0 AND correlation_id ~ '^[0-9a-f]{32}$' AND correlation_id <> repeat('0',32) AND action IN ('prepared','save_correction','approve','reject','request_review','keep_previous','refer_new_identity','applied')");
                table.ForeignKey(
                    name: "FK_official_catalog_version_history_global_products_global_pro~",
                    column: x => x.global_product_id,
                    principalTable: "global_products",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_official_catalog_version_history_official_catalog_curations~",
                    column: x => x.curation_id,
                    principalTable: "official_catalog_curations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_official_catalog_version_history_official_catalog_import_ba~",
                    column: x => x.import_batch_id,
                    principalTable: "official_catalog_import_batches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_official_catalog_version_history_official_catalog_refreshes~",
                    column: x => x.refresh_id,
                    principalTable: "official_catalog_refreshes",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_official_catalog_version_history_official_catalog_staging_r~",
                    column: x => x.staging_row_id,
                    principalTable: "official_catalog_staging_rows",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_curation_audit",
            table: "official_catalog_platform_audits",
            sql: "actor_id <> '00000000-0000-0000-0000-000000000000'::uuid AND candidate_id <> '00000000-0000-0000-0000-000000000000'::uuid AND isfinite(at_utc) AND correlation_id ~ '^[0-9a-f]{32}$' AND correlation_id <> repeat('0',32) AND (version IS NULL OR version >= 1) AND action IN ('normalized','save_correction','approve','reject','request_correction','published','prepare_failed','review_failed','publish_failed','refresh_prepared','refresh_save_correction','refresh_approve','refresh_reject','refresh_request_review','refresh_keep_previous','refresh_refer_new_identity','refresh_applied','refresh_prepare_failed','refresh_review_failed','refresh_apply_failed') AND ((action LIKE '%_failed' AND failure_code IS NOT NULL) OR (action NOT LIKE '%_failed' AND failure_code IS NULL))");

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_refreshes_change_classification_created_at~",
            table: "official_catalog_refreshes",
            columns: ["change_classification", "created_at_utc", "id"]);

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_refreshes_curation_id",
            table: "official_catalog_refreshes",
            column: "curation_id",
            unique: true,
            filter: "curation_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_refreshes_global_product_id_applied_produc~",
            table: "official_catalog_refreshes",
            columns: ["global_product_id", "applied_product_version"],
            unique: true,
            filter: "applied_product_version IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_refreshes_global_product_id_created_at_utc~",
            table: "official_catalog_refreshes",
            columns: ["global_product_id", "created_at_utc", "id"]);

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_refreshes_import_batch_id",
            table: "official_catalog_refreshes",
            column: "import_batch_id");

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_refreshes_publication_id",
            table: "official_catalog_refreshes",
            column: "publication_id");

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_refreshes_source_code_source_version_offic~",
            table: "official_catalog_refreshes",
            columns: ["source_code", "source_version", "official_identity"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_refreshes_source_code_source_version_statu~",
            table: "official_catalog_refreshes",
            columns: ["source_code", "source_version", "status", "created_at_utc", "id"]);

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_refreshes_staging_row_id",
            table: "official_catalog_refreshes",
            column: "staging_row_id");

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_version_history_curation_id",
            table: "official_catalog_version_history",
            column: "curation_id");

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_version_history_global_product_id_at_utc_id",
            table: "official_catalog_version_history",
            columns: ["global_product_id", "at_utc", "id"]);

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_version_history_import_batch_id",
            table: "official_catalog_version_history",
            column: "import_batch_id");

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_version_history_refresh_id_version",
            table: "official_catalog_version_history",
            columns: ["refresh_id", "version"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_version_history_source_code_source_version~",
            table: "official_catalog_version_history",
            columns: ["source_code", "source_version", "at_utc", "id"]);

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_version_history_staging_row_id",
            table: "official_catalog_version_history",
            column: "staging_row_id");
        migrationBuilder.Sql(OfficialCatalogRefreshSql.Up);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(OfficialCatalogRefreshSql.Down);
        migrationBuilder.DropTable(
            name: "official_catalog_version_history");

        migrationBuilder.DropTable(
            name: "official_catalog_refreshes");

        migrationBuilder.DropCheckConstraint(
            name: "ck_curation_audit",
            table: "official_catalog_platform_audits");

        migrationBuilder.AddCheckConstraint(
            name: "ck_curation_audit",
            table: "official_catalog_platform_audits",
            sql: "actor_id <> '00000000-0000-0000-0000-000000000000'::uuid AND candidate_id <> '00000000-0000-0000-0000-000000000000'::uuid AND isfinite(at_utc) AND correlation_id ~ '^[0-9a-f]{32}$' AND correlation_id <> repeat('0',32) AND (version IS NULL OR version >= 1) AND action IN ('normalized','save_correction','approve','reject','request_correction','published','prepare_failed','review_failed','publish_failed') AND ((action LIKE '%_failed' AND failure_code IS NOT NULL) OR (action NOT LIKE '%_failed' AND failure_code IS NULL))");
    }
}
