using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.IdentityAccess.Persistence.Configurations;

internal sealed class InitialOwnerInvitationConfiguration : IEntityTypeConfiguration<InitialOwnerInvitation>
{
    public void Configure(EntityTypeBuilder<InitialOwnerInvitation> builder)
    {
        builder.ToTable("initial_owner_invitations", table =>
        {
            table.HasCheckConstraint("ck_initial_owner_email", "length(normalized_email) BETWEEN 3 AND 320 AND normalized_email = lower(btrim(normalized_email)) AND normalized_email LIKE '%@%'");
            table.HasCheckConstraint("ck_initial_owner_state", "(status = 'pending' AND claimed_user_id IS NULL AND membership_id IS NULL AND claimed_at IS NULL AND revoked_by IS NULL AND revoked_at IS NULL) OR (status = 'claimed' AND claimed_user_id IS NOT NULL AND membership_id IS NOT NULL AND claimed_at IS NOT NULL AND claimed_at >= created_at AND revoked_by IS NULL AND revoked_at IS NULL) OR (status = 'revoked' AND claimed_user_id IS NULL AND membership_id IS NULL AND claimed_at IS NULL AND revoked_by IS NOT NULL AND revoked_at IS NOT NULL AND revoked_at >= created_at)");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(row => row.TenantId).HasColumnName("tenant_id");
        builder.Property(row => row.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(320);
        builder.Property(row => row.Status).HasColumnName("status").HasMaxLength(16).IsConcurrencyToken();
        builder.Property(row => row.InvitedBy).HasColumnName("invited_by");
        builder.Property(row => row.CreationRequestKey).HasColumnName("creation_request_key");
        builder.Property(row => row.CreatedAt).HasColumnName("created_at");
        builder.Property(row => row.ClaimedUserId).HasColumnName("claimed_user_id");
        builder.Property(row => row.MembershipId).HasColumnName("membership_id");
        builder.Property(row => row.ClaimedAt).HasColumnName("claimed_at");
        builder.Property(row => row.RevokedBy).HasColumnName("revoked_by");
        builder.Property(row => row.RevokedAt).HasColumnName("revoked_at");
        builder.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.InvitedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.ClaimedUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.RevokedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Membership>().WithMany().HasForeignKey(row => new { row.TenantId, row.MembershipId }).HasPrincipalKey(row => new { row.TenantId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(row => row.TenantId).IsUnique().HasFilter("status IN ('pending','claimed')").HasDatabaseName("ux_initial_owner_current");
        builder.HasIndex(row => row.NormalizedEmail).HasFilter("status = 'pending'");
    }
}
