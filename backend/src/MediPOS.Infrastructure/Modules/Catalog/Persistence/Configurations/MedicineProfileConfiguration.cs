using MediPOS.Domain.Modules.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.Catalog.Persistence.Configurations;

internal sealed class MedicineProfileConfiguration : IEntityTypeConfiguration<MedicineProfile>
{
    public void Configure(EntityTypeBuilder<MedicineProfile> builder)
    {
        builder.ToTable("medicine_profiles", table =>
        {
            table.HasCheckConstraint("ck_medicine_profiles_type", "product_type = 'medicine'");
            table.HasCheckConstraint("ck_medicine_profiles_normalization", PharmaNormalizationConstraint.ForColumns(string.Empty));
            table.HasCheckConstraint("ck_medicine_profiles_data", """
                cardinality(active_ingredients) > 0 AND array_position(active_ingredients, NULL) IS NULL
                AND array_position(active_ingredients, '') IS NULL AND length(btrim(normalized_strength)) > 0
                AND length(btrim(dosage_form)) > 0
                """);
        });
        builder.HasKey(value => value.GlobalProductId);
        builder.Property(value => value.GlobalProductId).HasColumnName("global_product_id").ValueGeneratedNever();
        builder.Property(value => value.ProductType).HasColumnName("product_type").HasMaxLength(16)
            .HasConversion(value => ProductTypeCodes.ToCode(value), value => ProductTypeCodes.FromCode(value));
        builder.OwnsOne(value => value.Data, data => MedicineDataMapping.Configure(data, string.Empty));
        builder.Navigation(value => value.Data).IsRequired();
    }
}
