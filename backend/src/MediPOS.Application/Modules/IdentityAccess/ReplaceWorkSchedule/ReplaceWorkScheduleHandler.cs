using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.Application.Modules.IdentityAccess.ReplaceWorkSchedule;

public sealed record WorkWindow(DayOfWeek DayOfWeek, TimeOnly StartTime, TimeOnly EndTime);
public sealed record ReplaceWorkScheduleCommand(Guid TenantId, Guid MembershipId, IReadOnlyList<WorkWindow> Windows);

public sealed class ReplaceWorkScheduleHandler(IIdentityAccessStore store, ITenantLicenseProvisioning provisioning)
{
    public async Task HandleAsync(ReplaceWorkScheduleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Windows);
        var requested = command.Windows.ToArray();
        if (requested.Distinct().Count() != requested.Length)
            throw new ArgumentException("Duplicate work windows are not allowed.", nameof(command));

        await using var scope = await provisioning.BeginAsync(command.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Current tenant license was not found.");
        if (scope.TenantId != command.TenantId)
            throw new InvalidOperationException("The provisioning scope belongs to another tenant.");
        var membership = await store.FindMembershipAsync(command.TenantId, command.MembershipId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Membership was not found for the tenant.");
        if (!membership.IsActive)
            throw new InvalidOperationException("Inactive memberships retain their work schedule.");

        var schedule = requested.Select(window => WorkSchedule.Create(
            command.TenantId, membership.Id, membership.TenantId, window.DayOfWeek, window.StartTime, window.EndTime)).ToArray();
        await store.ReplaceScheduleAsync(command.TenantId, membership.Id, schedule, cancellationToken).ConfigureAwait(false);
        await scope.CompleteAsync(cancellationToken).ConfigureAwait(false);
    }
}
