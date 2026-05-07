using Microsoft.IdentityModel.Tokens;

namespace Vakthund.Proxy.Models;

internal sealed record JwksSigningKeyCacheEntry(IReadOnlyCollection<SecurityKey> Keys, DateTimeOffset ExpiresAt);
