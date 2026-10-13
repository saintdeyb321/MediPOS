using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;

namespace MediPOS.Api.Authentication;

// One bounded process-local server store. Restart requires login; logout revokes even a copied cookie.
public sealed class ServerSessionTicketStore(TimeProvider clock) : ITicketStore, IDisposable
{
    private readonly MemoryCache _tickets = new(new MemoryCacheOptions { SizeLimit = 4096 });
    public Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        Validate(ticket);
        var key = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        _tickets.Set(key, Clone(ticket), new MemoryCacheEntryOptions { AbsoluteExpiration = ticket.Properties.ExpiresUtc, Size = 1 });
        if (!_tickets.TryGetValue(key, out _)) throw new InvalidOperationException("Session capacity is unavailable.");
        return Task.FromResult(key);
    }
    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        // Sliding renewal is disabled. An existing login cannot replace another user's server ticket.
        Validate(ticket);
        if (_tickets.TryGetValue(key, out AuthenticationTicket? original) && original is not null &&
            original.Principal.Claims.Single().Value == ticket.Principal.Claims.Single().Value && original.Properties.ExpiresUtc == ticket.Properties.ExpiresUtc)
            _tickets.Set(key, Clone(ticket), new MemoryCacheEntryOptions { AbsoluteExpiration = ticket.Properties.ExpiresUtc, Size = 1 });
        else throw new InvalidOperationException("A new login requires a new server session.");
        return Task.CompletedTask;
    }
    public Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        if (_tickets.TryGetValue(key, out AuthenticationTicket? ticket) && ticket?.Properties.ExpiresUtc > clock.GetUtcNow())
            return Task.FromResult<AuthenticationTicket?>(Clone(ticket));
        _tickets.Remove(key);
        return Task.FromResult<AuthenticationTicket?>(null);
    }
    public Task RemoveAsync(string key) { _tickets.Remove(key); return Task.CompletedTask; }
    public void Dispose() => _tickets.Dispose();
    private void Validate(AuthenticationTicket ticket)
    {
        if (ticket.AuthenticationScheme != GoogleAuthenticationRegistration.SessionScheme || !SessionIdentity.TryGetUser(ticket.Principal, out _) ||
            ticket.Properties.GetTokens().Any() || ticket.Properties.ExpiresUtc is not { } expires || expires <= clock.GetUtcNow() ||
            expires > clock.GetUtcNow().AddHours(1)) throw new InvalidOperationException("Only a bounded internal MediPOS session can be stored.");
    }
    private static AuthenticationTicket Clone(AuthenticationTicket ticket) => new(ticket.Principal.Clone(),
        new AuthenticationProperties(new Dictionary<string, string?>(ticket.Properties.Items)), ticket.AuthenticationScheme);
}
