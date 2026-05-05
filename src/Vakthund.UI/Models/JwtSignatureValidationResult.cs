namespace Vakthund.UI.Models;

public record JwtSignatureValidationResult
{
    public required JwtSignatureValidationStatus Status { get; init; }
    public required string Message { get; init; }
    public JwtSigningKeySource KeySource { get; init; } = JwtSigningKeySource.None;
}
