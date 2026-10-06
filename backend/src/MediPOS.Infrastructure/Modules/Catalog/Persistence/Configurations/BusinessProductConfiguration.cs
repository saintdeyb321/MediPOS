using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.Catalog.Persistence.Configurations;

internal sealed class BusinessProductConfiguration : IEntityTypeConfiguration<BusinessProduct>
{
    internal const string InternalCodeIndex = "ux_business_products_tenant_internal_code";

    public void Configure(EntityTypeBuilder<BusinessProduct> builder)
    {
        builder.ToTable("business_products", table =>
        {
            table.HasCheckConstraint("ck_business_products_type", "product_type IN ('medicine', 'retail')");
            table.HasCheckConstraint("ck_business_products_text", "length(btrim(internal_code)) > 0 AND internal_code = btrim(internal_code) AND length(btrim(name)) > 0 AND length(btrim(brand_or_laboratory)) > 0 AND (barcode IS NULL OR length(btrim(barcode)) > 0)");
            table.HasCheckConstraint("ck_business_products_prices", "retail_price >= 0 AND (wholesale_price IS NULL OR wholesale_price >= 0)");
            table.HasCheckConstraint("ck_business_products_normalization", PharmaNormalizationConstraint.ForColumns("medicine_"));
            table.HasCheckConstraint("ck_business_products_retail_normalization", "product_type = 'medicine' OR medicine_equivalence_key IS NULL");
            table.HasCheckConstraint("ck_business_products_medicine", """
                (product_type = 'medicine' AND medicine_active_ingredients IS NOT NULL
                    AND cardinality(medicine_active_ingredients) > 0 AND array_position(medicine_active_ingredients, NULL) IS NULL
                    AND array_position(medicine_active_ingredients, '') IS NULL
                    AND medicine_normalized_strength IS NOT NULL AND length(btrim(medicine_normalized_strength)) > 0
                    AND medicine_dosage_form IS NOT NULL AND length(btrim(medicine_dosage_form)) > 0)
                OR (product_type = 'retail' AND medicine_active_ingredients IS NULL AND medicine_normalized_strength IS NULL
                    AND medicine_dosage_form IS NULL AND medicine_route IS NULL AND medicine_sanitary_registration IS NULL)
                """);
        });
        builder.HasKey(value => value.Id);
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.TenantId).HasColumnName("tenant_id");
        builder.Property(value => value.GlobalProductId).HasColumnName("global_product_id");
        builder.Property(value => value.InternalCode).HasColumnName("internal_code").HasMaxLength(64).IsRequired();
        builder.Property(value => value.Name).HasColumnName("name").HasMaxLength(256).IsRequired();
        builder.Property(value => value.ProductType).HasColumnName("product_type").HasMaxLength(16)
            .HasConversion(value => ProductTypeCodes.ToCode(value), value => ProductTypeCodes.FromCode(value));
        builder.Property(value => value.CategoryId).HasColumnName("category_id");
        builder.Property(value => value.BrandOrLaboratory).HasColumnName("brand_or_laboratory").HasMaxLength(200).IsRequired();
        builder.Property(value => value.Barcode).HasColumnName("barcode").HasMaxLength(128);
        builder.Property(value => value.RetailPrice).HasColumnName("retail_price").HasPrecision(18, 4);
        builder.Property(value => value.WholesalePrice).HasColumnName("wholesale_price").HasPrecision(18, 4);
        builder.Property(value => value.IsActive).HasColumnName("is_active");
        builder.Property(value => value.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        builder.OwnsOne(value => value.Medicine, data =>
        {
            MedicineDataMapping.Configure(data, "medicine_");
            data.Property<uint>("Version").IsRowVersion().HasColumnName("xmin");
        });
        builder.Navigation(value => value.Medicine).IsRequired(false);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(value => value.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Category>().WithMany().HasForeignKey(value => value.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GlobalProduct>().WithMany().HasForeignKey(value => value.GlobalProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.InternalCode }).IsUnique().HasDatabaseName(InternalCodeIndex);
        builder.HasIndex(value => new { value.TenantId, value.Name });
        builder.HasIndex(value => new { value.TenantId, value.Barcode }).HasFilter("barcode IS NOT NULL");
        builder.Property<uint>("Version").IsRowVersion().HasColumnName("xmin");
    }
}
