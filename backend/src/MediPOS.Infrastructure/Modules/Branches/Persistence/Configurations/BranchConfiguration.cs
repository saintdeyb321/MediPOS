using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.Branches.Persistence.Configurations;

internal sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("branches", table => table.HasCheckConstraint("ck_branches_name", "name ~ '[^[:space:]]'"));
        builder.HasKey(branch => branch.Id);
        builder.HasAlternateKey(branch => new { branch.TenantId, branch.Id });
        builder.Property(branch => branch.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(branch => branch.TenantId).HasColumnName("tenant_id");
        builder.Property(branch => branch.LegalEntityId).HasColumnName("legal_entity_id");
        builder.Property(branch => branch.Name).HasColumnName("name").IsRequired();
        builder.Property(branch => branch.IsMainHub).HasColumnName("is_main_hub");
        builder.Property(branch => branch.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        builder.HasOne<Tenant>().WithMany().HasForeignKey(branch => branch.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LegalEntity>().WithMany()
            .HasForeignKey(branch => new { branch.TenantId, branch.LegalEntityId })
            .HasPrincipalKey(legalEntity => new { legalEntity.TenantId, legalEntity.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(branch => new { branch.TenantId, branch.LegalEntityId });
        builder.HasIndex(branch => branch.TenantId).IsUnique().HasFilter("is_main_hub")
            .HasDatabaseName("ux_branches_tenant_main_hub");
    }
}
