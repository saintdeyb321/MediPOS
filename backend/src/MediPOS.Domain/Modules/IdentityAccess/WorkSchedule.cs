namespace MediPOS.Domain.Modules.IdentityAccess;

public sealed class WorkSchedule
{
    private WorkSchedule() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid MembershipId { get; private set; }
    public DayOfWeek DayOfWeek { get; private set; }
    public TimeOnly StartTime { get; private set; }
    public TimeOnly EndTime { get; private set; }

    public static WorkSchedule Create(
        Guid tenantId, Guid membershipId, Guid membershipTenantId,
        DayOfWeek dayOfWeek, TimeOnly startTime, TimeOnly endTime)
    {
        if (tenantId == Guid.Empty || membershipId == Guid.Empty)
            throw new ArgumentException("Tenant and membership identifiers are required.");
        if (membershipTenantId != tenantId)
            throw new ArgumentException("The membership must belong to the schedule tenant.");
        if (!Enum.IsDefined(dayOfWeek))
            throw new ArgumentOutOfRangeException(nameof(dayOfWeek));
        if (startTime >= endTime)
            throw new ArgumentException("Schedule start must precede end; overnight windows are not supported.");
        // PostgreSQL time has microsecond precision. Reject unrepresentable boundaries instead of silently rounding.
        if (startTime.Ticks % 10 != 0 || endTime.Ticks % 10 != 0)
            throw new ArgumentException("Schedule times must have at most microsecond precision.");

        return new WorkSchedule
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            MembershipId = membershipId,
            DayOfWeek = dayOfWeek,
            StartTime = startTime,
            EndTime = endTime,
        };
    }

    public bool Contains(DayOfWeek day, TimeOnly time) => DayOfWeek == day && StartTime <= time && time < EndTime;
}
