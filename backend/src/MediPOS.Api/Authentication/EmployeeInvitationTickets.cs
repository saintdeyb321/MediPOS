using System.Security.Cryptography;
using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.Invitations;
using Microsoft.AspNetCore.DataProtection;

namespace MediPOS.Api.Authentication;

public sealed class EmployeeInvitationTickets(IDataProtectionProvider protection, TimeProvider clock)
{
    private readonly IDataProtector _protector = protection.CreateProtector("MediPOS.EmployeeInvitation.v1");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record Payload(Guid TenantId, Guid InvitationId, byte[] Secret, DateTimeOffset ExpiresAt);

    public string Protect(Guid tenantId, Guid invitationId, byte[] secret, DateTimeOffset expiresAt)
    {
        if (secret is not { Length: 32 } || tenantId == Guid.Empty || invitationId == Guid.Empty || expiresAt <= clock.GetUtcNow())
            throw new InvalidOperationException("A live server invitation is required.");
        return _protector.Protect(JsonSerializer.Serialize(new Payload(tenantId, invitationId, secret, expiresAt), Json));
    }

    public AcceptEmployeeInvitationCommand Read(string ticket)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(ticket) || ticket.Length > 4096) throw new CryptographicException();
            var payload = JsonSerializer.Deserialize<Payload>(_protector.Unprotect(ticket), Json) ?? throw new CryptographicException();
            if (payload.Secret is not { Length: 32 } || payload.TenantId == Guid.Empty || payload.InvitationId == Guid.Empty || payload.ExpiresAt <= clock.GetUtcNow())
                throw new CryptographicException();
            try { return new(payload.TenantId, payload.InvitationId, SHA256.HashData(payload.Secret)); }
            finally { CryptographicOperations.ZeroMemory(payload.Secret); }
        }
        catch (Exception error) when (error is CryptographicException or JsonException or ArgumentException or FormatException)
        { throw new ApplicationErrorException(InvitationErrors.Unavailable); }
    }
}
