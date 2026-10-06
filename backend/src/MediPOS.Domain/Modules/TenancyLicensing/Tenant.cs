namespace MediPOS.Domain.Modules.TenancyLicensing;

public sealed class Tenant
{
    private Tenant()
    {
    }

    public Guid Id { get; private set; }
    public string TradingName { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public License License { get; private set; } = null!;

    public static Tenant Create(
        string tradingName,
        DateTimeOffset startsAt,
        DateTimeOffset expiresAt,
        int maxBranches,
        LicenseStatus status,
        Guid actorId,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tradingName);

        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            TradingName = tradingName.Trim(),
            CreatedAt = createdAt.ToUniversalTime(),
        };

        tenant.License = License.Create(tenant.Id, startsAt, expiresAt, maxBranches, status, actorId, createdAt);

        return tenant;
    }
}
