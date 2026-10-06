namespace MediPOS.Domain.Modules.IdentityAccess;

public sealed class User
{
    private User() { }

    public Guid Id { get; private set; }
    public string GoogleSubject { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public static User Create(string googleSubject, string email, string displayName, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(googleSubject);
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            GoogleSubject = googleSubject.Trim(),
            CreatedAt = createdAt.ToUniversalTime(),
        };
        user.UpdateGoogleProfile(email, displayName);
        return user;
    }

    public void UpdateGoogleProfile(string email, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        Email = email.Trim();
        DisplayName = displayName.Trim();
    }
}
