namespace MediPOS.Domain.Modules.Notifications;

public sealed class NotificationReadState
{
    private NotificationReadState() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid NotificationId { get; private set; }
    public Guid MembershipId { get; private set; }
    public DateTimeOffset ReadAt { get; private set; }

    public static NotificationReadState Read(Guid tenantId, Guid notificationId, Guid membershipId, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty || notificationId == Guid.Empty || membershipId == Guid.Empty)
            throw new ArgumentException("Read state references are required.");
        return new NotificationReadState
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            NotificationId = notificationId,
            MembershipId = membershipId,
            ReadAt = now.ToUniversalTime(),
        };
    }
}
