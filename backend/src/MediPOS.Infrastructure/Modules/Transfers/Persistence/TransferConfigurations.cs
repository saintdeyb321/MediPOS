using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.Transfers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.Transfers.Persistence;

internal sealed class TransferConfiguration : IEntityTypeConfiguration<Transfer>
{
    public void Configure(EntityTypeBuilder<Transfer> b)
    {
        b.ToTable("transfers", table =>
        {
            table.HasCheckConstraint("ck_transfers_branches", "source_branch_id <> destination_branch_id");
            table.HasCheckConstraint("ck_transfers_status", "status IN ('requested','approved','in_transit','received','cancelled')");
            table.HasCheckConstraint("ck_transfers_time", "updated_at >= requested_at");
            table.HasCheckConstraint("ck_transfers_id", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
        });
        b.HasKey(t => t.Id);
        b.HasAlternateKey(t => new { t.TenantId, t.Id });
        b.HasAlternateKey(t => new { t.TenantId, t.Id, t.SourceBranchId, t.DestinationBranchId });
        b.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(t => t.TenantId).HasColumnName("tenant_id");
        b.Property(t => t.SourceBranchId).HasColumnName("source_branch_id");
        b.Property(t => t.DestinationBranchId).HasColumnName("destination_branch_id");
        b.Property(t => t.Status).HasColumnName("status").HasMaxLength(16).HasConversion(t => TransferStatusCodes.ToCode(t), t => TransferStatusCodes.FromCode(t)).IsConcurrencyToken();
        b.Property(t => t.RequestedAt).HasColumnName("requested_at").HasColumnType("timestamp with time zone");
        b.Property(t => t.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        b.HasOne<Tenant>().WithMany().HasForeignKey(t => t.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Branch>().WithMany().HasForeignKey(t => new { t.TenantId, t.SourceBranchId }).HasPrincipalKey(t => new { t.TenantId, t.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Branch>().WithMany().HasForeignKey(t => new { t.TenantId, t.DestinationBranchId }).HasPrincipalKey(t => new { t.TenantId, t.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(t => t.Lines).WithOne().HasForeignKey(t => new { t.TenantId, t.TransferId }).HasPrincipalKey(t => new { t.TenantId, t.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(t => t.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(t => new { t.TenantId, t.SourceBranchId, t.Status });
        b.HasIndex(t => new { t.TenantId, t.DestinationBranchId, t.Status });
    }
}
internal sealed class TransferLineConfiguration : IEntityTypeConfiguration<TransferLine>
{
    public void Configure(EntityTypeBuilder<TransferLine> b)
    {
        b.ToTable("transfer_lines", table =>
        {
            table.HasCheckConstraint("ck_transfer_lines_quantity", "requested_quantity > 0 AND requested_quantity <= 9999999999999999.999999999999 AND requested_base_quantity > 0 AND requested_base_quantity <= 9999999999999999.999999999999 AND conversion_to_base_snapshot > 0 AND conversion_to_base_snapshot <= 9999999999999999.999999999999 AND requested_base_quantity = requested_quantity * conversion_to_base_snapshot");
            table.HasCheckConstraint("ck_transfer_lines_unit", "unit_name_snapshot ~ '[^[:space:]]' AND product_unit_id_snapshot <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("ck_transfer_lines_id", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
        });
        b.HasKey(t => t.Id);
        b.HasAlternateKey(t => new { t.TenantId, t.TransferId, t.BusinessProductId, t.Id });
        b.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(t => t.TenantId).HasColumnName("tenant_id");
        b.Property(t => t.TransferId).HasColumnName("transfer_id");
        b.Property(t => t.BusinessProductId).HasColumnName("business_product_id");
        b.Property(t => t.ProductUnitIdSnapshot).HasColumnName("product_unit_id_snapshot");
        b.Property(t => t.RequestedQuantity).HasColumnName("requested_quantity").HasPrecision(28, 12);
        b.Property(t => t.RequestedBaseQuantity).HasColumnName("requested_base_quantity").HasPrecision(28, 12);
        b.Property(t => t.ConversionToBaseSnapshot).HasColumnName("conversion_to_base_snapshot").HasPrecision(28, 12);
        b.Property(t => t.UnitNameSnapshot).HasColumnName("unit_name_snapshot").HasMaxLength(128);
        b.HasOne<BusinessProduct>().WithMany().HasForeignKey(t => new { t.TenantId, t.BusinessProductId }).HasPrincipalKey(t => new { t.TenantId, t.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(t => new { t.TenantId, t.TransferId, t.BusinessProductId, t.ProductUnitIdSnapshot }).IsUnique().HasDatabaseName("ux_transfer_lines_selection");
    }
}
internal sealed class TransferEventConfiguration : IEntityTypeConfiguration<TransferEvent>
{
    public void Configure(EntityTypeBuilder<TransferEvent> b)
    {
        b.ToTable("transfer_events", table =>
        {
            table.HasCheckConstraint("ck_transfer_events_type", "event_type IN ('requested','approved','dispatched','received','cancelled')");
            table.HasCheckConstraint("ck_transfer_events_reason", "(event_type = 'cancelled' AND reason IS NOT NULL AND reason ~ '[^[:space:]]' AND reason = btrim(reason) AND length(reason) <= 512) OR (event_type <> 'cancelled' AND reason IS NULL)");
            table.HasCheckConstraint("ck_transfer_events_ids", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND actor_id <> '00000000-0000-0000-0000-000000000000'::uuid");
        });
        b.HasKey(t => t.Id);
        b.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(t => t.TenantId).HasColumnName("tenant_id");
        b.Property(t => t.TransferId).HasColumnName("transfer_id");
        b.Property(t => t.EventType).HasColumnName("event_type").HasMaxLength(16).HasConversion(t => TransferEventCodes.ToCode(t), t => TransferEventCodes.FromCode(t));
        b.Property(t => t.ActorId).HasColumnName("actor_id");
        b.Property(t => t.OccurredAt).HasColumnName("occurred_at").HasColumnType("timestamp with time zone");
        b.Property(t => t.Reason).HasColumnName("reason").HasMaxLength(512);
        b.HasOne<Transfer>().WithMany().HasForeignKey(t => new { t.TenantId, t.TransferId }).HasPrincipalKey(t => new { t.TenantId, t.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(t => new { t.TenantId, t.TransferId, t.EventType }).IsUnique().HasDatabaseName("ux_transfer_events_transition");
    }
}
internal sealed class TransferLotAllocationConfiguration : IEntityTypeConfiguration<TransferLotAllocation>
{
    public void Configure(EntityTypeBuilder<TransferLotAllocation> b)
    {
        b.ToTable("transfer_lot_allocations", table =>
        {
            table.HasCheckConstraint("ck_transfer_allocations_quantity", "dispatched_quantity_base > 0 AND dispatched_quantity_base <= 9999999999999999.999999999999 AND (received_quantity_base IS NULL OR (received_quantity_base >= 0 AND received_quantity_base <= dispatched_quantity_base))");
            table.HasCheckConstraint("ck_transfer_allocations_batch", "batch_number_snapshot IS NULL OR batch_number_snapshot ~ '[^[:space:]]'");
            table.HasCheckConstraint("ck_transfer_allocations_id", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
        });
        b.HasKey(t => t.Id);
        b.HasAlternateKey(t => new { t.TenantId, t.Id, t.BusinessProductId });
        b.HasAlternateKey(t => new { t.TenantId, t.DestinationBranchId, t.BusinessProductId, t.SourcePurchaseLineId, t.Id });
        b.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(t => t.TenantId).HasColumnName("tenant_id");
        b.Property(t => t.TransferId).HasColumnName("transfer_id");
        b.Property(t => t.TransferLineId).HasColumnName("transfer_line_id");
        b.Property(t => t.SourceBranchId).HasColumnName("source_branch_id");
        b.Property(t => t.DestinationBranchId).HasColumnName("destination_branch_id");
        b.Property(t => t.BusinessProductId).HasColumnName("business_product_id");
        b.Property(t => t.SourceInventoryLotId).HasColumnName("source_inventory_lot_id");
        b.Property(t => t.SourcePurchaseLineId).HasColumnName("source_purchase_line_id");
        b.Property(t => t.BatchNumberSnapshot).HasColumnName("batch_number_snapshot").HasMaxLength(128);
        b.Property(t => t.ExpirationDateSnapshot).HasColumnName("expiration_date_snapshot").HasColumnType("date");
        b.Property(t => t.DispatchedQuantityBase).HasColumnName("dispatched_quantity_base").HasPrecision(28, 12);
        b.Property(t => t.ReceivedQuantityBase).HasColumnName("received_quantity_base").HasPrecision(28, 12).IsConcurrencyToken();
        b.Property(t => t.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        b.Ignore(t => t.DifferenceBase);
        b.HasOne<Transfer>().WithMany().HasForeignKey(t => new { t.TenantId, t.TransferId, t.SourceBranchId, t.DestinationBranchId })
            .HasPrincipalKey(t => new { t.TenantId, t.Id, t.SourceBranchId, t.DestinationBranchId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TransferLine>().WithMany().HasForeignKey(t => new { t.TenantId, t.TransferId, t.BusinessProductId, t.TransferLineId })
            .HasPrincipalKey(t => new { t.TenantId, t.TransferId, t.BusinessProductId, t.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<InventoryLot>().WithMany().HasForeignKey(t => new { t.TenantId, t.SourceInventoryLotId, t.SourceBranchId, t.BusinessProductId, t.SourcePurchaseLineId })
            .HasPrincipalKey(t => new { t.TenantId, t.Id, t.BranchId, t.BusinessProductId, t.SourcePurchaseLineId }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(t => new { t.TenantId, t.TransferId, t.TransferLineId, t.SourceInventoryLotId }).IsUnique().HasDatabaseName("ux_transfer_allocations_line_lot");
    }
}
