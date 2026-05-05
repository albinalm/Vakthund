namespace Vakthund.UI.Models;

public enum JwtSigningKeySource
{
    None,
    ConfiguredJwks,
    ConfiguredOpenIdMetadata,
    ConfiguredIssuerMetadata,
    InferredTokenIssuerMetadata
}
