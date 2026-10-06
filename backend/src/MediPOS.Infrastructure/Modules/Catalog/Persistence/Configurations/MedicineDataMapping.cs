using MediPOS.Domain.Modules.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.Catalog.Persistence.Configurations;

internal static class MedicineDataMapping
{
    public static void Configure<TOwner>(OwnedNavigationBuilder<TOwner, MedicineData> builder, string prefix) where TOwner : class
    {
        builder.Ignore(value => value.ActiveIngredients);
        builder.Ignore(value => value.Components);
        builder.Ignore(value => value.IsNormalized);
        builder.Property<string[]>("_activeIngredients").HasField("_activeIngredients").UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName(prefix + "active_ingredients").HasColumnType("text[]").IsRequired();
        builder.Property(value => value.NormalizedStrength).HasColumnName(prefix + "normalized_strength").HasColumnType("text").IsRequired();
        builder.Property(value => value.DosageForm).HasColumnName(prefix + "dosage_form").HasMaxLength(128).IsRequired();
        builder.Property(value => value.Route).HasColumnName(prefix + "route").HasMaxLength(128);
        builder.Property(value => value.SanitaryRegistration).HasColumnName(prefix + "sanitary_registration").HasMaxLength(128);
        builder.Property<string[]?>("_normalizedIngredients").HasField("_normalizedIngredients").UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName(prefix + "normalized_ingredients").HasColumnType("text[]");
        builder.Property<string[]?>("_normalizedStrengths").HasField("_normalizedStrengths").UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName(prefix + "normalized_strengths").HasColumnType("text[]");
        builder.Property(value => value.CanonicalDosageForm).HasColumnName(prefix + "canonical_dosage_form").HasMaxLength(128);
        builder.Property(value => value.CanonicalRoute).HasColumnName(prefix + "canonical_route").HasMaxLength(128);
        builder.Property(value => value.EquivalenceKey).HasColumnName(prefix + "equivalence_key").HasMaxLength(64);
        builder.HasIndex(value => value.EquivalenceKey).HasFilter(prefix + "equivalence_key IS NOT NULL");
    }
}
