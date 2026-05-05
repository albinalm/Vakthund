using Vakthund.Shared.Models;
using Vakthund.UI.Models;

namespace Vakthund.UI.Services;

public class MetricsService
{
    private const int AggregateWindowMinutes = 60;
    private const int RequestChartWindowSeconds = 60;
    private static readonly TimeSpan RecentWindow = TimeSpan.FromMinutes(1);

    public DashboardMetrics Compute(
        MetricsSnapshot snapshot,
        int retainedRequestCount,
        AuditEntry? latestRequest,
        IReadOnlyDictionary<int, int>? retainedStatusCounts = null,
        DateTimeOffset? now = null)
    {
        DateTimeOffset anchor = now ?? DateTimeOffset.UtcNow;
        DateTimeOffset aggregateAnchor = snapshot.LatestTimestamp ?? anchor;
        DateTime latestMinute = MetricsStore.MinuteBucket(aggregateAnchor);
        DateTime minuteCutoff = latestMinute.AddMinutes(-AggregateWindowMinutes + 1);
        DateTime latestSecond = MetricsStore.SecondBucket(anchor);
        DateTime secondCutoff = latestSecond.AddSeconds(-(int)RecentWindow.TotalSeconds + 1);
        DateTime requestChartCutoff = latestSecond.AddSeconds(-RequestChartWindowSeconds + 1);

        MetricsBucketSnapshot[] buckets = snapshot.MinuteBuckets
            .Where(bucket => bucket.Start >= minuteCutoff && bucket.Start <= latestMinute)
            .OrderBy(bucket => bucket.Start)
            .ToArray();

        int windowRequests = buckets.Sum(bucket => bucket.Count);
        int lostAuditEntries = buckets.Sum(bucket => bucket.LostCount);
        long windowDurationMs = buckets.Sum(bucket => bucket.DurationSumMs);
        long windowTargetDurationMs = buckets.Sum(bucket => bucket.TargetDurationSumMs);
        int windowTargetCount = buckets.Sum(bucket => bucket.TargetCount);
        int errors = buckets.Sum(bucket => bucket.ErrorCount);
        KeyValuePair<DateTime, int>[] recentSecondBuckets = snapshot.SecondBuckets
            .Where(bucket => bucket.Key > secondCutoff && bucket.Key <= latestSecond)
            .OrderBy(bucket => bucket.Key)
            .ToArray();

        int requestsPerMin = 0;
        if (recentSecondBuckets.Length > 0)
        {
            int recentTotal = recentSecondBuckets.Sum(bucket => bucket.Value);
            double elapsedSeconds = Math.Min(
                RecentWindow.TotalSeconds,
                (latestSecond - recentSecondBuckets.First().Key).TotalSeconds + 1);
            requestsPerMin = (int)Math.Round(recentTotal / elapsedSeconds * 60.0);
        }

        Dictionary<int, int> aggregateStatusCounts = [];
        foreach (MetricsBucketSnapshot bucket in buckets)
        {
            foreach ((int statusCode, int count) in bucket.StatusCounts)
            {
                aggregateStatusCounts[statusCode] = aggregateStatusCounts.GetValueOrDefault(statusCode) + count;
            }
        }

        bool hasMetrics = snapshot.MinuteBuckets.Count > 0 || snapshot.SecondBuckets.Count > 0;
        Dictionary<DateTime, int> secondBucketMap = recentSecondBuckets.ToDictionary(bucket => bucket.Key, bucket => bucket.Value);
        List<TimePoint> requestsOverTime = !hasMetrics
            ? []
            : Enumerable.Range(0, RequestChartWindowSeconds)
                .Select(offset => requestChartCutoff.AddSeconds(offset))
                .Select(second => new TimePoint
                {
                    Time = second.ToString("HH:mm:ss"),
                    Value = secondBucketMap.GetValueOrDefault(second)
                })
                .ToList();

        IReadOnlyDictionary<int, int> statusCounts = retainedStatusCounts ?? aggregateStatusCounts;
        List<TimePoint> responseTimeOverTime = buckets
            .Where(bucket => bucket.Count > 0)
            .Select(bucket => new TimePoint { Time = bucket.Start.ToString("HH:mm"), Value = bucket.DurationSumMs / (double)bucket.Count })
            .ToList();

        List<StatusGroup> statusDistribution = statusCounts
            .OrderBy(k => k.Key)
            .Select(k => new StatusGroup { Label = k.Key.ToString(), Count = k.Value })
            .ToList();

        return new DashboardMetrics
        {
            RequestsOverTime = requestsOverTime,
            ResponseTimeOverTime = responseTimeOverTime,
            StatusDistribution = statusDistribution,
            TotalRequests = retainedRequestCount,
            StatusRequestCount = statusCounts.Values.Sum(),
            WindowRequests = windowRequests,
            LostAuditEntries = lostAuditEntries,
            RequestsPerMin = requestsPerMin,
            AvgResponseTime = windowRequests != 0 ? windowDurationMs / (double)windowRequests : 0,
            AvgTargetResponseTime = windowTargetCount != 0 ? windowTargetDurationMs / (double)windowTargetCount : 0,
            ErrorRate = windowRequests != 0 ? errors * 100.0 / windowRequests : 0,
            LatestRequest = latestRequest
        };
    }

}
