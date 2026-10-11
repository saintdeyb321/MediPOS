using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.Notifications.Persistence;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications", table =>
        {
            table.HasCheckConstraint("ck_notifications_type", "type = 'lot_expiring_30d'");
            table.HasCheckConstraint("ck_notifications_text", "title ~ '[^[:space:]]' AND message ~ '[^[:space:]]'");
        });
        builder.HasKey(value => value.Id);
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.TenantId).HasColumnName("tenant_id");
        builder.Property(value => value.BranchId).HasColumnName("branch_id");
        builder.Property(value => value.Type).HasColumnName("type").HasMaxLength(40);
        builder.Property(value => value.InventoryLotId).HasColumnName("inventory_lot_id");
        builder.Property(value => value.BusinessProductId).HasColumnName("business_product_id");
        builder.Property(value => value.Title).HasColumnName("title").HasMaxLength(128);
        builder.Property(value => value.Message).HasColumnName("message").HasMaxLength(512);
        builder.Property(value => value.ExpirationDate).HasColumnName("expiration_date").HasColumnType("date");
        builder.Property(value => value.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        builder.HasOne<InventoryLot>().WithMany().HasForeignKey(value => new { value.TenantId, Id = value.InventoryLotId, value.BranchId, value.BusinessProductId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id, value.BranchId, value.BusinessProductId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BusinessProduct>().WithMany().HasForeignKey(value => new { value.TenantId, value.BusinessProductId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany().HasForeignKey(value => new { value.TenantId, value.BranchId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.InventoryLotId, value.Type, value.ExpirationDate }).IsUnique().HasDatabaseName("ux_notifications_expiration");
        builder.HasIndex(value => new { value.TenantId, value.BranchId, value.CreatedAt, value.Id }).IsDescending(false, false, true, true)
            .HasDatabaseName("ix_notifications_branch_created");
        builder.HasIndex(value => new { value.TenantId, value.CreatedAt, value.Id }).IsDescending(false, true, true).HasDatabaseName("ix_notifications_created");
    }
}
internal sealed class NotificationReadStateConfiguration : IEntityTypeConfiguration<NotificationReadState>
{
    public void Configure(EntityTypeBuilder<NotificationReadState> builder)
    {
        builder.ToTable("notification_read_states");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.TenantId).HasColumnName("tenant_id");
        builder.Property(value => value.NotificationId).HasColumnName("notification_id");
        builder.Property(value => value.MembershipId).HasColumnName("membership_id");
        builder.Property(value => value.ReadAt).HasColumnName("read_at").HasColumnType("timestamp with time zone");
        builder.HasOne<Notification>().WithMany().HasForeignKey(value => new { value.TenantId, value.NotificationId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Membership>().WithMany().HasForeignKey(value => new { value.TenantId, value.MembershipId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.NotificationId, value.MembershipId }).IsUnique().HasDatabaseName("ux_notification_read_membership");
        builder.HasIndex(value => new { value.TenantId, value.MembershipId, value.NotificationId }).HasDatabaseName("ix_notification_reads_membership");
    }
}
