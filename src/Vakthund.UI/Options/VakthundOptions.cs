namespace Vakthund.UI.Options;

public class VakthundOptions
{
    public int MaxAuditEntries { get; set; } = 50_000;
    public JweOptions Jwe { get; set; } = new();
}
