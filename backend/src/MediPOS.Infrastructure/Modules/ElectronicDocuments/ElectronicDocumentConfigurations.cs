using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.ElectronicDocuments;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Domain.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.ElectronicDocuments;

internal static class ElectronicDocumentColumns
{
    public static void Apply(IMutableEntityType type)
    {
        foreach (var p in type.GetProperties())
            p.SetColumnName(string.Concat(p.Name.Select((c, i) => char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString())));
    }
}
internal sealed class ElectronicDocumentSeriesConfiguration : IEntityTypeConfiguration<ElectronicDocumentSeries>
{
    public void Configure(EntityTypeBuilder<ElectronicDocumentSeries> b)
    {
        b.ToTable("electronic_document_series", t =>
        {
            t.HasCheckConstraint("ck_cpe_series", "document_type IN ('receipt','invoice') AND code ~ '^[A-Z][A-Z0-9]{0,11}$' AND profile='preparation_v1' AND next_number BETWEEN 1 AND 100000000 AND length(btrim(issuer_address)) BETWEEN 1 AND 512 AND isfinite(created_at_utc) AND updated_at_utc>=created_at_utc AND isfinite(updated_at_utc)");
        });
        b.HasKey(v => v.Id); b.HasAlternateKey(v => new { v.TenantId, v.Id }); b.Property(v => v.Id).ValueGeneratedNever();
        b.Property(v => v.Code).HasMaxLength(12); b.Property(v => v.DocumentType).HasMaxLength(16); b.Property(v => v.Profile).HasMaxLength(32); b.Property(v => v.IssuerAddress).HasMaxLength(512);
        b.HasOne<Tenant>().WithMany().HasForeignKey(v => v.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LegalEntity>().WithMany().HasForeignKey(v => new { v.TenantId, v.LegalEntityId }).HasPrincipalKey(v => new { v.TenantId, v.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Membership>().WithMany().HasForeignKey(v => new { v.TenantId, v.AuthorizedByMembershipId }).HasPrincipalKey(v => new { v.TenantId, v.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(v => new { v.TenantId, v.LegalEntityId, v.DocumentType, v.Code }).IsUnique().HasDatabaseName("ux_cpe_series");
        ElectronicDocumentColumns.Apply(b.Metadata); b.Property(v => v.Version).IsRowVersion().HasColumnName("xmin");
    }
}
internal sealed class ElectronicDocumentConfiguration : IEntityTypeConfiguration<ElectronicDocument>
{
    public void Configure(EntityTypeBuilder<ElectronicDocument> b)
    {
        b.ToTable("electronic_documents", t =>
        {
            t.HasCheckConstraint("ck_cpe_document", "document_type IN ('receipt','invoice') AND sequential_number BETWEEN 1 AND 99999999 AND document_number=series_code||'-'||sequential_number::text AND request_fingerprint ~ '^[0-9a-f]{64}$' AND currency='PEN' AND total>0 AND total<=99999999999999.9999 AND total NOT IN ('NaN'::numeric,'Infinity'::numeric) AND jsonb_typeof(snapshot_json)='object' AND octet_length(snapshot_json::text)<=1048576");
            t.HasCheckConstraint("ck_cpe_state", "status IN ('pending','reserved','dispatching','provider_received','authority_accepted','rejected','retry_wait','uncertain','manual_review','simulated') AND attempt_count BETWEEN 0 AND 5 AND ((status IN ('reserved','dispatching') AND claim_token IS NOT NULL AND lease_expires_at_utc IS NOT NULL AND attempt_started_at_utc IS NOT NULL) OR (status NOT IN ('reserved','dispatching') AND claim_token IS NULL AND lease_expires_at_utc IS NULL)) AND ((status IN ('pending','retry_wait') AND next_attempt_at_utc IS NOT NULL) OR (status NOT IN ('pending','retry_wait') AND next_attempt_at_utc IS NULL)) AND (status<>'authority_accepted' OR (evidence_sha256 ~ '^[0-9a-f]{64}$' AND evidence_sha256 IS NOT NULL AND provider_reference IS NOT NULL)) AND (status NOT IN ('dispatching','provider_received','authority_accepted','simulated') OR submitted_at_utc IS NOT NULL)");
            t.HasCheckConstraint("ck_cpe_time", "isfinite(created_at_utc) AND isfinite(updated_at_utc) AND updated_at_utc>=created_at_utc AND (submitted_at_utc IS NULL OR (isfinite(submitted_at_utc) AND submitted_at_utc>=created_at_utc)) AND (next_attempt_at_utc IS NULL OR isfinite(next_attempt_at_utc)) AND (lease_expires_at_utc IS NULL OR isfinite(lease_expires_at_utc)) AND (attempt_started_at_utc IS NULL OR (isfinite(attempt_started_at_utc) AND attempt_started_at_utc>=created_at_utc))");
        });
        b.HasKey(v => v.Id); b.HasAlternateKey(v => new { v.TenantId, v.Id }); b.Property(v => v.Id).ValueGeneratedNever(); b.Ignore(v => v.IdempotencyKey);
        b.Property(v => v.DocumentType).HasMaxLength(16); b.Property(v => v.SeriesCode).HasMaxLength(12); b.Property(v => v.DocumentNumber).HasMaxLength(32);
        b.Property(v => v.SnapshotJson).HasColumnType("jsonb"); b.Property(v => v.RequestFingerprint).HasMaxLength(64); b.Property(v => v.Currency).HasMaxLength(3); b.Property(v => v.Total).HasPrecision(18, 4);
        b.Property(v => v.Status).HasMaxLength(32); b.Property(v => v.ProviderStatus).HasMaxLength(32); b.Property(v => v.LastErrorCode).HasMaxLength(64); b.Property(v => v.ProviderReference).HasMaxLength(128); b.Property(v => v.EvidenceSha256).HasMaxLength(64);
        b.HasOne<Tenant>().WithMany().HasForeignKey(v => v.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Branch>().WithMany().HasForeignKey(v => new { v.TenantId, v.BranchId }).HasPrincipalKey(v => new { v.TenantId, v.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LegalEntity>().WithMany().HasForeignKey(v => new { v.TenantId, v.LegalEntityId }).HasPrincipalKey(v => new { v.TenantId, v.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Sale>().WithMany().HasForeignKey(v => new { v.TenantId, v.SaleId }).HasPrincipalKey(v => new { v.TenantId, v.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Membership>().WithMany().HasForeignKey(v => new { v.TenantId, v.SellerMembershipId }).HasPrincipalKey(v => new { v.TenantId, v.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Membership>().WithMany().HasForeignKey(v => new { v.TenantId, v.RequestedByMembershipId }).HasPrincipalKey(v => new { v.TenantId, v.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ElectronicDocumentSeries>().WithMany().HasForeignKey(v => new { v.TenantId, v.SeriesId }).HasPrincipalKey(v => new { v.TenantId, v.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(v => new { v.TenantId, v.LegalEntityId, v.DocumentType, v.SeriesCode, v.SequentialNumber }).IsUnique().HasDatabaseName("ux_cpe_number");
        // An ordinary sale has a single document, including across types; changing it requires a regulated future flow.
        b.HasIndex(v => new { v.TenantId, v.SaleId }).IsUnique().HasDatabaseName("ux_cpe_sale");
        b.HasIndex(v => new { v.TenantId, v.Status, v.NextAttemptAtUtc, v.Id }).HasDatabaseName("ix_cpe_queue_due");
        b.HasIndex(v => new { v.TenantId, v.Status, v.LeaseExpiresAtUtc }); b.HasIndex(v => new { v.TenantId, v.BranchId, v.CreatedAtUtc, v.Id });
        ElectronicDocumentColumns.Apply(b.Metadata); b.Property(v => v.Version).IsRowVersion().HasColumnName("xmin");
    }
}
internal sealed class ElectronicDocumentAttemptConfiguration : IEntityTypeConfiguration<ElectronicDocumentAttempt>
{
    public void Configure(EntityTypeBuilder<ElectronicDocumentAttempt> b)
    {
        b.ToTable("electronic_document_attempts", t =>
        {
            t.HasCheckConstraint("ck_cpe_attempt", "number BETWEEN 1 AND 5 AND phase IN ('started','completed') AND idempotency_key='medipos-cpe-'||replace(document_id::text,'-','') AND isfinite(started_at_utc) AND ((phase='started' AND finished_at_utc IS NULL AND outcome='reserved' AND error_code IS NULL AND provider_reference IS NULL) OR (phase='completed' AND finished_at_utc IS NOT NULL AND isfinite(finished_at_utc) AND finished_at_utc>=started_at_utc AND outcome IN ('provider_received','authority_accepted','rejected','retry_wait','uncertain','manual_review','simulated')))");
        });
        b.HasKey(v => v.Id); b.Property(v => v.Id).ValueGeneratedNever(); b.Property(v => v.Phase).HasMaxLength(16); b.Property(v => v.IdempotencyKey).HasMaxLength(64); b.Property(v => v.Outcome).HasMaxLength(32); b.Property(v => v.ErrorCode).HasMaxLength(64); b.Property(v => v.ProviderReference).HasMaxLength(128);
        b.HasOne<Tenant>().WithMany().HasForeignKey(v => v.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ElectronicDocument>().WithMany().HasForeignKey(v => new { v.TenantId, v.DocumentId }).HasPrincipalKey(v => new { v.TenantId, v.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(v => new { v.TenantId, v.DocumentId, v.Number, v.Phase }).IsUnique();
        ElectronicDocumentColumns.Apply(b.Metadata);
    }
}
