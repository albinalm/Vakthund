using Vakthund.Shared.Models;

namespace Vakthund.Proxy.Services;

internal sealed class AuditLossAccumulator
{
    private readonly Dictionary<DateTime, AuditLossBucket> _minuteBuckets = [];
    private readonly Dictionary<DateTime, AuditLossSecondBucket> _secondBuckets = [];
    private int _count;

    public void Add(AuditEntry entry)
    {
        _count++;

        DateTime minute = MinuteBucket(entry.Timestamp);
        if (!_minuteBuckets.TryGetValue(minute, out AuditLossBucket? minuteBucket))
        {
            minuteBucket = new AuditLossBucket { Start = new DateTimeOffset(minute) };
            _minuteBuckets[minute] = minuteBucket;
        }

        minuteBucket.Count++;
        minuteBucket.DurationSumMs += entry.DurationMs;

        if (entry.TargetDurationMs.HasValue)
        {
            minuteBucket.TargetDurationSumMs += entry.TargetDurationMs.Value;
            minuteBucket.TargetCount++;
        }

        if (entry.StatusCode >= 400)
        {
            minuteBucket.ErrorCount++;
        }

        if (entry.StatusCode.HasValue)
        {
            int statusCode = entry.StatusCode.Value;
            minuteBucket.StatusCounts[statusCode] = minuteBucket.StatusCounts.GetValueOrDefault(statusCode) + 1;
        }

        DateTime second = SecondBucket(entry.Timestamp);
        if (!_secondBuckets.TryGetValue(second, out AuditLossSecondBucket? secondBucket))
        {
            secondBucket = new AuditLossSecondBucket { Start = new DateTimeOffset(second) };
            _secondBuckets[second] = secondBucket;
        }

        secondBucket.Count++;
    }

    public AuditLossSummary? Drain()
    {
        if (_count == 0)
        {
            return null;
        }

        var summary = new AuditLossSummary
        {
            Count = _count,
            MinuteBuckets = _minuteBuckets.Values.OrderBy(bucket => bucket.Start).ToList(),
            SecondBuckets = _secondBuckets.Values.OrderBy(bucket => bucket.Start).ToList()
        };

        _count = 0;
        _minuteBuckets.Clear();
        _secondBuckets.Clear();

        return summary;
    }

    private static DateTime MinuteBucket(DateTimeOffset timestamp)
    {
        DateTime utc = timestamp.UtcDateTime;
        return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc);
    }

    private static DateTime SecondBucket(DateTimeOffset timestamp)
    {
        DateTime utc = timestamp.UtcDateTime;
        return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, utc.Second, DateTimeKind.Utc);
    }
}
