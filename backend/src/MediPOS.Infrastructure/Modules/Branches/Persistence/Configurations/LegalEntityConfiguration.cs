using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.Branches.Persistence.Configurations;

internal sealed class LegalEntityConfiguration : IEntityTypeConfiguration<LegalEntity>
{
    public void Configure(EntityTypeBuilder<LegalEntity> builder)
    {
        builder.ToTable("legal_entities", table =>
        {
            table.HasCheckConstraint("ck_legal_entities_name", "legal_name ~ '[^[:space:]]'");
            table.HasCheckConstraint("ck_legal_entities_ruc", "ruc ~ '[^[:space:]]'");
        });
        builder.HasKey(legalEntity => legalEntity.Id);
        builder.Property(legalEntity => legalEntity.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(legalEntity => legalEntity.TenantId).HasColumnName("tenant_id");
        builder.Property(legalEntity => legalEntity.LegalName).HasColumnName("legal_name").IsRequired();
        builder.Property(legalEntity => legalEntity.Ruc).HasColumnName("ruc").IsRequired();
        builder.Property(legalEntity => legalEntity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        builder.HasAlternateKey(legalEntity => new { legalEntity.TenantId, legalEntity.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(legalEntity => legalEntity.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
