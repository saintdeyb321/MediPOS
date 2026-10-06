namespace MediPOS.Domain.Modules.Branches;

public sealed class Branch
{
    private Branch()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid LegalEntityId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public bool IsMainHub { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Branch Create(
        Guid tenantId,
        Guid legalEntityId,
        Guid legalEntityTenantId,
        string name,
        DateTimeOffset createdAt)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant identifier is required.", nameof(tenantId));
        }

        if (legalEntityId == Guid.Empty)
        {
            throw new ArgumentException("A legal entity identifier is required.", nameof(legalEntityId));
        }

        if (legalEntityTenantId != tenantId)
        {
            throw new ArgumentException("The legal entity must belong to the branch tenant.", nameof(legalEntityTenantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new Branch
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            LegalEntityId = legalEntityId,
            Name = name.Trim(),
            CreatedAt = createdAt.ToUniversalTime(),
        };
    }

    public void MarkAsMainHub() => IsMainHub = true;
    public void ClearMainHub() => IsMainHub = false;
}
