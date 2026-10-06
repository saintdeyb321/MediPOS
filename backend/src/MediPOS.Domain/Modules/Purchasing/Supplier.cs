namespace MediPOS.Domain.Modules.Purchasing;

public sealed class Supplier
{
    private Supplier() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Ruc { get; private set; }
    public string? Contact { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Supplier Create(Guid tenantId, string name, string? ruc, string? contact, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        return new Supplier
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Name = PurchasingFields.Required(name, 256),
            Ruc = PurchasingFields.Optional(ruc, 32),
            Contact = PurchasingFields.Optional(contact, 512),
            IsActive = true,
            CreatedAt = now.ToUniversalTime(),
        };
    }
}

internal static class PurchasingFields
{
    public static string Required(string? value, int maximum) =>
        Optional(value, maximum) ?? throw new ArgumentException("A value is required.");
    public static string? Optional(string? value, int maximum)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)) return null;
        if (normalized.Length > maximum) throw new ArgumentException("Value exceeds its storage limit.");
        return normalized;
    }
}
