using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.OfflineSync;
using MediPOS.Domain.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.OfflineSync.Persistence;

internal sealed class SyncDeviceConfiguration : IEntityTypeConfiguration<SyncDevice>
{
    public void Configure(EntityTypeBuilder<SyncDevice> builder)
    {
        builder.ToTable("sync_devices", table =>
        {
            table.HasCheckConstraint("ck_sync_devices_ids", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("ck_sync_devices_label", "device_label ~ '[^[:space:]]' AND device_label !~ '[[:cntrl:]]'");
            table.HasCheckConstraint("ck_sync_devices_status", "(status = 'active' AND revoked_at_utc IS NULL AND revoked_by_membership_id IS NULL) OR (status = 'revoked' AND revoked_at_utc IS NOT NULL AND revoked_at_utc >= registered_at_utc AND revoked_by_membership_id IS NOT NULL)");
            table.HasCheckConstraint("ck_sync_devices_time", "isfinite(registered_at_utc) AND (last_seen_at_utc IS NULL OR (isfinite(last_seen_at_utc) AND last_seen_at_utc >= registered_at_utc)) AND (revoked_at_utc IS NULL OR isfinite(revoked_at_utc))");
        });
        builder.HasKey(value => value.Id);
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.TenantId).HasColumnName("tenant_id");
        builder.Property(value => value.RegisteredByMembershipId).HasColumnName("registered_by_membership_id");
        builder.Property(value => value.BranchId).HasColumnName("branch_id");
        builder.Property(value => value.DeviceLabel).HasColumnName("device_label").HasMaxLength(SyncDevice.MaximumLabelLength);
        builder.Property(value => value.Status).HasColumnName("status").HasMaxLength(16);
        builder.Property(value => value.RegisteredAtUtc).HasColumnName("registered_at_utc");
        builder.Property(value => value.LastSeenAtUtc).HasColumnName("last_seen_at_utc");
        builder.Property(value => value.RevokedAtUtc).HasColumnName("revoked_at_utc");
        builder.Property(value => value.RevokedByMembershipId).HasColumnName("revoked_by_membership_id");
        builder.HasOne<Tenant>().WithMany().HasForeignKey(value => value.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany().HasForeignKey(value => new { value.TenantId, value.BranchId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Membership>().WithMany().HasForeignKey(value => new { value.TenantId, value.RegisteredByMembershipId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Membership>().WithMany().HasForeignKey(value => new { value.TenantId, value.RevokedByMembershipId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.BranchId, value.Status });
        builder.HasIndex(value => new { value.TenantId, value.RegisteredByMembershipId, value.RegisteredAtUtc });
    }
}

internal sealed class SyncEventConfiguration : IEntityTypeConfiguration<SyncEvent>
{
    public void Configure(EntityTypeBuilder<SyncEvent> builder)
    {
        builder.ToTable("sync_events", table =>
        {
            table.HasCheckConstraint("ck_sync_events_ids", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND event_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("ck_sync_events_version", "event_type = 'offline_sale.v1' AND schema_version = 1");
            // Canonical JSON text preserves exact decimal spelling/hash across persistence (jsonb rewrites numbers).
            table.HasCheckConstraint("ck_sync_events_payload", "octet_length(payload) BETWEEN 2 AND 65536 AND jsonb_typeof(payload::jsonb) = 'object' AND payload::jsonb <> '{}'::jsonb");
            table.HasCheckConstraint("ck_sync_events_hash", "payload_hash ~ '^[0-9a-f]{64}$' AND payload_hash = encode(sha256(convert_to(payload, 'UTF8')), 'hex')");
            table.HasCheckConstraint("ck_sync_events_time", "isfinite(client_occurred_at_utc) AND isfinite(received_at_utc) AND isfinite(client_local_date) AND client_local_date <> '0001-01-01'::date AND length(client_time_zone_id) BETWEEN 1 AND 100 AND (processed_at_utc IS NULL OR (isfinite(processed_at_utc) AND processed_at_utc >= received_at_utc))");
            table.HasCheckConstraint("ck_sync_events_result", "(processing_status = 'pending' AND processed_at_utc IS NULL AND result_code IS NULL AND result_reference_id IS NULL) OR (processing_status IN ('accepted', 'conflict', 'rejected') AND processed_at_utc IS NOT NULL AND result_code IS NOT NULL AND result_code ~ '^[a-z0-9_.]{1,80}$' AND (result_reference_id IS NULL OR result_reference_id <> '00000000-0000-0000-0000-000000000000'::uuid))");
        });
        builder.HasKey(value => value.Id);
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.TenantId).HasColumnName("tenant_id");
        builder.Property(value => value.DeviceId).HasColumnName("device_id");
        builder.Property(value => value.EventId).HasColumnName("event_id");
        builder.Property(value => value.EventType).HasColumnName("event_type").HasMaxLength(40);
        builder.Property(value => value.SchemaVersion).HasColumnName("schema_version");
        builder.Property(value => value.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64);
        builder.Property(value => value.Payload).HasColumnName("payload").HasColumnType("text");
        builder.Property(value => value.ClientOccurredAtUtc).HasColumnName("client_occurred_at_utc");
        builder.Property(value => value.ClientLocalDate).HasColumnName("client_local_date").HasColumnType("date");
        builder.Property(value => value.ClientTimeZoneId).HasColumnName("client_time_zone_id").HasMaxLength(100);
        builder.Property(value => value.ReceivedAtUtc).HasColumnName("received_at_utc");
        builder.Property(value => value.ProcessingStatus).HasColumnName("processing_status").HasMaxLength(16);
        builder.Property(value => value.ProcessedAtUtc).HasColumnName("processed_at_utc");
        builder.Property(value => value.ResultCode).HasColumnName("result_code").HasMaxLength(80);
        builder.Property(value => value.ResultReferenceId).HasColumnName("result_reference_id");
        builder.HasOne<SyncDevice>().WithMany().HasForeignKey(value => new { value.TenantId, value.DeviceId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.EventId }).IsUnique().HasDatabaseName("ux_sync_events_tenant_event");
        builder.HasIndex(value => new { value.TenantId, value.DeviceId, value.ReceivedAtUtc });
        builder.HasIndex(value => new { value.TenantId, value.ProcessingStatus, value.ReceivedAtUtc, value.Id });
    }
}
