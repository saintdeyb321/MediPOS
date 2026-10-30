using System.Net.Mail;

namespace MediPOS.Application.Modules.IdentityAccess.Authentication;

public static class GoogleEmailAddress
{
    // Compare verified addresses; never guess that dots or plus aliases identify the same account.
    public static string? Normalize(string? value)
    {
        if (value is null || value.Trim().Length is < 3 or > 320) return null;
        var trimmed = value.Trim();
        return MailAddress.TryCreate(trimmed, out var parsed) && string.Equals(parsed.Address, trimmed, StringComparison.OrdinalIgnoreCase)
            ? trimmed.ToLowerInvariant() : null;
    }
}
