using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.IdentityAccess.Persistence;

internal sealed class IdentityAccessStore(MediPosDbContext context) : IIdentityAccessStore
{
    public async Task<User> UpsertGoogleUserAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        // PostgreSQL arbitrates concurrent first sign-ins; the existing ID and creation time survive.
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO users (id, google_subject, email, display_name, created_at)
            VALUES ({user.Id}, {user.GoogleSubject}, {user.Email}, {user.DisplayName}, {user.CreatedAt})
            ON CONFLICT (google_subject) DO UPDATE
            SET email = EXCLUDED.email, display_name = EXCLUDED.display_name
            """, cancellationToken).ConfigureAwait(false);
        return await context.Users.AsNoTracking().SingleAsync(
            value => value.GoogleSubject == user.GoogleSubject, cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken) =>
        context.Users.AnyAsync(value => value.Id == userId, cancellationToken);

    public Task<bool> HasActiveMembershipAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        return context.Memberships.AnyAsync(value => value.TenantId == tenantId && value.UserId == userId && value.IsActive, cancellationToken);
    }

    public Task<int> CountActiveOwnersAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        return context.Memberships.CountAsync(value => value.TenantId == tenantId && value.IsActive && value.Role == TenantRole.Owner, cancellationToken);
    }

    public Task<Membership?> FindMembershipAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        return context.Memberships.AsNoTracking().SingleOrDefaultAsync(
            value => value.TenantId == tenantId && value.Id == membershipId, cancellationToken);
    }

    public async Task AddMembershipAsync(Membership membership, AuditLog audit, CancellationToken cancellationToken)
    {
        context.SelectTenant(membership.TenantId);
        RequireProvisioningTransaction();
        context.AddAudit(audit, membership.TenantId, AuditAction.MembershipCreated, membership.Id);
        context.Memberships.Add(membership);
        await context.SaveAuditedChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveDeactivationAsync(Membership membership, AuditLog audit, CancellationToken cancellationToken)
    {
        context.SelectTenant(membership.TenantId);
        RequireProvisioningTransaction();
        context.ValidateAudit(audit, membership.TenantId, AuditAction.MembershipDeactivated, membership.Id);
        var updated = await context.Memberships.Where(value => value.TenantId == membership.TenantId && value.Id == membership.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.IsActive, membership.IsActive)
                .SetProperty(value => value.DeactivatedAt, membership.DeactivatedAt), cancellationToken).ConfigureAwait(false);
        if (updated != 1)
            throw new ApplicationErrorException(ApplicationErrors.MembershipNotFound);
        context.AddAudit(audit, membership.TenantId, AuditAction.MembershipDeactivated, membership.Id);
        await context.SaveAuditedChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ReplaceBranchesAsync(
        Guid tenantId, Guid membershipId, IReadOnlyList<MembershipBranch> branches, AuditLog audit, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        if (branches.Any(value => value.TenantId != tenantId || value.MembershipId != membershipId))
            throw new InvalidOperationException("Assignments must belong to the selected tenant and membership.");
        context.ValidateAudit(audit, tenantId, AuditAction.MembershipBranchesReplaced, membershipId);
        RequireProvisioningTransaction();
        await context.MembershipBranches.Where(value => value.TenantId == tenantId && value.MembershipId == membershipId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        context.MembershipBranches.AddRange(branches);
        context.AddAudit(audit, tenantId, AuditAction.MembershipBranchesReplaced, membershipId);
        await context.SaveAuditedChangesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var assignment in branches)
            context.Entry(assignment).State = EntityState.Detached;
    }

    public async Task ReplaceScheduleAsync(
        Guid tenantId, Guid membershipId, IReadOnlyList<WorkSchedule> schedule, AuditLog audit, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        if (schedule.Any(value => value.TenantId != tenantId || value.MembershipId != membershipId))
            throw new InvalidOperationException("Work windows must belong to the selected tenant and membership.");
        context.ValidateAudit(audit, tenantId, AuditAction.MembershipScheduleReplaced, membershipId);
        RequireProvisioningTransaction();
        await context.WorkSchedules.Where(value => value.TenantId == tenantId && value.MembershipId == membershipId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        context.WorkSchedules.AddRange(schedule);
        context.AddAudit(audit, tenantId, AuditAction.MembershipScheduleReplaced, membershipId);
        await context.SaveAuditedChangesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var window in schedule)
            context.Entry(window).State = EntityState.Detached;
    }

    private void RequireProvisioningTransaction()
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Membership changes require a tenant license provisioning scope.");
    }

    public async Task<IReadOnlyList<Guid>> FindBranchAssignmentsAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        return await context.MembershipBranches.Where(value => value.TenantId == tenantId && value.MembershipId == membershipId)
            .Select(value => value.BranchId).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WorkSchedule>> FindWorkScheduleAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        return await context.WorkSchedules.AsNoTracking().Where(value => value.TenantId == tenantId && value.MembershipId == membershipId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}
