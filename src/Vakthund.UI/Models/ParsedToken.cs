namespace Vakthund.UI.Models;

public record ParsedToken
{
    public static readonly ParsedToken Empty = new();

    public string? HeaderName { get; init; }
    public string? Scheme { get; init; }
    public string? RawToken { get; init; }
    public string? JwtHeaderJson { get; init; }
    public string? JwtPayloadJson { get; init; }
    public TokenHeaderSummary? Header { get; init; }
    public string? BasicUsername { get; init; }
    public string? BasicPassword { get; init; }
    public DateTimeOffset? JwtExpiry { get; init; }
    public bool JwtExpired { get; init; }
    public TokenClaimSummary? Claims { get; init; }
    public bool IsJwe { get; init; }
    public string? JweDecryptError { get; init; }
    public string? DecryptedRawJwt { get; init; }
}
