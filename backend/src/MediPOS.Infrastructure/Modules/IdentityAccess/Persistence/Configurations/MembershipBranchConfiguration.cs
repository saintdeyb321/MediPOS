using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.IdentityAccess.Persistence.Configurations;

internal sealed class MembershipBranchConfiguration : IEntityTypeConfiguration<MembershipBranch>
{
    public void Configure(EntityTypeBuilder<MembershipBranch> builder)
    {
        builder.ToTable("membership_branches");
        builder.HasKey(value => new { value.TenantId, value.MembershipId, value.BranchId });
        builder.Property(value => value.TenantId).HasColumnName("tenant_id");
        builder.Property(value => value.MembershipId).HasColumnName("membership_id");
        builder.Property(value => value.BranchId).HasColumnName("branch_id");
        builder.HasOne<Membership>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.MembershipId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.BranchId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.BranchId });
    }
}
