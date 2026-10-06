using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.IdentityAccess.Persistence.Configurations;

internal sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.ToTable("memberships", table =>
        {
            table.HasCheckConstraint("ck_memberships_role", "role IN ('owner', 'pharmacist', 'cashier')");
            table.HasCheckConstraint("ck_memberships_activation",
                "(is_active AND deactivated_at IS NULL) OR (NOT is_active AND deactivated_at IS NOT NULL AND deactivated_at >= created_at)");
        });
        builder.HasKey(value => value.Id);
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.TenantId).HasColumnName("tenant_id");
        builder.Property(value => value.UserId).HasColumnName("user_id");
        builder.Property(value => value.Role).HasColumnName("role").HasConversion(
            role => TenantRoleCodes.ToCode(role), code => TenantRoleCodes.FromCode(code)).IsRequired();
        builder.Property(value => value.IsActive).HasColumnName("is_active");
        builder.Property(value => value.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        builder.Property(value => value.DeactivatedAt).HasColumnName("deactivated_at").HasColumnType("timestamp with time zone");
        builder.HasOne<Tenant>().WithMany().HasForeignKey(value => value.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(value => value.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.UserId }).IsUnique().HasFilter("is_active")
            .HasDatabaseName("ux_memberships_tenant_user_active");
        builder.HasIndex(value => new { value.TenantId, value.Role }).HasFilter("is_active");
    }
}
