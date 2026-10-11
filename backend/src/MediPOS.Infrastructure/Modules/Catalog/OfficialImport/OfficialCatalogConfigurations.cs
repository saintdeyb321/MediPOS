using MediPOS.Domain.Modules.Catalog.OfficialImport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.Catalog.OfficialImport;

internal sealed class OfficialCatalogBatchConfiguration : IEntityTypeConfiguration<OfficialCatalogImportBatch>
{
    public void Configure(EntityTypeBuilder<OfficialCatalogImportBatch> builder)
    {
        builder.ToTable("official_catalog_import_batches", table =>
        {
            table.HasCheckConstraint("ck_official_batch_source", "source_code ~ '^[a-z][a-z0-9_.-]{0,63}$' AND length(btrim(source_version)) > 0 AND source_checksum_sha256 ~ '^[0-9a-f]{64}$' AND input_format = 'csv_utf8_v1'");
            table.HasCheckConstraint("ck_official_batch_counts", "total_rows >= 0 AND valid_rows >= 0 AND invalid_rows >= 0 AND total_rows = valid_rows + invalid_rows AND duplicate_rows BETWEEN 0 AND total_rows AND attempt_count BETWEEN 0 AND 5");
            table.HasCheckConstraint("ck_official_batch_times", "isfinite(started_at_utc) AND (source_published_at_utc IS NULL OR isfinite(source_published_at_utc)) AND (completed_at_utc IS NULL OR (isfinite(completed_at_utc) AND completed_at_utc >= started_at_utc AND completed_at_utc >= last_attempt_started_at_utc))");
            table.HasCheckConstraint("ck_official_batch_actor", "started_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid AND started_correlation_id ~ '^[0-9a-f]{32}$' AND started_correlation_id <> repeat('0',32)");
            table.HasCheckConstraint("ck_official_batch_state", """
                (status = 'pending' AND attempt_count = 0 AND total_rows = 0 AND processing_lease_id IS NULL AND processing_lease_expires_at_utc IS NULL AND completed_at_utc IS NULL AND last_attempt_by_actor_id IS NULL AND last_attempt_started_at_utc IS NULL AND last_attempt_correlation_id IS NULL AND failure_code IS NULL) OR
                (status IN ('processing','staged','failed') AND attempt_count > 0 AND last_attempt_by_actor_id IS NOT NULL AND last_attempt_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid AND last_attempt_started_at_utc IS NOT NULL AND isfinite(last_attempt_started_at_utc) AND last_attempt_started_at_utc >= started_at_utc AND last_attempt_correlation_id ~ '^[0-9a-f]{32}$' AND last_attempt_correlation_id <> repeat('0',32) AND
                  ((status = 'processing' AND processing_lease_id IS NOT NULL AND processing_lease_id <> '00000000-0000-0000-0000-000000000000'::uuid AND processing_lease_expires_at_utc IS NOT NULL AND isfinite(processing_lease_expires_at_utc) AND processing_lease_expires_at_utc > last_attempt_started_at_utc AND completed_at_utc IS NULL AND failure_code IS NULL) OR
                   (status IN ('staged','failed') AND processing_lease_id IS NULL AND processing_lease_expires_at_utc IS NULL AND completed_at_utc IS NOT NULL AND ((status = 'staged' AND failure_code IS NULL AND total_rows > 0) OR (status = 'failed' AND failure_code IS NOT NULL)))))
                """);
            table.HasCheckConstraint("ck_official_batch_failures", "jsonb_typeof(failure_history) = 'array' AND jsonb_array_length(failure_history) <= attempt_count");
        });
        builder.HasKey(value => value.Id); builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Ignore(value => value.Failures);
        builder.Property(value => value.SourceCode).HasMaxLength(64); builder.Property(value => value.SourceVersion).HasMaxLength(128);
        builder.Property(value => value.SourceInstitution).HasMaxLength(256); builder.Property(value => value.SourceFileName).HasMaxLength(256);
        foreach (var name in new[] { nameof(OfficialCatalogImportBatch.OfficialReference), nameof(OfficialCatalogImportBatch.ReuseAuthorization), nameof(OfficialCatalogImportBatch.Coverage), nameof(OfficialCatalogImportBatch.RecordIdentifierDescription) }) builder.Property<string>(name).HasMaxLength(1024);
        builder.Property(value => value.SourceChecksumSha256).HasMaxLength(64); builder.Property(value => value.Status).HasMaxLength(16);
        builder.Property(value => value.InputFormat).HasMaxLength(32); builder.Property(value => value.FailureCode).HasMaxLength(96);
        builder.Property(value => value.StartedCorrelationId).HasMaxLength(32); builder.Property(value => value.LastAttemptCorrelationId).HasMaxLength(32);
        builder.Property(value => value.FailureHistoryJson).HasColumnType("jsonb");
        OfficialCatalogColumnNames.Apply(builder.Metadata); builder.Property(value => value.FailureHistoryJson).HasColumnName("failure_history");
        // One immutable file per source version. A different checksum is a reviewable conflict, never replacement.
        builder.HasIndex(value => new { value.SourceCode, value.SourceVersion }).IsUnique().HasDatabaseName("ux_official_catalog_source_version");
    }
}
internal sealed class OfficialCatalogStagingConfiguration : IEntityTypeConfiguration<OfficialCatalogStagingRow>
{
    public void Configure(EntityTypeBuilder<OfficialCatalogStagingRow> builder)
    {
        builder.ToTable("official_catalog_staging_rows", table =>
        {
            table.HasCheckConstraint("ck_official_row_identity", "row_number >= 2 AND row_checksum_sha256 ~ '^[0-9a-f]{64}$' AND potential_duplicate_fingerprint ~ '^[0-9a-f]{64}$' AND (source_record_id_fingerprint IS NULL OR source_record_id_fingerprint ~ '^[0-9a-f]{64}$') AND isfinite(created_at_utc)");
            table.HasCheckConstraint("ck_official_row_status", "validation_status IN ('valid','invalid') AND cardinality(validation_error_codes) <= 16 AND array_position(validation_error_codes,NULL) IS NULL AND length(array_to_string(validation_error_codes,',')) <= 1551 AND ((validation_status = 'valid' AND cardinality(validation_error_codes) = 0) OR (validation_status = 'invalid' AND cardinality(validation_error_codes) > 0))");
            table.HasCheckConstraint("ck_official_row_duplicates", "duplicate_signals <@ ARRAY['source_id','exact_row','potential']::text[] AND array_position(duplicate_signals,NULL) IS NULL AND cardinality(duplicate_signals) <= 3 AND ((cardinality(duplicate_signals) = 0 AND duplicate_of_row_number IS NULL) OR (cardinality(duplicate_signals) > 0 AND duplicate_of_row_number >= 2 AND duplicate_of_row_number < row_number))");
            table.HasCheckConstraint("ck_official_row_raw", "jsonb_typeof(raw_fields) = 'object' AND octet_length(raw_fields::text) <= 524288");
        });
        builder.HasKey(value => value.Id); builder.Property(value => value.Id).ValueGeneratedNever();
        foreach (var name in new[] { nameof(OfficialCatalogStagingRow.SourceRecordId), nameof(OfficialCatalogStagingRow.OriginalName), nameof(OfficialCatalogStagingRow.OriginalLaboratory), nameof(OfficialCatalogStagingRow.OriginalRegistration), nameof(OfficialCatalogStagingRow.OriginalPresentation), nameof(OfficialCatalogStagingRow.OriginalIngredientsText), nameof(OfficialCatalogStagingRow.OriginalStrengthText), nameof(OfficialCatalogStagingRow.OriginalDosageFormText), nameof(OfficialCatalogStagingRow.OriginalRouteText) }) builder.Property<string>(name).HasMaxLength(8192);
        builder.Property(value => value.RowChecksumSha256).HasMaxLength(64); builder.Property(value => value.PotentialDuplicateFingerprint).HasMaxLength(64);
        builder.Property(value => value.SourceRecordIdFingerprint).HasMaxLength(64); builder.Property(value => value.ValidationStatus).HasMaxLength(16);
        builder.Property(value => value.RawFieldsJson).HasColumnType("jsonb");
        OfficialCatalogColumnNames.Apply(builder.Metadata); builder.Property(value => value.RawFieldsJson).HasColumnName("raw_fields");
        builder.HasOne<OfficialCatalogImportBatch>().WithMany().HasForeignKey(value => value.ImportBatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.ImportBatchId, value.RowNumber }).IsUnique();
        builder.HasOne<OfficialCatalogStagingRow>().WithMany().HasForeignKey(value => new { value.ImportBatchId, value.DuplicateOfRowNumber })
            .HasPrincipalKey(value => new { value.ImportBatchId, value.RowNumber }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.ImportBatchId, value.ValidationStatus, value.RowNumber });
        builder.HasIndex(value => new { value.ImportBatchId, value.SourceRecordIdFingerprint, value.RowNumber });
        builder.HasIndex(value => new { value.ImportBatchId, value.RowChecksumSha256, value.RowNumber });
        builder.HasIndex(value => new { value.ImportBatchId, value.PotentialDuplicateFingerprint, value.RowNumber });
        builder.HasIndex(value => value.ValidationErrorCodes).HasMethod("gin");
    }
}
internal static class OfficialCatalogColumnNames
{
    internal static void Apply(IMutableEntityType entity)
    {
        foreach (var property in entity.GetProperties()) property.SetColumnName(string.Concat(property.Name.Select((c, i) =>
            i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString())));
    }
}
