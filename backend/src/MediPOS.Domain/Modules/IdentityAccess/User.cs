namespace MediPOS.Domain.Modules.IdentityAccess;

public sealed class User
{
    private User() { }

    public Guid Id { get; private set; }
    public string GoogleSubject { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public bool GoogleEmailVerified { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public static User Create(string googleSubject, string email, string displayName, DateTimeOffset createdAt, bool googleEmailVerified = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(googleSubject);
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            GoogleSubject = googleSubject.Trim(),
            CreatedAt = createdAt.ToUniversalTime(),
        };
        user.UpdateGoogleProfile(email, displayName, googleEmailVerified);
        return user;
    }

    public void UpdateGoogleProfile(string email, string displayName, bool googleEmailVerified = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        Email = email.Trim();
        GoogleEmailVerified = googleEmailVerified;
        DisplayName = displayName.Trim();
    }
}
