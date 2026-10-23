using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;

namespace Vakthund.UI.Services;

public sealed class UiSessionStore(IMemoryCache cache) : ITicketStore
{
    public const string SessionClaim = "vakthund:session";
    public Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        string key = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        foreach (ClaimsIdentity identity in ticket.Principal.Identities)
        {
            foreach (Claim claim in identity.FindAll(SessionClaim).ToArray())
                identity.RemoveClaim(claim);
        }
        if (ticket.Principal.Identity is ClaimsIdentity primaryIdentity)
            primaryIdentity.AddClaim(new Claim(SessionClaim, key));
        Save(key, ticket);
        return Task.FromResult(key);
    }
    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        Save(key, ticket);
        return Task.CompletedTask;
    }
    public Task<AuthenticationTicket?> RetrieveAsync(string key) =>
        Task.FromResult(cache.TryGetValue(key, out AuthenticationTicket? ticket) ? ticket : null);
    public Task RemoveAsync(string key)
    {
        cache.Remove(key);
        return Task.CompletedTask;
    }
    private void Save(string key, AuthenticationTicket ticket) =>
        cache.Set(key, ticket, ticket.Properties.ExpiresUtc ?? throw new InvalidOperationException("UI sessions must have an expiration."));
}
