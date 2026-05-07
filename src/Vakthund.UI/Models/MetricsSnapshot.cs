namespace Vakthund.UI.Models;

public sealed record MetricsSnapshot(
    DateTimeOffset? LatestTimestamp,
    IReadOnlyList<MetricsBucketSnapshot> MinuteBuckets,
    IReadOnlyDictionary<DateTime, int> SecondBuckets);
