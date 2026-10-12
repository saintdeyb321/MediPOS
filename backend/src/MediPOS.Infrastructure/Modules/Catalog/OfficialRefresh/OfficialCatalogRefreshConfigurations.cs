using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Catalog.OfficialCuration;
using MediPOS.Domain.Modules.Catalog.OfficialImport;
using MediPOS.Domain.Modules.Catalog.OfficialRefresh;
using MediPOS.Infrastructure.Modules.Catalog.OfficialImport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.Catalog.OfficialRefresh;

internal sealed class OfficialCatalogRefreshConfiguration : IEntityTypeConfiguration<OfficialCatalogRefresh>
{
    public void Configure(EntityTypeBuilder<OfficialCatalogRefresh> builder)
    {
        builder.ToTable("official_catalog_refreshes", table =>
        {
            table.HasCheckConstraint("ck_refresh_identity", "length(btrim(source_code)) > 0 AND length(btrim(source_version)) > 0 AND length(btrim(official_identity)) > 0 AND source_checksum_sha256 ~ '^[0-9a-f]{64}$' AND product_fingerprint ~ '^[0-9a-f]{64}$' AND ((curation_id IS NULL AND staging_row_id IS NULL AND row_checksum_sha256 IS NULL AND proposed_snapshot IS NULL AND change_classification = 'missing_from_source') OR (curation_id IS NOT NULL AND staging_row_id IS NOT NULL AND row_checksum_sha256 ~ '^[0-9a-f]{64}$' AND proposed_snapshot IS NOT NULL))");
            table.HasCheckConstraint("ck_refresh_data", "jsonb_typeof(previous_snapshot) = 'object' AND octet_length(previous_snapshot::text) <= 524288 AND (proposed_snapshot IS NULL OR (jsonb_typeof(proposed_snapshot) = 'object' AND octet_length(proposed_snapshot::text) <= 524288)) AND jsonb_typeof(changed_fields) = 'array' AND jsonb_array_length(changed_fields) <= 16");
            table.HasCheckConstraint("ck_refresh_status", "status IN ('pending_review','requires_review','approved','rejected','kept_previous','referred','applied') AND change_classification IN ('unchanged','metadata_changed','clinically_significant_change','identity_conflict','missing_from_source','requires_review') AND version >= 1 AND base_product_version >= 0 AND created_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid AND isfinite(created_at_utc)");
            table.HasCheckConstraint("ck_refresh_review", "(version = 1 AND reviewed_by_actor_id IS NULL AND reviewed_at_utc IS NULL AND reason IS NULL) OR (version > 1 AND reviewed_by_actor_id IS NOT NULL AND reviewed_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid AND reviewed_at_utc IS NOT NULL AND isfinite(reviewed_at_utc) AND reviewed_at_utc >= created_at_utc AND reason IS NOT NULL AND length(btrim(reason)) > 0)");
            table.HasCheckConstraint("ck_refresh_approval", "(status = 'approved' AND approved_version = version AND approved_version IS NOT NULL) OR (status = 'applied' AND approved_version = version - 1 AND approved_version IS NOT NULL) OR (status NOT IN ('approved','applied') AND approved_version IS NULL)");
            table.HasCheckConstraint("ck_refresh_application", "(status = 'applied' AND applied_product_version = base_product_version + 1 AND applied_product_version IS NOT NULL AND applied_by_actor_id IS NOT NULL AND applied_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid AND applied_at_utc IS NOT NULL AND isfinite(applied_at_utc) AND applied_at_utc >= reviewed_at_utc AND change_classification IN ('unchanged','metadata_changed')) OR (status <> 'applied' AND applied_product_version IS NULL AND applied_by_actor_id IS NULL AND applied_at_utc IS NULL)");
            table.HasCheckConstraint("ck_refresh_referral", "(status = 'referred' AND referred_official_identity IS NOT NULL AND length(btrim(referred_official_identity)) > 0 AND referred_official_identity <> official_identity AND change_classification IN ('clinically_significant_change','identity_conflict')) OR (status <> 'referred' AND referred_official_identity IS NULL)");
        });
        builder.HasKey(value => value.Id); builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.SourceCode).HasMaxLength(64); builder.Property(value => value.SourceVersion).HasMaxLength(128);
        builder.Property(value => value.OfficialIdentity).HasMaxLength(256); builder.Property(value => value.ReferredOfficialIdentity).HasMaxLength(256);
        builder.Property(value => value.SourceChecksumSha256).HasMaxLength(64); builder.Property(value => value.RowChecksumSha256).HasMaxLength(64);
        builder.Property(value => value.ProductFingerprint).HasMaxLength(64); builder.Property(value => value.Status).HasMaxLength(32);
        builder.Property(value => value.ChangeClassification).HasMaxLength(32); builder.Property(value => value.Reason).HasMaxLength(2048);
        builder.Property(value => value.Version).IsConcurrencyToken();
        builder.Property(value => value.PreviousSnapshotJson).HasColumnType("jsonb"); builder.Property(value => value.ProposedSnapshotJson).HasColumnType("jsonb");
        builder.Property(value => value.ChangedFieldsJson).HasColumnType("jsonb"); builder.Ignore(value => value.Previous); builder.Ignore(value => value.Proposed);
        OfficialCatalogColumnNames.Apply(builder.Metadata);
        builder.Property(value => value.PreviousSnapshotJson).HasColumnName("previous_snapshot"); builder.Property(value => value.ProposedSnapshotJson).HasColumnName("proposed_snapshot");
        builder.Property(value => value.ChangedFieldsJson).HasColumnName("changed_fields");
        builder.HasOne<OfficialCatalogPublication>().WithMany().HasForeignKey(value => value.PublicationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GlobalProduct>().WithMany().HasForeignKey(value => value.GlobalProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OfficialCatalogImportBatch>().WithMany().HasForeignKey(value => value.ImportBatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OfficialCatalogStagingRow>().WithMany().HasForeignKey(value => value.StagingRowId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OfficialCatalogCuration>().WithMany().HasForeignKey(value => value.CurationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.SourceCode, value.SourceVersion, value.OfficialIdentity }).IsUnique();
        builder.HasIndex(value => value.CurationId).IsUnique().HasFilter("curation_id IS NOT NULL");
        builder.HasIndex(value => new { value.GlobalProductId, value.AppliedProductVersion }).IsUnique().HasFilter("applied_product_version IS NOT NULL");
        builder.HasIndex(value => new { value.SourceCode, value.SourceVersion, value.Status, value.CreatedAtUtc, value.Id });
        builder.HasIndex(value => new { value.ChangeClassification, value.CreatedAtUtc, value.Id });
        builder.HasIndex(value => new { value.GlobalProductId, value.CreatedAtUtc, value.Id });
    }
}
internal sealed class OfficialCatalogVersionHistoryConfiguration : IEntityTypeConfiguration<OfficialCatalogVersionHistory>
{
    public void Configure(EntityTypeBuilder<OfficialCatalogVersionHistory> builder)
    {
        builder.ToTable("official_catalog_version_history", table =>
        {
            table.HasCheckConstraint("ck_refresh_history_event", "version >= 1 AND base_product_version >= 0 AND actor_id <> '00000000-0000-0000-0000-000000000000'::uuid AND isfinite(at_utc) AND length(btrim(reason)) > 0 AND correlation_id ~ '^[0-9a-f]{32}$' AND correlation_id <> repeat('0',32) AND action IN ('prepared','save_correction','approve','reject','request_review','keep_previous','refer_new_identity','applied')");
            table.HasCheckConstraint("ck_refresh_history_data", "jsonb_typeof(previous_snapshot) = 'object' AND octet_length(previous_snapshot::text) <= 524288 AND (proposed_snapshot IS NULL OR (jsonb_typeof(proposed_snapshot) = 'object' AND octet_length(proposed_snapshot::text) <= 524288)) AND (approved_snapshot IS NULL OR (jsonb_typeof(approved_snapshot) = 'object' AND octet_length(approved_snapshot::text) <= 524288)) AND jsonb_typeof(changed_fields) = 'array' AND jsonb_array_length(changed_fields) <= 16");
        });
        builder.HasKey(value => value.Id); builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.SourceCode).HasMaxLength(64); builder.Property(value => value.SourceVersion).HasMaxLength(128);
        builder.Property(value => value.OfficialIdentity).HasMaxLength(256); builder.Property(value => value.ReferredOfficialIdentity).HasMaxLength(256);
        builder.Property(value => value.Status).HasMaxLength(32); builder.Property(value => value.ChangeClassification).HasMaxLength(32);
        builder.Property(value => value.Action).HasMaxLength(32); builder.Property(value => value.Reason).HasMaxLength(2048); builder.Property(value => value.CorrelationId).HasMaxLength(32);
        builder.Property(value => value.PreviousSnapshotJson).HasColumnType("jsonb"); builder.Property(value => value.ApprovedSnapshotJson).HasColumnType("jsonb");
        builder.Property(value => value.ProposedSnapshotJson).HasColumnType("jsonb");
        builder.Property(value => value.ChangedFieldsJson).HasColumnType("jsonb"); OfficialCatalogColumnNames.Apply(builder.Metadata);
        builder.Property(value => value.PreviousSnapshotJson).HasColumnName("previous_snapshot"); builder.Property(value => value.ApprovedSnapshotJson).HasColumnName("approved_snapshot");
        builder.Property(value => value.ProposedSnapshotJson).HasColumnName("proposed_snapshot");
        builder.Property(value => value.ChangedFieldsJson).HasColumnName("changed_fields");
        builder.HasOne<OfficialCatalogRefresh>().WithMany().HasForeignKey(value => value.RefreshId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GlobalProduct>().WithMany().HasForeignKey(value => value.GlobalProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OfficialCatalogImportBatch>().WithMany().HasForeignKey(value => value.ImportBatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OfficialCatalogStagingRow>().WithMany().HasForeignKey(value => value.StagingRowId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OfficialCatalogCuration>().WithMany().HasForeignKey(value => value.CurationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.RefreshId, value.Version }).IsUnique();
        builder.HasIndex(value => new { value.GlobalProductId, value.AtUtc, value.Id });
        builder.HasIndex(value => new { value.SourceCode, value.SourceVersion, value.AtUtc, value.Id });
    }
}
