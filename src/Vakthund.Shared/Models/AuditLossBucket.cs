namespace Vakthund.Shared.Models;

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
