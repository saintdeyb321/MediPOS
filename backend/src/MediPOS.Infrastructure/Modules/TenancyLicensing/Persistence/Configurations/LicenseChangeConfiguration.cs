using MediPOS.Domain.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.TenancyLicensing.Persistence.Configurations;

internal sealed class LicenseChangeConfiguration : IEntityTypeConfiguration<LicenseChange>
{
    public void Configure(EntityTypeBuilder<LicenseChange> builder)
    {
        builder.ToTable("license_changes", table =>
        {
            table.HasCheckConstraint("ck_license_changes_kind", $"kind IN ({LicenseCodes.ChangeKindValues})");
            table.HasCheckConstraint("ck_license_changes_status", $"new_status IN ({LicenseCodes.StatusValues}) AND (previous_status IS NULL OR previous_status IN ({LicenseCodes.StatusValues}))");
            table.HasCheckConstraint("ck_license_changes_new_period", "new_expires_at > new_starts_at");
            table.HasCheckConstraint("ck_license_changes_new_max_branches", "new_max_branches BETWEEN 1 AND 5");
            table.HasCheckConstraint("ck_license_changes_previous_values",
                "(kind = 'created' AND previous_status IS NULL AND previous_starts_at IS NULL AND previous_expires_at IS NULL AND previous_max_branches IS NULL) OR "
                + "(kind <> 'created' AND previous_status IS NOT NULL AND previous_starts_at IS NOT NULL AND previous_expires_at IS NOT NULL AND previous_max_branches IS NOT NULL AND previous_max_branches BETWEEN 1 AND 5 AND previous_expires_at > previous_starts_at)");
            table.HasCheckConstraint("ck_license_changes_actor", "actor_id <> '00000000-0000-0000-0000-000000000000'::uuid");
        });

        builder.HasKey(change => change.Id);
        builder.Property(change => change.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(change => change.TenantId).HasColumnName("tenant_id");
        builder.Property(change => change.LicenseId).HasColumnName("license_id");
        builder.Property(change => change.ActorId).HasColumnName("actor_id");
        builder.Property(change => change.OccurredAt).HasColumnName("occurred_at").HasColumnType("timestamp with time zone");
        builder.Property(change => change.Kind).HasColumnName("kind")
            .HasConversion(kind => LicenseCodes.KindToCode(kind), code => LicenseCodes.KindFromCode(code));
        builder.Property(change => change.PreviousStatus).HasColumnName("previous_status")
            .HasConversion(status => LicenseCodes.StatusToCode(status!.Value), code => LicenseCodes.StatusFromCode(code));
        builder.Property(change => change.NewStatus).HasColumnName("new_status")
            .HasConversion(status => LicenseCodes.StatusToCode(status), code => LicenseCodes.StatusFromCode(code));
        builder.Property(change => change.PreviousStartsAt).HasColumnName("previous_starts_at").HasColumnType("timestamp with time zone");
        builder.Property(change => change.PreviousExpiresAt).HasColumnName("previous_expires_at").HasColumnType("timestamp with time zone");
        builder.Property(change => change.NewStartsAt).HasColumnName("new_starts_at").HasColumnType("timestamp with time zone");
        builder.Property(change => change.NewExpiresAt).HasColumnName("new_expires_at").HasColumnType("timestamp with time zone");
        builder.Property(change => change.PreviousMaxBranches).HasColumnName("previous_max_branches");
        builder.Property(change => change.NewMaxBranches).HasColumnName("new_max_branches");
        builder.HasIndex(change => new { change.TenantId, change.LicenseId, change.OccurredAt });
    }
}
