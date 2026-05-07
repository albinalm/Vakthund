namespace Vakthund.Shared.Models;

public class AuditBatch
{
    public List<AuditEntry> Entries { get; set; } = [];
    public AuditLossSummary? Loss { get; set; }
}
