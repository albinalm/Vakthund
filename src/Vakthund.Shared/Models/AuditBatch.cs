namespace Vakthund.Shared.Models;

public class AuditBatch
{
    public List<AuditEntry> Entries { get; set; } = [];
    public AuditLossSummary? Loss { get; set; }
}

public class AuditLossSummary
{
    public int Count { get; set; }
    public List<AuditLossBucket> MinuteBuckets { get; set; } = [];
    public List<AuditLossSecondBucket> SecondBuckets { get; set; } = [];
}

public class AuditLossBucket
{
    public DateTimeOffset Start { get; set; }
    public int Count { get; set; }
    public long DurationSumMs { get; set; }
    public long TargetDurationSumMs { get; set; }
    public int TargetCount { get; set; }
    public int ErrorCount { get; set; }
    public Dictionary<int, int> StatusCounts { get; set; } = [];
}

public class AuditLossSecondBucket
{
    public DateTimeOffset Start { get; set; }
    public int Count { get; set; }
}
