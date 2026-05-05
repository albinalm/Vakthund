using Vakthund.Shared.Models;
using Vakthund.UI.Models;

namespace Vakthund.UI.Services;

public class MetricsService
{
    private const int ChartWindowMinutes = 10;
    private static readonly TimeSpan RecentWindow = TimeSpan.FromMinutes(1);

    public DashboardMetrics Compute(
        MetricsSnapshot snapshot,
        int retainedRequestCount,
        AuditEntry? latestRequest)
    {
        DateTimeOffset anchor = snapshot.LatestTimestamp ?? DateTimeOffset.UtcNow;
        DateTime latestMinute = MetricsStore.MinuteBucket(anchor);
        DateTime minuteCutoff = latestMinute.AddMinutes(-ChartWindowMinutes + 1);
        DateTime secondCutoff = MetricsStore.SecondBucket(anchor - RecentWindow);
        DateTime latestSecond = MetricsStore.SecondBucket(anchor);

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
            double elapsedSeconds = (recentSecondBuckets.Last().Key - recentSecondBuckets.First().Key).TotalSeconds + 1;
            requestsPerMin = (int)Math.Round(recentTotal / elapsedSeconds * 60.0);
        }

        Dictionary<int, int> statusCounts = [];
        foreach (MetricsBucketSnapshot bucket in buckets)
        {
            foreach ((int statusCode, int count) in bucket.StatusCounts)
            {
                statusCounts[statusCode] = statusCounts.GetValueOrDefault(statusCode) + count;
            }
        }

        Dictionary<DateTime, MetricsBucketSnapshot> bucketMap = buckets.ToDictionary(bucket => bucket.Start);
        List<TimePoint> requestsOverTime = windowRequests == 0
            ? []
            : Enumerable.Range(0, ChartWindowMinutes)
                .Select(offset => minuteCutoff.AddMinutes(offset))
                .Select(minute => new TimePoint
                {
                    Time = minute.ToString("HH:mm"),
                    Value = bucketMap.GetValueOrDefault(minute)?.Count ?? 0
                })
                .ToList();

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
