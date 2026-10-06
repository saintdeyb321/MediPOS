using MediPOS.Domain.Modules.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MediPOS.Infrastructure.Modules.Catalog.Persistence.Configurations;

internal static class ProductSearchConfiguration
{
    internal static void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<BusinessProduct> builder)
    {
        foreach (var (property, column, required) in new (string, string, bool)[]
        {
            ("SearchName", "search_name", true), ("SearchInternalCode", "search_internal_code", true),
            ("SearchBarcode", "search_barcode", false), ("SearchBrand", "search_brand", true),
            ("SearchIngredients", "search_ingredients", true),
        })
        {
            var field = builder.Property<string>(property).HasColumnName(column).HasColumnType("text").UseCollation("C")
                .IsRequired(required).ValueGeneratedOnAddOrUpdate();
            field.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
            field.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        }
        builder.HasIndex("TenantId", "SearchBarcode").HasFilter("is_active AND search_barcode IS NOT NULL")
            .HasDatabaseName("ix_business_products_search_barcode");
        builder.HasIndex("TenantId", "SearchInternalCode").HasFilter("is_active").HasDatabaseName("ix_business_products_search_code");
        builder.HasIndex("TenantId", "SearchName").HasFilter("is_active").HasDatabaseName("ix_business_products_search_name");
        // PostgreSQL can combine these text GIN indexes with existing B-tree indexes starting with tenant_id.
        builder.HasIndex("SearchName").HasMethod("gin").HasOperators("public.gin_trgm_ops").HasFilter("is_active")
            .HasDatabaseName("ix_business_products_search_name_trgm");
        builder.HasIndex("SearchIngredients").HasMethod("gin").HasOperators("public.gin_trgm_ops")
            .HasFilter("is_active AND product_type = 'medicine'").HasDatabaseName("ix_business_products_search_ingredients_trgm");
        builder.HasIndex("SearchBrand").HasMethod("gin").HasOperators("public.gin_trgm_ops").HasFilter("is_active")
            .HasDatabaseName("ix_business_products_search_brand_trgm");
        builder.HasIndex("TenantId", "SearchBrand").HasFilter("is_active").HasDatabaseName("ix_business_products_search_brand");
    }
}
