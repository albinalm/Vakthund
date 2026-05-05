namespace Vakthund.UI.Models;

public enum JwtSignatureValidationStatus
{
    NotConfigured,
    Valid,
    Invalid,
    UnknownKey,
    UnsupportedAlgorithm,
    FetchFailed,
    MalformedToken
}
