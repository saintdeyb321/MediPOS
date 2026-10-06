using MediPOS.Domain.Modules.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.Catalog.Persistence.Configurations;

internal static class MedicineDataMapping
{
    public static void Configure<TOwner>(OwnedNavigationBuilder<TOwner, MedicineData> builder, string prefix) where TOwner : class
    {
        builder.Ignore(value => value.ActiveIngredients);
        builder.Property<string[]>("_activeIngredients").HasField("_activeIngredients").UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName(prefix + "active_ingredients").HasColumnType("text[]").IsRequired();
        builder.Property(value => value.NormalizedStrength).HasColumnName(prefix + "normalized_strength").HasMaxLength(128).IsRequired();
        builder.Property(value => value.DosageForm).HasColumnName(prefix + "dosage_form").HasMaxLength(128).IsRequired();
        builder.Property(value => value.Route).HasColumnName(prefix + "route").HasMaxLength(128);
        builder.Property(value => value.SanitaryRegistration).HasColumnName(prefix + "sanitary_registration").HasMaxLength(128);
    }
}
