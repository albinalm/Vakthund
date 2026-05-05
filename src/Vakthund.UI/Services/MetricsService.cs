using Vakthund.Shared.Models;
using Vakthund.UI.Models;

namespace Vakthund.UI.Services;

public class MetricsService
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    public DashboardMetrics Compute(IReadOnlyCollection<AuditEntry> all)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset cutoff = now - Window;

        var requestBuckets = new Dictionary<DateTime, int>();
        var rtBuckets = new Dictionary<DateTime, (double Sum, int Count)>();

        foreach (AuditEntry entry in all.Where(e => e.Timestamp >= cutoff))
        {
            DateTime bucket = BucketFor(entry.Timestamp);
            requestBuckets[bucket] = requestBuckets.GetValueOrDefault(bucket) + 1;
            (double sum, int count) = rtBuckets.GetValueOrDefault(bucket);
            rtBuckets[bucket] = (sum + entry.DurationMs, count + 1);
        }

        List<TimePoint> requestsOverTime = requestBuckets
            .OrderBy(k => k.Key)
            .Select(k => new TimePoint { Time = k.Key.ToString("HH:mm:ss"), Value = k.Value })
            .ToList();

        List<TimePoint> responseTimeOverTime = rtBuckets
            .OrderBy(k => k.Key)
            .Select(k => new TimePoint { Time = k.Key.ToString("HH:mm:ss"), Value = k.Value.Sum / k.Value.Count })
            .ToList();

        List<StatusGroup> statusDistribution = all
            .Where(e => e.StatusCode.HasValue)
            .GroupBy(e => e.StatusCode!.Value)
            .Select(g => new StatusGroup { Label = g.Key.ToString(), Count = g.Count() })
            .OrderBy(g => g.Label)
            .ToList();

        int totalRequests = all.Count;
        int requestsPerMin = all.Count(e => e.Timestamp >= now.AddMinutes(-1));
        double avgResponseTime = all.Count != 0 ? all.Average(e => e.DurationMs) : 0;
        double errorRate = totalRequests > 0
            ? all.Count(e => e.StatusCode >= 400) * 100.0 / totalRequests
            : 0;

        return new DashboardMetrics
        {
            RequestsOverTime = requestsOverTime,
            ResponseTimeOverTime = responseTimeOverTime,
            StatusDistribution = statusDistribution,
            TotalRequests = totalRequests,
            RequestsPerMin = requestsPerMin,
            AvgResponseTime = avgResponseTime,
            ErrorRate = errorRate,
            LatestRequest = all.FirstOrDefault()
        };
    }

    private static DateTime BucketFor(DateTimeOffset dt)
    {
        var utc = dt.UtcDateTime;
        return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, (utc.Second / 30) * 30, DateTimeKind.Utc);
    }
}
