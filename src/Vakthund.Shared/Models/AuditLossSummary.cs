namespace Vakthund.Shared.Models;

public class AuditLossSummary
{
    public int Count { get; set; }
    public List<AuditLossBucket> MinuteBuckets { get; set; } = [];
    public List<AuditLossSecondBucket> SecondBuckets { get; set; } = [];
}
