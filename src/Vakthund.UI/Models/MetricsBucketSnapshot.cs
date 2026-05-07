namespace Vakthund.UI.Models;

public sealed record MetricsBucketSnapshot(
    DateTime Start,
    int Count,
    long DurationSumMs,
    long TargetDurationSumMs,
    int TargetCount,
    int ErrorCount,
    int LostCount,
    IReadOnlyDictionary<int, int> StatusCounts);
