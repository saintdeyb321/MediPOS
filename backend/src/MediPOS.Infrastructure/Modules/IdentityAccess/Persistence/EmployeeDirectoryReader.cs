using MediPOS.Application.Modules.IdentityAccess.Directory;
using MediPOS.Application.Modules.IdentityAccess.ReplaceWorkSchedule;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.IdentityAccess.Persistence;

internal sealed class EmployeeDirectoryReader(MediPosDbContext context) : IEmployeeDirectoryReader
{
    public async Task<IReadOnlyList<EmployeeSummary>> ReadAsync(Guid tenantId, int offset, int limit, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        var members = await context.Memberships.AsNoTracking().OrderBy(value => value.Id).Skip(offset).Take(limit).ToListAsync(token).ConfigureAwait(false);
        var memberIds = members.Select(value => value.Id).ToArray(); var userIds = members.Select(value => value.UserId).ToArray();
        var names = await context.Users.AsNoTracking().Where(value => userIds.Contains(value.Id)).Select(value => new { value.Id, value.DisplayName }).ToDictionaryAsync(value => value.Id, value => value.DisplayName, token).ConfigureAwait(false);
        var branches = await context.MembershipBranches.AsNoTracking().Where(value => memberIds.Contains(value.MembershipId)).ToListAsync(token).ConfigureAwait(false);
        var schedules = await context.WorkSchedules.AsNoTracking().Where(value => memberIds.Contains(value.MembershipId)).ToListAsync(token).ConfigureAwait(false);
        return members.Select(value => new EmployeeSummary(value.Id, value.UserId, names[value.UserId], value.Role, value.IsActive, value.CreatedAt,
            branches.Where(branch => branch.MembershipId == value.Id).Select(branch => branch.BranchId).Order().ToArray(),
            schedules.Where(window => window.MembershipId == value.Id).OrderBy(window => window.DayOfWeek).ThenBy(window => window.StartTime)
                .Select(window => new WorkWindow(window.DayOfWeek, window.StartTime, window.EndTime)).ToArray())).ToArray();
    }
}
