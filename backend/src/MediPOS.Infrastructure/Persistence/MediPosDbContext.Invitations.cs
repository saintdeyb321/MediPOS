using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Persistence;

public sealed partial class MediPosDbContext
{
    private void ValidateInvitationWrites()
    {
        foreach (var entry in ChangeTracker.Entries<EmployeeInvitation>().Where(value => value.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (entry.State == EntityState.Deleted || Database.CurrentTransaction is null) throw new InvalidOperationException("Invitation history requires an explicit transaction and cannot be deleted.");
            if (entry.State == EntityState.Modified && entry.Properties.Any(property => property.IsModified && property.Metadata.Name is
                nameof(EmployeeInvitation.Id) or nameof(EmployeeInvitation.TenantId) or nameof(EmployeeInvitation.IssuerMembershipId) or nameof(EmployeeInvitation.ClientRequestId) or
                nameof(EmployeeInvitation.Role) or nameof(EmployeeInvitation.SecretHash) or nameof(EmployeeInvitation.CreatedAt) or nameof(EmployeeInvitation.ExpiresAt)))
                throw new InvalidOperationException("Invitation intent, role, secret and lifetime are immutable.");
            var action = entry.Entity.Status switch
            {
                EmployeeInvitationStatus.PendingAcceptance when entry.State == EntityState.Added => AuditAction.EmployeeInvitationCreated,
                EmployeeInvitationStatus.AcceptedPendingApproval when entry.State == EntityState.Modified => AuditAction.EmployeeInvitationAccepted,
                EmployeeInvitationStatus.Approved when entry.State == EntityState.Modified => AuditAction.EmployeeInvitationApproved,
                EmployeeInvitationStatus.Revoked when entry.State == EntityState.Modified => AuditAction.EmployeeInvitationRevoked,
                _ => throw new InvalidOperationException("Invalid invitation transition."),
            };
            if (ChangeTracker.Entries<AuditLog>().Count(value => value.State == EntityState.Added && value.Entity.Action == action &&
                value.Entity.EntityId == entry.Entity.Id && value.Entity.TenantId == entry.Entity.TenantId) != 1)
                throw new InvalidOperationException("Invitation mutation requires its single matching audit.");
        }
    }
}
