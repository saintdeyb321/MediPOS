using MediPOS.Domain.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.TenancyLicensing.Persistence.Configurations;

internal sealed class LicenseConfiguration : IEntityTypeConfiguration<License>
{
    public void Configure(EntityTypeBuilder<License> builder)
    {
        builder.ToTable("licenses", table =>
        {
            table.HasCheckConstraint("ck_licenses_period", "expires_at > starts_at");
            table.HasCheckConstraint("ck_licenses_max_branches", "max_branches BETWEEN 1 AND 5");
            table.HasCheckConstraint("ck_licenses_status", $"status IN ({LicenseCodes.StatusValues})");
        });

        builder.HasKey(license => license.Id);
        builder.Property(license => license.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(license => license.TenantId).HasColumnName("tenant_id");
        builder.HasAlternateKey(license => new { license.TenantId, license.Id });
        builder.Property(license => license.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        builder.Property(license => license.StartsAt).HasColumnName("starts_at").HasColumnType("timestamp with time zone");
        builder.Property(license => license.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp with time zone");
        builder.Property(license => license.MaxBranches).HasColumnName("max_branches");
        builder.Property(license => license.Status).HasColumnName("status")
            .HasConversion(status => LicenseCodes.StatusToCode(status), code => LicenseCodes.StatusFromCode(code));

        builder.HasOne<Tenant>().WithOne(tenant => tenant.License)
            .HasForeignKey<License>(license => license.TenantId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(license => license.Changes).WithOne()
            .HasForeignKey(change => new { change.TenantId, change.LicenseId })
            .HasPrincipalKey(license => new { license.TenantId, license.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(license => license.Changes).HasField("_changes").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property<uint>("Version").IsRowVersion().HasColumnName("xmin");
    }
}
