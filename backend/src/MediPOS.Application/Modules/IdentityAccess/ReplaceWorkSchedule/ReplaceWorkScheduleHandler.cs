using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.Application.Modules.IdentityAccess.ReplaceWorkSchedule;

public sealed record WorkWindow(DayOfWeek DayOfWeek, TimeOnly StartTime, TimeOnly EndTime);
// ActorId is supplied by the authenticated server caller, never a freely bound HTTP field.
public sealed record ReplaceWorkScheduleCommand(Guid TenantId, Guid MembershipId, IReadOnlyList<WorkWindow> Windows, Guid ActorId);

public sealed class ReplaceWorkScheduleHandler(IIdentityAccessStore store, ITenantLicenseProvisioning provisioning, TimeProvider timeProvider)
{
    public async Task HandleAsync(ReplaceWorkScheduleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        AuditTrail.RequireActor(command.ActorId);
        ArgumentNullException.ThrowIfNull(command.Windows);
        var requested = command.Windows.ToArray();
        if (requested.Distinct().Count() != requested.Length)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await using var scope = await provisioning.BeginAsync(command.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.LicenseNotFound);
        if (scope.TenantId != command.TenantId)
            throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        var membership = await store.FindMembershipAsync(command.TenantId, command.MembershipId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.MembershipNotFound);
        if (!membership.IsActive)
            throw new ApplicationErrorException(ApplicationErrors.MembershipInactive);
        WorkSchedule[] schedule;
        try
        {
            schedule = requested.Select(window => WorkSchedule.Create(command.TenantId, membership.Id, membership.TenantId,
                window.DayOfWeek, window.StartTime, window.EndTime)).ToArray();
        }
        catch (ArgumentException) { throw new ApplicationErrorException(ApplicationErrors.InvalidRequest); }
        var existing = await store.FindWorkScheduleAsync(command.TenantId, membership.Id, cancellationToken).ConfigureAwait(false);
        var before = AuditTrail.WorkSchedule(existing);
        var after = AuditTrail.WorkSchedule(schedule);
        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            var audit = AuditTrail.Record(command.TenantId, command.ActorId, AuditAction.MembershipScheduleReplaced, membership.Id,
                timeProvider.GetUtcNow(), before, after);
            await store.ReplaceScheduleAsync(command.TenantId, membership.Id, schedule, audit, cancellationToken).ConfigureAwait(false);
        }
        await scope.CompleteAsync(cancellationToken).ConfigureAwait(false);
    }
}
