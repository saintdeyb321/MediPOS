namespace MediPOS.Domain.Modules.Branches;

public sealed class LegalEntity
{
    private LegalEntity()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string LegalName { get; private set; } = string.Empty;
    public string Ruc { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public static LegalEntity Create(Guid tenantId, string legalName, string ruc, DateTimeOffset createdAt)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant identifier is required.", nameof(tenantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(legalName);
        ArgumentException.ThrowIfNullOrWhiteSpace(ruc);

        return new LegalEntity
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            LegalName = legalName.Trim(),
            Ruc = ruc.Trim(),
            CreatedAt = createdAt.ToUniversalTime(),
        };
    }
}
