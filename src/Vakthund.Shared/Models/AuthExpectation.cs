namespace Vakthund.Shared.Models;

public class AuthExpectation
{
    public bool Enforced { get; set; }
    public string? Issuer { get; set; }
    public string? Audience { get; set; }
    public List<string> Audiences { get; set; } = [];
    public List<string> Scopes { get; set; } = [];
    public List<string> Roles { get; set; } = [];
    public string? OpenIdConfigurationUrl { get; set; }
    public string? JwksUrl { get; set; }
    public JweDecryptionConfig Jwe { get; set; } = new();
}
