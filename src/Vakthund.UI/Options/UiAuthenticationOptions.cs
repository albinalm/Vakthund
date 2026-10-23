namespace Vakthund.UI.Options;

public sealed class UiAuthenticationOptions
{
    public bool Enabled { get; set; }
    public string Authority { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string ClientSecretFile { get; set; } = string.Empty;
    public string[] AllowedSubjects { get; set; } = [];
    public int SessionMinutes { get; set; } = 480;
    public void Validate()
    {
        if (!Enabled)
            return;
        if (!Uri.TryCreate(Authority, UriKind.Absolute, out Uri? authority) || authority.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(authority.UserInfo) || !string.IsNullOrEmpty(authority.Query) || !string.IsNullOrEmpty(authority.Fragment))
            throw new InvalidOperationException("UI:Authentication:Authority must be an absolute HTTPS issuer URL without credentials, query, or fragment.");
        if (string.IsNullOrWhiteSpace(ClientId))
            throw new InvalidOperationException("UI:Authentication:ClientId is required when authentication is enabled.");
        if (AllowedSubjects is not { Length: > 0 } || AllowedSubjects.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("UI:Authentication:AllowedSubjects must explicitly list at least one non-empty account subject.");
        if (SessionMinutes is < 1 or > 1440)
            throw new InvalidOperationException("UI:Authentication:SessionMinutes must be between 1 and 1440.");
        if (!string.IsNullOrEmpty(ClientSecret) && !string.IsNullOrEmpty(ClientSecretFile))
            throw new InvalidOperationException("Configure either UI:Authentication:ClientSecret or ClientSecretFile, not both.");
    }
}
