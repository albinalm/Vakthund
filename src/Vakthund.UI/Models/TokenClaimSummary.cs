namespace Vakthund.UI.Models;

public record TokenClaimSummary
{
    public string? Subject { get; init; }
    public string? Issuer { get; init; }
    public IReadOnlyList<string> Audiences { get; init; } = [];
    public IReadOnlyList<string> Scopes { get; init; } = [];
    public IReadOnlyList<string> Roles { get; init; } = [];
    public string? ClientId { get; init; }
    public string? AuthorizedParty { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset? NotBefore { get; init; }
    public DateTimeOffset? IssuedAt { get; init; }
}
