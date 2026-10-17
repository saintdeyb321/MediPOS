using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.IdentityAccess.Persistence.Configurations;

internal sealed class EmployeeInvitationConfiguration : IEntityTypeConfiguration<EmployeeInvitation>
{
    public void Configure(EntityTypeBuilder<EmployeeInvitation> builder)
    {
        builder.ToTable("employee_invitations", table =>
        {
            table.HasCheckConstraint("ck_employee_invitations_hash", "octet_length(secret_hash) = 32");
            table.HasCheckConstraint("ck_employee_invitations_identifiers", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND client_request_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("ck_employee_invitations_role", "role IN ('owner','pharmacist','cashier')");
            table.HasCheckConstraint("ck_employee_invitations_lifetime", "expires_at = created_at + interval '15 minutes'");
            table.HasCheckConstraint("ck_employee_invitations_acceptance", "(accepted_user_id IS NULL AND accepted_at IS NULL) OR (accepted_user_id IS NOT NULL AND accepted_at IS NOT NULL AND accepted_at >= created_at AND accepted_at < expires_at)");
            table.HasCheckConstraint("ck_employee_invitations_state", """
                (status = 'pending_acceptance' AND accepted_user_id IS NULL AND approved_at IS NULL AND result_membership_id IS NULL AND approved_from_version IS NULL AND revoked_at IS NULL AND revoked_by_user_id IS NULL AND revocation_reason IS NULL) OR
                (status = 'accepted_pending_approval' AND accepted_user_id IS NOT NULL AND approved_at IS NULL AND result_membership_id IS NULL AND approved_from_version IS NULL AND revoked_at IS NULL AND revoked_by_user_id IS NULL AND revocation_reason IS NULL) OR
                (status = 'approved' AND accepted_user_id IS NOT NULL AND approved_at IS NOT NULL AND approved_at >= accepted_at AND approved_at < expires_at AND result_membership_id IS NOT NULL AND approved_from_version IS NOT NULL AND approved_from_version > 0 AND revoked_at IS NULL AND revoked_by_user_id IS NULL AND revocation_reason IS NULL) OR
                (status = 'revoked' AND approved_at IS NULL AND result_membership_id IS NULL AND approved_from_version IS NULL AND revoked_at IS NOT NULL AND revoked_at >= created_at AND revoked_by_user_id IS NOT NULL AND revocation_reason IS NOT NULL AND length(btrim(revocation_reason)) BETWEEN 1 AND 256)
                """);
        });
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.TenantId).HasColumnName("tenant_id");
        builder.Property(value => value.IssuerMembershipId).HasColumnName("issuer_membership_id");
        builder.Property(value => value.ClientRequestId).HasColumnName("client_request_id");
        builder.Property(value => value.Role).HasColumnName("role").HasMaxLength(16).HasConversion(value => TenantRoleCodes.ToCode(value), value => TenantRoleCodes.FromCode(value));
        builder.Property(value => value.SecretHash).HasColumnName("secret_hash").IsRequired();
        builder.Property(value => value.Status).HasColumnName("status").HasMaxLength(32).HasConversion(value => EmployeeInvitationCodes.ToCode(value), value => EmployeeInvitationCodes.FromCode(value));
        builder.Property(value => value.CreatedAt).HasColumnName("created_at");
        builder.Property(value => value.ExpiresAt).HasColumnName("expires_at");
        builder.Property(value => value.AcceptedUserId).HasColumnName("accepted_user_id");
        builder.Property(value => value.AcceptedAt).HasColumnName("accepted_at");
        builder.Property(value => value.ApprovedAt).HasColumnName("approved_at");
        builder.Property(value => value.ResultMembershipId).HasColumnName("result_membership_id");
        builder.Property(value => value.ApprovedFromVersion).HasColumnName("approved_from_version");
        builder.Property(value => value.RevokedAt).HasColumnName("revoked_at");
        builder.Property(value => value.RevokedByUserId).HasColumnName("revoked_by_user_id");
        builder.Property(value => value.RevocationReason).HasColumnName("revocation_reason").HasMaxLength(256);
        builder.Property<uint>("Version").IsRowVersion().HasColumnName("xmin");
        builder.HasOne<Tenant>().WithMany().HasForeignKey(value => value.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Membership>().WithMany().HasForeignKey(value => new { value.TenantId, value.IssuerMembershipId }).HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Membership>().WithMany().HasForeignKey(value => new { value.TenantId, value.ResultMembershipId }).HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(value => value.AcceptedUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(value => value.RevokedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.IssuerMembershipId, value.ClientRequestId }).IsUnique().HasDatabaseName("ux_employee_invitations_intention");
        builder.HasIndex(value => new { value.TenantId, value.CreatedAt, value.Id });
    }
}
