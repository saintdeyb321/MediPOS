using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Purchasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.Purchasing.Persistence.Configurations;

internal sealed class PurchaseLineConfiguration : IEntityTypeConfiguration<PurchaseLine>
{
    public void Configure(EntityTypeBuilder<PurchaseLine> builder)
    {
        builder.ToTable("purchase_lines", table =>
        {
            table.HasCheckConstraint("ck_purchase_lines_quantities", "quantity > 0 AND conversion_to_base_snapshot > 0 AND base_quantity > 0 AND base_quantity = quantity * conversion_to_base_snapshot");
            table.HasCheckConstraint("ck_purchase_lines_cost", "unit_cost >= 0");
            table.HasCheckConstraint("ck_purchase_lines_unit_name", "unit_name_snapshot ~ '[^[:space:]]'");
        });
        builder.HasKey(value => value.Id);
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.TenantId).HasColumnName("tenant_id");
        builder.Property(value => value.PurchaseId).HasColumnName("purchase_id");
        builder.Property(value => value.BusinessProductId).HasColumnName("business_product_id");
        builder.Property(value => value.Quantity).HasColumnName("quantity").HasColumnType("numeric");
        builder.Property(value => value.UnitNameSnapshot).HasColumnName("unit_name_snapshot").HasMaxLength(128).IsRequired();
        builder.Property(value => value.ConversionToBaseSnapshot).HasColumnName("conversion_to_base_snapshot").HasPrecision(28, 12);
        builder.Property(value => value.BaseQuantity).HasColumnName("base_quantity").HasColumnType("numeric");
        builder.Property(value => value.UnitCost).HasColumnName("unit_cost").HasColumnType("numeric");
        builder.Property(value => value.BatchNumber).HasColumnName("batch_number").HasMaxLength(128);
        builder.Property(value => value.ExpirationDate).HasColumnName("expiration_date").HasColumnType("date");
        builder.HasAlternateKey(value => new { value.TenantId, value.Id, value.BusinessProductId });
        builder.HasOne<Purchase>().WithMany().HasForeignKey(value => new { value.TenantId, value.PurchaseId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BusinessProduct>().WithMany().HasForeignKey(value => new { value.TenantId, value.BusinessProductId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.PurchaseId });
    }
}
