using System.Security.Cryptography;
using System.Text.Json;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using Microsoft.AspNetCore.DataProtection;

namespace MediPOS.Api.Authentication;

// Registration only: these links/cookies can never create a membership or platform authority.
public sealed class OwnerRegistrationLinks(IDataProtectionProvider protection, TimeProvider clock)
{
    public const string Page = "/registro-propietario";
    internal const string StateKey = ".medipos.owner-registration";
    private const string PendingCookie = "__Host-MediPOS.OwnerRegistration";
    private const string CompletedCookie = "__Host-MediPOS.OwnerRegistrationComplete";
    private readonly IDataProtector _links = protection.CreateProtector("MediPOS.OwnerRegistration.Link.v1");
    private readonly IDataProtector _completed = protection.CreateProtector("MediPOS.OwnerRegistration.Completed.v1");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public sealed record Invitation(Guid TenantId, string BusinessName, string Email, DateTimeOffset ExpiresAt);
    private sealed record Completion(Invitation Invitation, Guid UserId, string DisplayName);

    public (string Ticket, DateTimeOffset ExpiresAt) Create(Guid tenantId, string businessName, string email)
    {
        var address = GoogleEmailAddress.Normalize(email) ?? throw new ArgumentException("A bounded email address is required.");
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(businessName) || businessName.Length > 256) throw new ArgumentException("A tenant is required.");
        var expires = clock.GetUtcNow().AddMinutes(15);
        return (_links.Protect(JsonSerializer.Serialize(new Invitation(tenantId, businessName, address, expires), Json)), expires);
    }

    private Invitation? Read(string? ticket)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(ticket) || ticket.Length > 4096) return null;
            var value = JsonSerializer.Deserialize<Invitation>(_links.Unprotect(ticket), Json);
            return Valid(value) ? value : null;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or ArgumentException) { return null; }
    }
    private bool Valid(Invitation? value) => value is not null && value.TenantId != Guid.Empty && value.ExpiresAt > clock.GetUtcNow() &&
        value.ExpiresAt <= clock.GetUtcNow().AddMinutes(15) && GoogleEmailAddress.Normalize(value.Email) is { } email && email == value.Email && value.BusinessName is { Length: > 0 and <= 256 };

    public bool Prepare(HttpContext context, string ticket)
    {
        var invitation = Read(ticket);
        if (invitation is null) return false;
        context.Response.Cookies.Delete(CompletedCookie, Options(invitation.ExpiresAt));
        context.Response.Cookies.Append(PendingCookie, ticket, Options(invitation.ExpiresAt));
        return true;
    }
    public Invitation? Pending(HttpContext context) => Read(context.Request.Cookies[PendingCookie]);
    public string? PendingTicket(HttpContext context) => context.Request.Cookies[PendingCookie];
    public Invitation? FromState(string? ticket) => Read(ticket);
    public void Clear(HttpContext context)
    {
        context.Response.Cookies.Delete(PendingCookie, Options(clock.GetUtcNow()));
        context.Response.Cookies.Delete(CompletedCookie, Options(clock.GetUtcNow()));
    }

    public void Complete(HttpContext context, Invitation invitation, Guid userId, string displayName)
    {
        if (!Valid(invitation) || userId == Guid.Empty || string.IsNullOrWhiteSpace(displayName) || displayName.Length > 200) throw new InvalidOperationException("A registered Google session is required.");
        context.Response.Cookies.Delete(PendingCookie, Options(invitation.ExpiresAt));
        context.Response.Cookies.Append(CompletedCookie, _completed.Protect(JsonSerializer.Serialize(new Completion(invitation, userId, displayName), Json)), Options(invitation.ExpiresAt));
    }
    public object? Status(HttpContext context, Guid userId)
    {
        try
        {
            var cookie = context.Request.Cookies[CompletedCookie];
            if (string.IsNullOrWhiteSpace(cookie) || cookie.Length > 4096) return null;
            var value = JsonSerializer.Deserialize<Completion>(_completed.Unprotect(cookie), Json);
            return value is not null && value.UserId == userId && Valid(value.Invitation) && value.DisplayName is { Length: > 0 and <= 200 }
                ? new { registered = true, value.Invitation.BusinessName, value.Invitation.Email, value.DisplayName } : null;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or ArgumentException) { return null; }
    }
    private static CookieOptions Options(DateTimeOffset expires) => new() { Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/", Expires = expires, IsEssential = true };
}
