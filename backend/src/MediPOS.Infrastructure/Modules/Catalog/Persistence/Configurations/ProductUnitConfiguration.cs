using MediPOS.Domain.Modules.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.Catalog.Persistence.Configurations;

internal sealed class ProductUnitConfiguration : IEntityTypeConfiguration<ProductUnit>
{
    public void Configure(EntityTypeBuilder<ProductUnit> builder)
    {
        builder.ToTable("product_units", table =>
        {
            table.HasCheckConstraint("ck_product_units_factor", "conversion_to_base > 0");
            table.HasCheckConstraint("ck_product_units_base", "NOT is_base_unit OR conversion_to_base = 1");
            table.HasCheckConstraint("ck_product_units_name", "name ~ '[^[:space:]]' AND name = btrim(name)");
        });
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.TenantId).HasColumnName("tenant_id");
        builder.Property(value => value.BusinessProductId).HasColumnName("business_product_id");
        builder.Property(value => value.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
        builder.Property(value => value.ConversionToBase).HasColumnName("conversion_to_base").HasPrecision(28, 12);
        builder.Property(value => value.IsBaseUnit).HasColumnName("is_base_unit");
        builder.Property(value => value.IsActive).HasColumnName("is_active");
        builder.HasOne<BusinessProduct>().WithMany().HasForeignKey(value => new { value.TenantId, value.BusinessProductId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.BusinessProductId, value.Name }).IsUnique();
        builder.HasIndex(value => new { value.TenantId, value.BusinessProductId }).IsUnique()
            .HasFilter("is_base_unit").HasDatabaseName("ux_product_units_tenant_product_base");
    }
}
