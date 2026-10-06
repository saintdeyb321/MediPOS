using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.IdentityAccess.Persistence;

internal sealed class OperationalAccessReader(MediPosDbContext context) : IOperationalAccessReader
{
    public async Task<OperationalAccessSnapshot> ReadAsync(
        Guid userId, Guid tenantId, Guid? branchId, CancellationToken cancellationToken)
    {
        // One SQL statement gives membership, license, assignment and schedule one PostgreSQL statement snapshot.
        // Active membership wins; otherwise return the latest inactive one for an explicit denial.
        var query =
            from user in context.Users
            where user.Id == userId
            from membership in context.Memberships.Where(value => value.TenantId == tenantId && value.UserId == user.Id)
                .OrderByDescending(value => value.IsActive).ThenByDescending(value => value.CreatedAt).ThenByDescending(value => value.Id)
                .Take(1).DefaultIfEmpty()
            from license in context.Licenses.Where(value => value.TenantId == tenantId).DefaultIfEmpty()
            from schedule in context.WorkSchedules.Where(value =>
                membership != null && value.TenantId == tenantId && value.MembershipId == membership.Id).DefaultIfEmpty()
            select new
            {
                Membership = membership,
                License = license,
                Schedule = schedule,
                BranchExists = context.Branches.Any(value => value.TenantId == tenantId && value.Id == branchId),
                Assigned = context.MembershipBranches.Any(value =>
                    membership != null && value.TenantId == tenantId && value.MembershipId == membership.Id && value.BranchId == branchId),
            };

        var rows = await query.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0)
            return new OperationalAccessSnapshot(false, null, null, false, false, []);
        var first = rows[0];
        return new OperationalAccessSnapshot(true, first.Membership, first.License, first.BranchExists, first.Assigned,
            rows.Where(value => value.Schedule != null).Select(value => value.Schedule!).ToArray());
    }
}
