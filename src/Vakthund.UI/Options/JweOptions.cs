namespace Vakthund.UI.Options;

public class JweOptions
{
    public JweKeyType? KeyType { get; set; }
    public string? Key { get; set; }
}

public enum JweKeyType
{
    Rsa,
    Ec,
    Symmetric,
    Password
}
