namespace MediPOS.Domain.Modules.Catalog;

public sealed class Category
{
    private Category() { }
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Category Create(string name, DateTimeOffset createdAt, bool isActive = true) => new()
    {
        Id = Guid.CreateVersion7(),
        Name = CatalogFields.Required(name, 128, nameof(name)),
        IsActive = isActive,
        CreatedAt = createdAt.ToUniversalTime(),
    };
}
