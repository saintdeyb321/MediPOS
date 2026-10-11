using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.Catalog.OfficialImport.Migrations;

/// <inheritdoc />
public partial class AddOfficialCatalogStaging : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "official_catalog_import_batches",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                source_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                source_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                source_institution = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                official_reference = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                reuse_authorization = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                coverage = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                record_identifier_description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                source_file_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                source_checksum_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                input_format = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                source_published_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                total_rows = table.Column<int>(type: "integer", nullable: false),
                valid_rows = table.Column<int>(type: "integer", nullable: false),
                invalid_rows = table.Column<int>(type: "integer", nullable: false),
                duplicate_rows = table.Column<int>(type: "integer", nullable: false),
                started_by_actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                started_correlation_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                failure_code = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                failure_history = table.Column<string>(type: "jsonb", nullable: false),
                attempt_count = table.Column<int>(type: "integer", nullable: false),
                processing_lease_id = table.Column<Guid>(type: "uuid", nullable: true),
                processing_lease_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_attempt_by_actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                last_attempt_correlation_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                last_attempt_started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_official_catalog_import_batches", x => x.id);
                table.CheckConstraint("ck_official_batch_actor", "started_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid AND started_correlation_id ~ '^[0-9a-f]{32}$' AND started_correlation_id <> repeat('0',32)");
                table.CheckConstraint("ck_official_batch_counts", "total_rows >= 0 AND valid_rows >= 0 AND invalid_rows >= 0 AND total_rows = valid_rows + invalid_rows AND duplicate_rows BETWEEN 0 AND total_rows AND attempt_count BETWEEN 0 AND 5");
                table.CheckConstraint("ck_official_batch_failures", "jsonb_typeof(failure_history) = 'array' AND jsonb_array_length(failure_history) <= attempt_count");
                table.CheckConstraint("ck_official_batch_source", "source_code ~ '^[a-z][a-z0-9_.-]{0,63}$' AND length(btrim(source_version)) > 0 AND source_checksum_sha256 ~ '^[0-9a-f]{64}$' AND input_format = 'csv_utf8_v1'");
                table.CheckConstraint("ck_official_batch_state", "(status = 'pending' AND attempt_count = 0 AND total_rows = 0 AND processing_lease_id IS NULL AND processing_lease_expires_at_utc IS NULL AND completed_at_utc IS NULL AND last_attempt_by_actor_id IS NULL AND last_attempt_started_at_utc IS NULL AND last_attempt_correlation_id IS NULL AND failure_code IS NULL) OR\n(status IN ('processing','staged','failed') AND attempt_count > 0 AND last_attempt_by_actor_id IS NOT NULL AND last_attempt_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid AND last_attempt_started_at_utc IS NOT NULL AND isfinite(last_attempt_started_at_utc) AND last_attempt_started_at_utc >= started_at_utc AND last_attempt_correlation_id ~ '^[0-9a-f]{32}$' AND last_attempt_correlation_id <> repeat('0',32) AND\n  ((status = 'processing' AND processing_lease_id IS NOT NULL AND processing_lease_id <> '00000000-0000-0000-0000-000000000000'::uuid AND processing_lease_expires_at_utc IS NOT NULL AND isfinite(processing_lease_expires_at_utc) AND processing_lease_expires_at_utc > last_attempt_started_at_utc AND completed_at_utc IS NULL AND failure_code IS NULL) OR\n   (status IN ('staged','failed') AND processing_lease_id IS NULL AND processing_lease_expires_at_utc IS NULL AND completed_at_utc IS NOT NULL AND ((status = 'staged' AND failure_code IS NULL AND total_rows > 0) OR (status = 'failed' AND failure_code IS NOT NULL)))))");
                table.CheckConstraint("ck_official_batch_times", "isfinite(started_at_utc) AND (source_published_at_utc IS NULL OR isfinite(source_published_at_utc)) AND (completed_at_utc IS NULL OR (isfinite(completed_at_utc) AND completed_at_utc >= started_at_utc AND completed_at_utc >= last_attempt_started_at_utc))");
            });

        migrationBuilder.CreateTable(
            name: "official_catalog_staging_rows",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                import_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                row_number = table.Column<int>(type: "integer", nullable: false),
                source_record_id = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                source_record_id_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                original_name = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                original_laboratory = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                original_registration = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                original_presentation = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                original_ingredients_text = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                original_strength_text = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                original_dosage_form_text = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                original_route_text = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                raw_fields = table.Column<string>(type: "jsonb", nullable: false),
                row_checksum_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                potential_duplicate_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                validation_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                validation_error_codes = table.Column<string[]>(type: "text[]", nullable: false),
                duplicate_signals = table.Column<string[]>(type: "text[]", nullable: false),
                duplicate_of_row_number = table.Column<int>(type: "integer", nullable: true),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_official_catalog_staging_rows", x => x.id);
                table.UniqueConstraint("AK_official_catalog_staging_rows_import_batch_id_row_number", x => new { x.import_batch_id, x.row_number });
                table.CheckConstraint("ck_official_row_duplicates", "duplicate_signals <@ ARRAY['source_id','exact_row','potential']::text[] AND array_position(duplicate_signals,NULL) IS NULL AND cardinality(duplicate_signals) <= 3 AND ((cardinality(duplicate_signals) = 0 AND duplicate_of_row_number IS NULL) OR (cardinality(duplicate_signals) > 0 AND duplicate_of_row_number >= 2 AND duplicate_of_row_number < row_number))");
                table.CheckConstraint("ck_official_row_identity", "row_number >= 2 AND row_checksum_sha256 ~ '^[0-9a-f]{64}$' AND potential_duplicate_fingerprint ~ '^[0-9a-f]{64}$' AND (source_record_id_fingerprint IS NULL OR source_record_id_fingerprint ~ '^[0-9a-f]{64}$') AND isfinite(created_at_utc)");
                table.CheckConstraint("ck_official_row_raw", "jsonb_typeof(raw_fields) = 'object' AND octet_length(raw_fields::text) <= 524288");
                table.CheckConstraint("ck_official_row_status", "validation_status IN ('valid','invalid') AND cardinality(validation_error_codes) <= 16 AND array_position(validation_error_codes,NULL) IS NULL AND length(array_to_string(validation_error_codes,',')) <= 1551 AND ((validation_status = 'valid' AND cardinality(validation_error_codes) = 0) OR (validation_status = 'invalid' AND cardinality(validation_error_codes) > 0))");
                table.ForeignKey(
                    name: "FK_official_catalog_staging_rows_official_catalog_import_batch~",
                    column: x => x.import_batch_id,
                    principalTable: "official_catalog_import_batches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_official_catalog_staging_rows_official_catalog_staging_rows~",
                    columns: x => new { x.import_batch_id, x.duplicate_of_row_number },
                    principalTable: "official_catalog_staging_rows",
                    principalColumns: ["import_batch_id", "row_number"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ux_official_catalog_source_version",
            table: "official_catalog_import_batches",
            columns: ["source_code", "source_version"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_staging_rows_import_batch_id_duplicate_of_~",
            table: "official_catalog_staging_rows",
            columns: ["import_batch_id", "duplicate_of_row_number"]);

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_staging_rows_import_batch_id_potential_dup~",
            table: "official_catalog_staging_rows",
            columns: ["import_batch_id", "potential_duplicate_fingerprint", "row_number"]);

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_staging_rows_import_batch_id_row_checksum_~",
            table: "official_catalog_staging_rows",
            columns: ["import_batch_id", "row_checksum_sha256", "row_number"]);

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_staging_rows_import_batch_id_row_number",
            table: "official_catalog_staging_rows",
            columns: ["import_batch_id", "row_number"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_staging_rows_import_batch_id_source_record~",
            table: "official_catalog_staging_rows",
            columns: ["import_batch_id", "source_record_id_fingerprint", "row_number"]);

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_staging_rows_import_batch_id_validation_st~",
            table: "official_catalog_staging_rows",
            columns: ["import_batch_id", "validation_status", "row_number"]);

        migrationBuilder.CreateIndex(
            name: "IX_official_catalog_staging_rows_validation_error_codes",
            table: "official_catalog_staging_rows",
            column: "validation_error_codes")
            .Annotation("Npgsql:IndexMethod", "gin");
        migrationBuilder.Sql(OfficialCatalogStagingSql.Up);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(OfficialCatalogStagingSql.Down);
        migrationBuilder.DropTable(
            name: "official_catalog_staging_rows");

        migrationBuilder.DropTable(
            name: "official_catalog_import_batches");
    }
}
