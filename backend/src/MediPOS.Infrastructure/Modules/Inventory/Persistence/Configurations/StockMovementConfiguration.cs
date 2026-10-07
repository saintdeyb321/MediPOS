using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.Purchasing;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Domain.Modules.Transfers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.Inventory.Persistence.Configurations;

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    internal const string ReversalIndex = "ux_stock_movements_tenant_reverses";
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("stock_movements", table =>
        {
            table.HasCheckConstraint("ck_stock_movements_delta",
                "(movement_type = 'purchase_receipt' AND quantity_delta_base > 0 AND source_purchase_line_id IS NOT NULL AND source_sale_line_id IS NULL AND source_transfer_lot_allocation_id IS NULL AND reverses_stock_movement_id IS NULL AND reason IS NULL) OR " +
                "(movement_type = 'adjustment' AND quantity_delta_base <> 0 AND source_purchase_line_id IS NULL AND source_sale_line_id IS NULL AND source_transfer_lot_allocation_id IS NULL AND reverses_stock_movement_id IS NULL AND reason ~ '[^[:space:]]' AND reason = btrim(reason) AND reason IS NOT NULL) OR " +
                "(movement_type = 'sale' AND quantity_delta_base < 0 AND source_sale_line_id IS NOT NULL AND source_purchase_line_id IS NULL AND source_transfer_lot_allocation_id IS NULL AND reverses_stock_movement_id IS NULL AND reason IS NULL) OR " +
                "(movement_type = 'sale_reversal' AND quantity_delta_base > 0 AND source_sale_line_id IS NOT NULL AND source_transfer_lot_allocation_id IS NULL AND reverses_stock_movement_id IS NOT NULL AND reverses_stock_movement_id <> id AND source_purchase_line_id IS NULL AND reason IS NULL) OR " +
                "(movement_type IN ('transfer_dispatch','transfer_receipt') AND source_transfer_lot_allocation_id IS NOT NULL AND source_purchase_line_id IS NULL AND source_sale_line_id IS NULL AND reverses_stock_movement_id IS NULL AND reason IS NULL AND " +
                "((movement_type = 'transfer_dispatch' AND quantity_delta_base < 0) OR (movement_type = 'transfer_receipt' AND quantity_delta_base > 0)))");
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
        builder.Property(value => value.SourceSaleLineId).HasColumnName("source_sale_line_id");
        builder.Property(value => value.SourceTransferLotAllocationId).HasColumnName("source_transfer_lot_allocation_id");
        builder.Property(value => value.ReversesStockMovementId).HasColumnName("reverses_stock_movement_id");
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
        builder.HasOne<SaleLine>().WithMany().HasForeignKey(value => new { value.TenantId, value.SourceSaleLineId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TransferLotAllocation>().WithMany().HasForeignKey(value => new { value.TenantId, value.SourceTransferLotAllocationId, value.BusinessProductId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id, value.BusinessProductId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.MovementType, value.SourceTransferLotAllocationId }).IsUnique()
            .HasFilter("source_transfer_lot_allocation_id IS NOT NULL").HasDatabaseName("ux_stock_movements_transfer_effect");
        builder.HasOne<StockMovement>().WithMany().HasForeignKey(value => new { value.TenantId, value.ReversesStockMovementId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<InventoryLot>().WithMany().HasForeignKey(value => new { value.TenantId, value.InventoryLotId, value.BranchId, value.BusinessProductId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id, value.BranchId, value.BusinessProductId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<InventoryLot>().WithMany().HasForeignKey(value => new { value.TenantId, value.InventoryLotId, value.BranchId, value.BusinessProductId, value.SourcePurchaseLineId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id, value.BranchId, value.BusinessProductId, value.SourcePurchaseLineId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.BranchId, value.BusinessProductId, value.OccurredAt });
        builder.HasIndex(value => new { value.TenantId, value.InventoryLotId, value.OccurredAt });
        builder.HasIndex(value => new { value.TenantId, value.SourcePurchaseLineId }).IsUnique().HasFilter("source_purchase_line_id IS NOT NULL");
        builder.HasIndex(value => new { value.TenantId, value.SourceSaleLineId }).HasFilter("source_sale_line_id IS NOT NULL");
        builder.HasIndex(value => new { value.TenantId, value.ReversesStockMovementId }).IsUnique().HasFilter("reverses_stock_movement_id IS NOT NULL").HasDatabaseName(ReversalIndex);
    }
}
