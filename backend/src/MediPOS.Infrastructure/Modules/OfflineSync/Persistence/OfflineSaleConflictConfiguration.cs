using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.OfflineSync;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.OfflineSync.Persistence;

internal sealed class OfflineSaleConflictConfiguration : IEntityTypeConfiguration<OfflineSaleConflict>
{
    public void Configure(EntityTypeBuilder<OfflineSaleConflict> builder)
    {
        builder.ToTable("offline_sale_conflicts", table =>
        {
            table.HasCheckConstraint("ck_offline_conflicts_reason", "conflict_type IN ('insufficient_stock','expired_stock','cash_session_unavailable','product_unavailable','unit_or_price_changed','access_requires_review','inconsistent_payload') AND conflict_code = 'sync.sale_' || conflict_type");
            table.HasCheckConstraint("ck_offline_conflicts_time", "isfinite(detected_at_utc)");
            table.HasCheckConstraint("ck_offline_conflicts_identifier", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("ck_offline_conflicts_resolution", """
                (status = 'open' AND resolved_at_utc IS NULL AND resolved_by_membership_id IS NULL AND resolution_code IS NULL AND resolution_note IS NULL) OR
                (status = 'resolved' AND resolved_at_utc IS NOT NULL AND isfinite(resolved_at_utc) AND resolved_at_utc >= detected_at_utc
                  AND resolved_by_membership_id IS NOT NULL AND resolved_by_membership_id <> '00000000-0000-0000-0000-000000000000'::uuid
                  AND resolution_code IS NOT NULL AND resolution_code IN ('cancelled','reviewed_no_posting') AND resolution_note IS NOT NULL
                  AND resolution_note = btrim(resolution_note) AND resolution_note ~ '[^[:space:]]' AND length(resolution_note) <= 512 AND resolution_note !~ '[[:cntrl:]]')
                """);
        });
        builder.HasKey(value => value.Id);
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.TenantId).HasColumnName("tenant_id");
        builder.Property(value => value.SyncEventId).HasColumnName("sync_event_id");
        builder.Property(value => value.DeviceId).HasColumnName("device_id");
        builder.Property(value => value.BranchId).HasColumnName("branch_id");
        builder.Property(value => value.ConflictType).HasColumnName("conflict_type").HasMaxLength(40);
        builder.Property(value => value.ConflictCode).HasColumnName("conflict_code").HasMaxLength(80);
        builder.Property(value => value.DetectedAtUtc).HasColumnName("detected_at_utc");
        builder.Property(value => value.Status).HasColumnName("status").HasMaxLength(16);
        builder.Property(value => value.ResolvedAtUtc).HasColumnName("resolved_at_utc");
        builder.Property(value => value.ResolvedByMembershipId).HasColumnName("resolved_by_membership_id");
        builder.Property(value => value.ResolutionCode).HasColumnName("resolution_code").HasMaxLength(32);
        builder.Property(value => value.ResolutionNote).HasColumnName("resolution_note").HasMaxLength(512);
        builder.HasOne<SyncEvent>().WithMany().HasForeignKey(value => new { value.TenantId, value.SyncEventId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SyncDevice>().WithMany().HasForeignKey(value => new { value.TenantId, value.DeviceId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany().HasForeignKey(value => new { value.TenantId, value.BranchId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Membership>().WithMany().HasForeignKey(value => new { value.TenantId, value.ResolvedByMembershipId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.SyncEventId }).IsUnique();
        builder.HasIndex(value => new { value.TenantId, value.BranchId, value.Status, value.DetectedAtUtc, value.Id });
    }
}
