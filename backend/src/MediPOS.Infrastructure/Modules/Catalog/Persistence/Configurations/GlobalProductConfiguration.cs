using MediPOS.Domain.Modules.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.Catalog.Persistence.Configurations;

internal sealed class GlobalProductConfiguration : IEntityTypeConfiguration<GlobalProduct>
{
    public void Configure(EntityTypeBuilder<GlobalProduct> builder)
    {
        builder.ToTable("global_products", table =>
        {
            table.HasCheckConstraint("ck_global_products_type", "product_type IN ('medicine', 'retail')");
            table.HasCheckConstraint("ck_global_products_text", "length(btrim(name)) > 0 AND length(btrim(brand_or_laboratory)) > 0 AND (barcode IS NULL OR length(btrim(barcode)) > 0)");
        });
        builder.HasKey(value => value.Id);
        builder.HasAlternateKey(value => new { value.Id, value.ProductType });
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.ProductType).HasColumnName("product_type").HasMaxLength(16)
            .HasConversion(value => ProductTypeCodes.ToCode(value), value => ProductTypeCodes.FromCode(value));
        builder.Property(value => value.Name).HasColumnName("name").HasMaxLength(256).IsRequired();
        builder.Property(value => value.CategoryId).HasColumnName("category_id");
        builder.Property(value => value.BrandOrLaboratory).HasColumnName("brand_or_laboratory").HasMaxLength(200).IsRequired();
        builder.Property(value => value.Barcode).HasColumnName("barcode").HasMaxLength(128);
        builder.Property(value => value.IsActive).HasColumnName("is_active");
        builder.Property(value => value.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        builder.HasOne<Category>().WithMany().HasForeignKey(value => value.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.MedicineProfile).WithOne()
            .HasForeignKey<MedicineProfile>(value => new { value.GlobalProductId, value.ProductType })
            .HasPrincipalKey<GlobalProduct>(value => new { value.Id, value.ProductType }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => value.Name);
        builder.HasIndex(value => value.BrandOrLaboratory);
        builder.HasIndex(value => value.Barcode).HasFilter("barcode IS NOT NULL");
    }
}
