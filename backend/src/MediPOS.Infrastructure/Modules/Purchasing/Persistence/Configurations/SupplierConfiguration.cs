using MediPOS.Domain.Modules.Purchasing;
using MediPOS.Domain.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.Purchasing.Persistence.Configurations;

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("suppliers", table =>
        {
            table.HasCheckConstraint("ck_suppliers_name", "name ~ '[^[:space:]]' AND name = btrim(name)");
        });
        builder.HasKey(value => value.Id);
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.TenantId).HasColumnName("tenant_id");
        builder.Property(value => value.Name).HasColumnName("name").HasMaxLength(256).IsRequired();
        builder.Property(value => value.Ruc).HasColumnName("ruc").HasMaxLength(32);
        builder.Property(value => value.Contact).HasColumnName("contact").HasMaxLength(512);
        builder.Property(value => value.IsActive).HasColumnName("is_active");
        builder.Property(value => value.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        builder.HasOne<Tenant>().WithMany().HasForeignKey(value => value.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.Name });
        builder.HasIndex(value => new { value.TenantId, value.Ruc }).HasFilter("ruc IS NOT NULL");
    }
}
