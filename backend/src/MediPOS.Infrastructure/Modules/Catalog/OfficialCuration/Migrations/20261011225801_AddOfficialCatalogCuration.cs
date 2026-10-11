using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.Catalog.OfficialCuration.Migrations;

/// <inheritdoc />
public partial class AddOfficialCatalogCuration : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "official_catalog_curations",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                import_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                staging_row_id = table.Column<Guid>(type: "uuid", nullable: false),
                source_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                source_record_id = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                normalized_candidate = table.Column<string>(type: "jsonb", nullable: false),
                errors = table.Column<string[]>(type: "text[]", nullable: false),
                duplicate_evidence = table.Column<string>(type: "jsonb", nullable: false),
                has_potential_matches = table.Column<bool>(type: "boolean", nullable: false),
                review_notes = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                reviewed_by_actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                reviewed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                approved_version = table.Column<long>(type: "bigint", nullable: true),
                duplicate_resolution = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                existing_global_product_id = table.Column<Guid>(type: "uuid", nullable: true),
                published_global_product_id = table.Column<Guid>(type: "uuid", nullable: true),
                published_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_official_catalog_curations", x => x.id);
                table.CheckConstraint("ck_curation_approval", "(status = 'approved' AND approved_version IS NOT NULL AND approved_version = version AND reviewed_by_actor_id IS NOT NULL AND cardinality(errors) = 0) OR (status = 'published' AND approved_version IS NOT NULL AND approved_version = version - 1 AND reviewed_by_actor_id IS NOT NULL AND cardinality(errors) = 0) OR (status NOT IN ('approved','published') AND approved_version IS NULL)");
                table.CheckConstraint("ck_curation_errors", "cardinality(errors) <= 32 AND array_position(errors,NULL) IS NULL AND length(array_to_string(errors,',')) <= 3072");
                table.CheckConstraint("ck_curation_json", "jsonb_typeof(normalized_candidate) = 'object' AND octet_length(normalized_candidate::text) <= 262144 AND jsonb_typeof(duplicate_evidence) = 'array' AND jsonb_array_length(duplicate_evidence) <= 101");
                table.CheckConstraint("ck_curation_publication", "(status = 'published' AND published_global_product_id IS NOT NULL AND published_at_utc IS NOT NULL AND isfinite(published_at_utc) AND published_at_utc >= reviewed_at_utc) OR (status <> 'published' AND published_global_product_id IS NULL AND published_at_utc IS NULL)");
                table.CheckConstraint("ck_curation_resolution", "duplicate_resolution IN ('unresolved','distinct','link_existing') AND ((duplicate_resolution = 'link_existing' AND existing_global_product_id IS NOT NULL) OR (duplicate_resolution <> 'link_existing' AND existing_global_product_id IS NULL))");
                table.CheckConstraint("ck_curation_review", "(reviewed_by_actor_id IS NULL AND reviewed_at_utc IS NULL AND review_notes IS NULL AND version = 1) OR (reviewed_by_actor_id IS NOT NULL AND reviewed_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid AND reviewed_at_utc IS NOT NULL AND isfinite(reviewed_at_utc) AND reviewed_at_utc >= created_at_utc AND review_notes IS NOT NULL AND length(btrim(review_notes)) > 0)");
                table.CheckConstraint("ck_curation_state", "status IN ('pending_review','needs_correction','approved','rejected','published') AND version >= 1 AND isfinite(created_at_utc)");
                table.ForeignKey(
                    name: "FK_official_catalog_curations_global_products_existing_global_~",
                    column: x => x.existing_global_product_id,
                    principalTable: "global_products",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_official_catalog_curations_global_products_published_global~",
                    column: x => x.published_global_product_id,
                    principalTable: "global_products",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_official_catalog_curations_official_catalog_import_batches_~",
                    column: x => x.import_batch_id,
                    principalTable: "official_catalog_import_batches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_official_catalog_curations_official_catalog_staging_rows_st~",
                    column: x => x.staging_row_id,
                    principalTable: "official_catalog_staging_rows",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "official_catalog_platform_audits",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: true),
                actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                correlation_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                failure_code = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_official_catalog_platform_audits", x => x.id);
                table.CheckConstraint("ck_curation_audit", "actor_id <> '00000000-0000-0000-0000-000000000000'::uuid AND candidate_id <> '00000000-0000-0000-0000-000000000000'::uuid AND isfinite(at_utc) AND correlation_id ~ '^[0-9a-f]{32}$' AND correlation_id <> repeat('0',32) AND (version IS NULL OR version >= 1) AND action IN ('normalized','save_correction','approve','reject','request_correction','published','prepare_failed','review_failed','publish_failed') AND ((action LIKE '%_failed' AND failure_code IS NOT NULL) OR (action NOT LIKE '%_failed' AND failure_code IS NULL))");
            });

        migrationBuilder.CreateTable(
            name: "official_catalog_publications",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                source_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                official_identity = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                identity_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                curation_id = table.Column<Guid>(type: "uuid", nullable: false),
                staging_row_id = table.Column<Guid>(type: "uuid", nullable: false),
                import_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                global_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                approved_version = table.Column<long>(type: "bigint", nullable: false),
                candidate_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                presentation = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                published_by_actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                published_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_official_catalog_publications", x => x.id);
                table.CheckConstraint("ck_curation_origin", "length(btrim(official_identity)) > 0 AND identity_hash ~ '^[0-9a-f]{64}$' AND candidate_fingerprint ~ '^[0-9a-f]{64}$' AND approved_version >= 2 AND isfinite(published_at_utc) AND published_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid AND length(btrim(presentation)) > 0");
                table.ForeignKey(
                    name: "FK_official_catalog_publications_global_products_global_produc~",
                    column: x => x.global_product_id,
                    principalTable: "global_products",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_official_catalog_publications_official_catalog_curations_cu~",
                    column: x => x.curation_id,
                    principalTable: "official_catalog_curations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_official_catalog_publications_official_catalog_import_batch~",
                    column: x => x.import_batch_id,
                    principalTable: "official_catalog_import_batches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_official_catalog_publications_official_catalog_staging_rows~",
                    column: x => x.staging_row_id,
                    principalTable: "official_catalog_staging_rows",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "official_catalog_revisions",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                curation_id = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false),
                reviewed_version = table.Column<long>(type: "bigint", nullable: false),
                action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                candidate = table.Column<string>(type: "jsonb", nullable: false),
                duplicate_evidence = table.Column<string>(type: "jsonb", nullable: false),
                duplicate_resolution = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                existing_global_product_id = table.Column<Guid>(type: "uuid", nullable: true),
                actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                correlation_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                reason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_official_catalog_revisions", x => x.id);
                table.CheckConstraint("ck_curation_revision", "version >= 1 AND reviewed_version = version - 1 AND action IN ('normalized','save_correction','approve','reject','request_correction','published') AND actor_id <> '00000000-0000-0000-0000-000000000000'::uuid AND isfinite(at_utc) AND length(btrim(reason)) > 0 AND correlation_id ~ '^[0-9a-f]{32}$' AND correlation_id <> repeat('0',32)");
                table.CheckConstraint("ck_curation_revision_data", "jsonb_typeof(candidate) = 'object' AND octet_length(candidate::text) <= 262144 AND jsonb_typeof(duplicate_evidence) = 'array' AND jsonb_array_length(duplicate_evidence) <= 101");
                table.ForeignKey(
                    name: "FK_official_catalog_revisions_global_products_existing_global_~",
                    column: x => x.existing_global_product_id,
                    principalTable: "global_products",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_official_catalog_revisions_official_catalog_curations_curat~",
                    column: x => x.curation_id,
                    principalTable: "official_catalog_curations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_curations_errors",
            table: "official_catalog_curations",
            column: "errors")
            .Annotation("Npgsql:IndexMethod", "gin");

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_curations_existing_global_product_id",
            table: "official_catalog_curations",
            column: "existing_global_product_id");

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_curations_import_batch_id_status_created_a~",
            table: "official_catalog_curations",
            columns: ["import_batch_id", "status", "created_at_utc", "id"]);

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_curations_published_global_product_id",
            table: "official_catalog_curations",
            column: "published_global_product_id");

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_curations_source_code_status_created_at_ut~",
            table: "official_catalog_curations",
            columns: ["source_code", "status", "created_at_utc", "id"]);

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_curations_staging_row_id",
            table: "official_catalog_curations",
            column: "staging_row_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_platform_audits_candidate_id_at_utc_id",
            table: "official_catalog_platform_audits",
            columns: ["candidate_id", "at_utc", "id"]);

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_platform_audits_candidate_id_version",
            table: "official_catalog_platform_audits",
            columns: ["candidate_id", "version"],
            unique: true,
            filter: "failure_code IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_publications_candidate_fingerprint",
            table: "official_catalog_publications",
            column: "candidate_fingerprint");

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_publications_curation_id",
            table: "official_catalog_publications",
            column: "curation_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_publications_global_product_id",
            table: "official_catalog_publications",
            column: "global_product_id");

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_publications_import_batch_id",
            table: "official_catalog_publications",
            column: "import_batch_id");

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_publications_source_code_identity_hash",
            table: "official_catalog_publications",
            columns: ["source_code", "identity_hash"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_publications_source_code_official_identity",
            table: "official_catalog_publications",
            columns: ["source_code", "official_identity"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_publications_staging_row_id",
            table: "official_catalog_publications",
            column: "staging_row_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_revisions_curation_id_version",
            table: "official_catalog_revisions",
            columns: ["curation_id", "version"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_revisions_existing_global_product_id",
            table: "official_catalog_revisions",
            column: "existing_global_product_id");
        migrationBuilder.Sql(OfficialCatalogCurationSql.Up);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(OfficialCatalogCurationSql.Down);
        migrationBuilder.DropTable(
            name: "official_catalog_platform_audits");

        migrationBuilder.DropTable(
            name: "official_catalog_publications");

        migrationBuilder.DropTable(
            name: "official_catalog_revisions");

        migrationBuilder.DropTable(
            name: "official_catalog_curations");
    }
}
