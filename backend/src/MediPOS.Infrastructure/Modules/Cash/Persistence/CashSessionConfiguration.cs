using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.Cash.Persistence;

internal sealed class CashSessionConfiguration : IEntityTypeConfiguration<CashSession>
{
    internal const string OpenSessionIndex = "ux_cash_sessions_tenant_branch_membership_open";

    public void Configure(EntityTypeBuilder<CashSession> builder)
    {
        builder.ToTable("cash_sessions", table =>
        {
            table.HasCheckConstraint("ck_cash_sessions_status", "status IN ('open', 'closed')");
            table.HasCheckConstraint("ck_cash_sessions_opening_amount", "opening_amount >= 0 AND opening_amount <= 99999999999999.9999");
            table.HasCheckConstraint("ck_cash_sessions_identifiers", """
                id <> '00000000-0000-0000-0000-000000000000'::uuid AND
                tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid AND
                branch_id <> '00000000-0000-0000-0000-000000000000'::uuid AND
                membership_id <> '00000000-0000-0000-0000-000000000000'::uuid AND
                opened_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid
                """);
        });
        builder.HasKey(value => value.Id);
        builder.HasAlternateKey(value => new { value.TenantId, value.BranchId, value.MembershipId, value.Id });
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.TenantId).HasColumnName("tenant_id");
        builder.Property(value => value.BranchId).HasColumnName("branch_id");
        builder.Property(value => value.MembershipId).HasColumnName("membership_id");
        builder.Property(value => value.OpeningAmount).HasColumnName("opening_amount").HasPrecision(18, 4);
        builder.Property(value => value.Status).HasColumnName("status").HasMaxLength(16)
            .HasConversion(value => CashSessionStatusCodes.ToCode(value), value => CashSessionStatusCodes.FromCode(value));
        builder.Property(value => value.OpenedAt).HasColumnName("opened_at").HasColumnType("timestamp with time zone");
        builder.Property(value => value.OpenedByActorId).HasColumnName("opened_by_actor_id");
        builder.HasOne<Tenant>().WithMany().HasForeignKey(value => value.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany().HasForeignKey(value => new { value.TenantId, value.BranchId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Membership>().WithMany().HasForeignKey(value => new { value.TenantId, value.MembershipId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.BranchId, value.Status });
        builder.HasIndex(value => new { value.TenantId, value.MembershipId, value.Status });
        builder.HasIndex(value => new { value.TenantId, value.BranchId, value.MembershipId })
            .IsUnique().HasFilter("status = 'open'").HasDatabaseName(OpenSessionIndex);
    }
}
