using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.Purchasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.Inventory.Persistence.Configurations;

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("stock_movements", table =>
        {
            table.HasCheckConstraint("ck_stock_movements_delta",
                "(movement_type = 'purchase_receipt' AND quantity_delta_base > 0 AND source_purchase_line_id IS NOT NULL AND reason IS NULL) OR " +
                "(movement_type = 'adjustment' AND quantity_delta_base <> 0 AND source_purchase_line_id IS NULL AND reason ~ '[^[:space:]]' AND reason = btrim(reason) AND reason IS NOT NULL)");
            table.HasCheckConstraint("ck_stock_movements_actor", "actor_id <> '00000000-0000-0000-0000-000000000000'::uuid");
        });
        builder.HasKey(value => value.Id);
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.TenantId).HasColumnName("tenant_id");
        builder.Property(value => value.BranchId).HasColumnName("branch_id");
        builder.Property(value => value.BusinessProductId).HasColumnName("business_product_id");
        builder.Property(value => value.InventoryLotId).HasColumnName("inventory_lot_id");
        builder.Property(value => value.SourcePurchaseLineId).HasColumnName("source_purchase_line_id");
        builder.Property(value => value.MovementType).HasColumnName("movement_type").HasMaxLength(32).HasConversion(
            value => StockMovementCodes.ToCode(value), value => StockMovementCodes.FromCode(value));
        builder.Property(value => value.QuantityDeltaBase).HasColumnName("quantity_delta_base").HasColumnType("numeric");
        builder.Property(value => value.Reason).HasColumnName("reason").HasMaxLength(512);
        builder.Property(value => value.ActorId).HasColumnName("actor_id");
        builder.Property(value => value.OccurredAt).HasColumnName("occurred_at").HasColumnType("timestamp with time zone");
        builder.HasOne<Branch>().WithMany().HasForeignKey(value => new { value.TenantId, value.BranchId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BusinessProduct>().WithMany().HasForeignKey(value => new { value.TenantId, value.BusinessProductId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PurchaseLine>().WithMany().HasForeignKey(value => new { value.TenantId, value.SourcePurchaseLineId, value.BusinessProductId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id, value.BusinessProductId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<InventoryLot>().WithMany().HasForeignKey(value => new { value.TenantId, value.InventoryLotId, value.BranchId, value.BusinessProductId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id, value.BranchId, value.BusinessProductId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<InventoryLot>().WithMany().HasForeignKey(value => new { value.TenantId, value.InventoryLotId, value.BranchId, value.BusinessProductId, value.SourcePurchaseLineId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id, value.BranchId, value.BusinessProductId, value.SourcePurchaseLineId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.BranchId, value.BusinessProductId, value.OccurredAt });
        builder.HasIndex(value => new { value.TenantId, value.InventoryLotId, value.OccurredAt });
        builder.HasIndex(value => new { value.TenantId, value.SourcePurchaseLineId }).IsUnique().HasFilter("source_purchase_line_id IS NOT NULL");
    }
}
