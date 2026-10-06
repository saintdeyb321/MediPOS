using MediPOS.Domain.Modules.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.Catalog.Persistence.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories", table => table.HasCheckConstraint("ck_categories_name", "length(btrim(name)) > 0"));
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
        builder.Property(value => value.IsActive).HasColumnName("is_active");
        builder.Property(value => value.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
    }
}
