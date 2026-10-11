using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Catalog.OfficialCuration;
using MediPOS.Domain.Modules.Catalog.OfficialImport;
using MediPOS.Infrastructure.Modules.Catalog.OfficialImport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.Catalog.OfficialCuration;

internal sealed class OfficialCatalogCurationConfiguration : IEntityTypeConfiguration<OfficialCatalogCuration>
{
    public void Configure(EntityTypeBuilder<OfficialCatalogCuration> builder)
    {
        builder.ToTable("official_catalog_curations", table =>
        {
            table.HasCheckConstraint("ck_curation_state", "status IN ('pending_review','needs_correction','approved','rejected','published') AND version >= 1 AND isfinite(created_at_utc)");
            table.HasCheckConstraint("ck_curation_json", "jsonb_typeof(normalized_candidate) = 'object' AND octet_length(normalized_candidate::text) <= 262144 AND jsonb_typeof(duplicate_evidence) = 'array' AND jsonb_array_length(duplicate_evidence) <= 101");
            table.HasCheckConstraint("ck_curation_review", "(reviewed_by_actor_id IS NULL AND reviewed_at_utc IS NULL AND review_notes IS NULL AND version = 1) OR (reviewed_by_actor_id IS NOT NULL AND reviewed_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid AND reviewed_at_utc IS NOT NULL AND isfinite(reviewed_at_utc) AND reviewed_at_utc >= created_at_utc AND review_notes IS NOT NULL AND length(btrim(review_notes)) > 0)");
            table.HasCheckConstraint("ck_curation_approval", "(status = 'approved' AND approved_version IS NOT NULL AND approved_version = version AND reviewed_by_actor_id IS NOT NULL AND cardinality(errors) = 0) OR (status = 'published' AND approved_version IS NOT NULL AND approved_version = version - 1 AND reviewed_by_actor_id IS NOT NULL AND cardinality(errors) = 0) OR (status NOT IN ('approved','published') AND approved_version IS NULL)");
            table.HasCheckConstraint("ck_curation_publication", "(status = 'published' AND published_global_product_id IS NOT NULL AND published_at_utc IS NOT NULL AND isfinite(published_at_utc) AND published_at_utc >= reviewed_at_utc) OR (status <> 'published' AND published_global_product_id IS NULL AND published_at_utc IS NULL)");
            table.HasCheckConstraint("ck_curation_resolution", "duplicate_resolution IN ('unresolved','distinct','link_existing') AND ((duplicate_resolution = 'link_existing' AND existing_global_product_id IS NOT NULL) OR (duplicate_resolution <> 'link_existing' AND existing_global_product_id IS NULL))");
            table.HasCheckConstraint("ck_curation_errors", "cardinality(errors) <= 32 AND array_position(errors,NULL) IS NULL AND length(array_to_string(errors,',')) <= 3072");
        });
        builder.HasKey(value => value.Id); builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.SourceCode).HasMaxLength(64); builder.Property(value => value.SourceRecordId).HasMaxLength(8192);
        builder.Property(value => value.Status).HasMaxLength(32); builder.Property(value => value.ReviewNotes).HasMaxLength(2048);
        builder.Property(value => value.DuplicateResolution).HasMaxLength(32); builder.Property(value => value.Version).IsConcurrencyToken();
        builder.Property(value => value.NormalizedCandidateJson).HasColumnType("jsonb"); builder.Property(value => value.DuplicateEvidenceJson).HasColumnType("jsonb");
        builder.Ignore(value => value.Proposal); builder.Ignore(value => value.DuplicateEvidence);
        OfficialCatalogColumnNames.Apply(builder.Metadata);
        builder.Property(value => value.NormalizedCandidateJson).HasColumnName("normalized_candidate"); builder.Property(value => value.DuplicateEvidenceJson).HasColumnName("duplicate_evidence");
        builder.HasOne<OfficialCatalogImportBatch>().WithMany().HasForeignKey(value => value.ImportBatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OfficialCatalogStagingRow>().WithMany().HasForeignKey(value => value.StagingRowId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GlobalProduct>().WithMany().HasForeignKey(value => value.PublishedGlobalProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GlobalProduct>().WithMany().HasForeignKey(value => value.ExistingGlobalProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => value.StagingRowId).IsUnique(); builder.HasIndex(value => new { value.ImportBatchId, value.Status, value.CreatedAtUtc, value.Id });
        builder.HasIndex(value => new { value.SourceCode, value.Status, value.CreatedAtUtc, value.Id }); builder.HasIndex(value => value.Errors).HasMethod("gin");
    }
}
internal sealed class OfficialCatalogRevisionConfiguration : IEntityTypeConfiguration<OfficialCatalogRevision>
{
    public void Configure(EntityTypeBuilder<OfficialCatalogRevision> builder)
    {
        builder.ToTable("official_catalog_revisions", table =>
        {
            table.HasCheckConstraint("ck_curation_revision", "version >= 1 AND reviewed_version = version - 1 AND action IN ('normalized','save_correction','approve','reject','request_correction','published') AND actor_id <> '00000000-0000-0000-0000-000000000000'::uuid AND isfinite(at_utc) AND length(btrim(reason)) > 0 AND correlation_id ~ '^[0-9a-f]{32}$' AND correlation_id <> repeat('0',32)");
            table.HasCheckConstraint("ck_curation_revision_data", "jsonb_typeof(candidate) = 'object' AND octet_length(candidate::text) <= 262144 AND jsonb_typeof(duplicate_evidence) = 'array' AND jsonb_array_length(duplicate_evidence) <= 101");
        });
        builder.HasKey(value => value.Id); builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.Action).HasMaxLength(32); builder.Property(value => value.Status).HasMaxLength(32);
        builder.Property(value => value.DuplicateResolution).HasMaxLength(32); builder.Property(value => value.CorrelationId).HasMaxLength(32); builder.Property(value => value.Reason).HasMaxLength(2048);
        builder.Property(value => value.CandidateJson).HasColumnType("jsonb"); builder.Property(value => value.DuplicateEvidenceJson).HasColumnType("jsonb");
        OfficialCatalogColumnNames.Apply(builder.Metadata); builder.Property(value => value.CandidateJson).HasColumnName("candidate"); builder.Property(value => value.DuplicateEvidenceJson).HasColumnName("duplicate_evidence");
        builder.HasOne<OfficialCatalogCuration>().WithMany().HasForeignKey(value => value.CurationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GlobalProduct>().WithMany().HasForeignKey(value => value.ExistingGlobalProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.CurationId, value.Version }).IsUnique();
    }
}
internal sealed class OfficialCatalogPublicationConfiguration : IEntityTypeConfiguration<OfficialCatalogPublication>
{
    public void Configure(EntityTypeBuilder<OfficialCatalogPublication> builder)
    {
        builder.ToTable("official_catalog_publications", table =>
        {
            table.HasCheckConstraint("ck_curation_origin", "length(btrim(official_identity)) > 0 AND identity_hash ~ '^[0-9a-f]{64}$' AND candidate_fingerprint ~ '^[0-9a-f]{64}$' AND approved_version >= 2 AND isfinite(published_at_utc) AND published_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid AND length(btrim(presentation)) > 0");
        });
        builder.HasKey(value => value.Id); builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.SourceCode).HasMaxLength(64); builder.Property(value => value.OfficialIdentity).HasMaxLength(256);
        builder.Property(value => value.IdentityHash).HasMaxLength(64); builder.Property(value => value.CandidateFingerprint).HasMaxLength(64); builder.Property(value => value.Presentation).HasMaxLength(1024);
        OfficialCatalogColumnNames.Apply(builder.Metadata);
        builder.HasOne<OfficialCatalogCuration>().WithMany().HasForeignKey(value => value.CurationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OfficialCatalogStagingRow>().WithMany().HasForeignKey(value => value.StagingRowId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OfficialCatalogImportBatch>().WithMany().HasForeignKey(value => value.ImportBatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GlobalProduct>().WithMany().HasForeignKey(value => value.GlobalProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => value.CurationId).IsUnique(); builder.HasIndex(value => value.StagingRowId).IsUnique();
        builder.HasIndex(value => new { value.SourceCode, value.OfficialIdentity }).IsUnique();
        builder.HasIndex(value => new { value.SourceCode, value.IdentityHash }).IsUnique(); builder.HasIndex(value => value.CandidateFingerprint);
    }
}
internal sealed class OfficialCatalogPlatformAuditConfiguration : IEntityTypeConfiguration<OfficialCatalogPlatformAudit>
{
    public void Configure(EntityTypeBuilder<OfficialCatalogPlatformAudit> builder)
    {
        builder.ToTable("official_catalog_platform_audits", table => table.HasCheckConstraint("ck_curation_audit",
            "actor_id <> '00000000-0000-0000-0000-000000000000'::uuid AND candidate_id <> '00000000-0000-0000-0000-000000000000'::uuid AND isfinite(at_utc) AND correlation_id ~ '^[0-9a-f]{32}$' AND correlation_id <> repeat('0',32) AND (version IS NULL OR version >= 1) AND action IN ('normalized','save_correction','approve','reject','request_correction','published','prepare_failed','review_failed','publish_failed') AND ((action LIKE '%_failed' AND failure_code IS NOT NULL) OR (action NOT LIKE '%_failed' AND failure_code IS NULL))"));
        builder.HasKey(value => value.Id); builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.CorrelationId).HasMaxLength(32); builder.Property(value => value.Action).HasMaxLength(32); builder.Property(value => value.FailureCode).HasMaxLength(96);
        OfficialCatalogColumnNames.Apply(builder.Metadata); builder.HasIndex(value => new { value.CandidateId, value.AtUtc, value.Id });
        builder.HasIndex(value => new { value.CandidateId, value.Version }).IsUnique().HasFilter("failure_code IS NULL");
    }
}
