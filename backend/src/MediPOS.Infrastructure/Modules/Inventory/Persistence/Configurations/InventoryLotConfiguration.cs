using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.Purchasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.Inventory.Persistence.Configurations;

internal sealed class InventoryLotConfiguration : IEntityTypeConfiguration<InventoryLot>
{
    public void Configure(EntityTypeBuilder<InventoryLot> builder)
    {
        builder.ToTable("inventory_lots", table =>
        {
            table.HasCheckConstraint("ck_inventory_lots_available", "quantity_available_base >= 0");
            table.HasCheckConstraint("ck_inventory_lots_batch", "batch_number IS NULL OR batch_number ~ '[^[:space:]]'");
        });
        builder.HasKey(value => value.Id);
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.TenantId).HasColumnName("tenant_id");
        builder.Property(value => value.BranchId).HasColumnName("branch_id");
        builder.Property(value => value.BusinessProductId).HasColumnName("business_product_id");
        builder.Property(value => value.SourcePurchaseLineId).HasColumnName("source_purchase_line_id");
        builder.Property(value => value.BatchNumber).HasColumnName("batch_number").HasMaxLength(128);
        builder.Property(value => value.ExpirationDate).HasColumnName("expiration_date").HasColumnType("date");
        builder.Property(value => value.QuantityAvailableBase).HasColumnName("quantity_available_base").HasColumnType("numeric");
        builder.Property(value => value.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        builder.HasAlternateKey(value => new { value.TenantId, value.Id, value.BranchId, value.BusinessProductId });
        builder.HasAlternateKey(value => new { value.TenantId, value.Id, value.BranchId, value.BusinessProductId, value.SourcePurchaseLineId });
        builder.HasOne<Branch>().WithMany().HasForeignKey(value => new { value.TenantId, value.BranchId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BusinessProduct>().WithMany().HasForeignKey(value => new { value.TenantId, value.BusinessProductId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PurchaseLine>().WithMany().HasForeignKey(value => new { value.TenantId, value.SourcePurchaseLineId, value.BusinessProductId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id, value.BusinessProductId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.BranchId, value.BusinessProductId, value.ExpirationDate, value.CreatedAt, value.Id })
            .HasFilter("quantity_available_base > 0 AND expiration_date IS NOT NULL").HasDatabaseName("ix_inventory_lots_fefo");
        builder.HasIndex(value => new { value.TenantId, value.ExpirationDate, value.BranchId })
            .HasFilter("quantity_available_base > 0 AND expiration_date IS NOT NULL").HasDatabaseName("ix_inventory_lots_expiring");
        builder.HasIndex(value => new { value.TenantId, value.SourcePurchaseLineId }).IsUnique();
    }
}
