using Vakthund.Shared.Models;

namespace Vakthund.UI.Services;

public class MetricsStore
{
    private const int ChartWindowMinutes = 60;
    private const int RecentWindowSeconds = 120;

    private readonly Dictionary<DateTime, MetricsBucket> _minuteBuckets = [];
    private readonly Dictionary<DateTime, int> _secondBuckets = [];
    private readonly Lock _lock = new();
    private DateTimeOffset? _latestTimestamp;

    public void AddRange(IEnumerable<AuditEntry> entries)
    {
        lock (_lock)
        {
            bool added = false;

            foreach (AuditEntry entry in entries)
            {
                added = true;

                if (_latestTimestamp is null || entry.Timestamp > _latestTimestamp.Value)
                {
                    _latestTimestamp = entry.Timestamp;
                }

                DateTime minute = MinuteBucket(entry.Timestamp);
                if (!_minuteBuckets.TryGetValue(minute, out MetricsBucket? minuteBucket))
                {
                    minuteBucket = new MetricsBucket(minute);
                    _minuteBuckets[minute] = minuteBucket;
                }

                minuteBucket.Add(entry);

                DateTime second = SecondBucket(entry.Timestamp);
                _secondBuckets[second] = _secondBuckets.GetValueOrDefault(second) + 1;
            }

            if (added)
            {
                Trim();
            }
        }
    }

    public MetricsSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new MetricsSnapshot(
                _latestTimestamp,
                _minuteBuckets.Values
                    .Select(bucket => bucket.ToSnapshot())
                    .OrderBy(bucket => bucket.Start)
                    .ToArray(),
                new Dictionary<DateTime, int>(_secondBuckets));
        }
    }

    public void AddLoss(AuditLossSummary loss)
    {
        lock (_lock)
        {
            foreach (AuditLossBucket lossBucket in loss.MinuteBuckets)
            {
                DateTime minute = MinuteBucket(lossBucket.Start);
                if (!_minuteBuckets.TryGetValue(minute, out MetricsBucket? minuteBucket))
                {
                    minuteBucket = new MetricsBucket(minute);
                    _minuteBuckets[minute] = minuteBucket;
                }

                minuteBucket.AddLoss(lossBucket);

                if (_latestTimestamp is null || lossBucket.Start > _latestTimestamp.Value)
                {
                    _latestTimestamp = lossBucket.Start;
                }
            }

            foreach (AuditLossSecondBucket secondBucket in loss.SecondBuckets)
            {
                DateTime second = SecondBucket(secondBucket.Start);
                _secondBuckets[second] = _secondBuckets.GetValueOrDefault(second) + secondBucket.Count;

                if (_latestTimestamp is null || secondBucket.Start > _latestTimestamp.Value)
                {
                    _latestTimestamp = secondBucket.Start;
                }
            }

            Trim();
        }
    }

    private void Trim()
    {
        DateTimeOffset anchor = _latestTimestamp ?? DateTimeOffset.UtcNow;
        DateTime minuteCutoff = MinuteBucket(anchor).AddMinutes(-ChartWindowMinutes + 1);
        DateTime secondCutoff = SecondBucket(anchor).AddSeconds(-RecentWindowSeconds + 1);

        foreach (DateTime minute in _minuteBuckets.Keys.Where(minute => minute < minuteCutoff).ToArray())
        {
            _minuteBuckets.Remove(minute);
        }

        foreach (DateTime second in _secondBuckets.Keys.Where(second => second < secondCutoff).ToArray())
        {
            _secondBuckets.Remove(second);
        }
    }

    public static DateTime MinuteBucket(DateTimeOffset timestamp)
    {
        DateTime utc = timestamp.UtcDateTime;
        return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc);
    }

    public static DateTime SecondBucket(DateTimeOffset timestamp)
    {
        DateTime utc = timestamp.UtcDateTime;
        return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, utc.Second, DateTimeKind.Utc);
    }

    private sealed class MetricsBucket(DateTime start)
    {
        private readonly Dictionary<int, int> _statusCounts = [];

        public DateTime Start { get; } = start;
        public int Count { get; private set; }
        public long DurationSumMs { get; private set; }
        public long TargetDurationSumMs { get; private set; }
        public int TargetCount { get; private set; }
        public int ErrorCount { get; private set; }
        public int LostCount { get; private set; }

        public void Add(AuditEntry entry)
        {
            Count++;
            DurationSumMs += entry.DurationMs;

            if (entry.TargetDurationMs.HasValue)
            {
                TargetDurationSumMs += entry.TargetDurationMs.Value;
                TargetCount++;
            }

            if (entry.StatusCode >= 400)
            {
                ErrorCount++;
            }

            if (entry.StatusCode.HasValue)
            {
                int statusCode = entry.StatusCode.Value;
                _statusCounts[statusCode] = _statusCounts.GetValueOrDefault(statusCode) + 1;
            }
        }

        public void AddLoss(AuditLossBucket loss)
        {
            Count += loss.Count;
            LostCount += loss.Count;
            DurationSumMs += loss.DurationSumMs;
            TargetDurationSumMs += loss.TargetDurationSumMs;
            TargetCount += loss.TargetCount;
            ErrorCount += loss.ErrorCount;

            foreach ((int statusCode, int count) in loss.StatusCounts)
            {
                _statusCounts[statusCode] = _statusCounts.GetValueOrDefault(statusCode) + count;
            }
        }

        public MetricsBucketSnapshot ToSnapshot() =>
            new(Start, Count, DurationSumMs, TargetDurationSumMs, TargetCount, ErrorCount, LostCount, new Dictionary<int, int>(_statusCounts));
    }
}

public sealed record MetricsSnapshot(
    DateTimeOffset? LatestTimestamp,
    IReadOnlyList<MetricsBucketSnapshot> MinuteBuckets,
    IReadOnlyDictionary<DateTime, int> SecondBuckets);

public sealed record MetricsBucketSnapshot(
    DateTime Start,
    int Count,
    long DurationSumMs,
    long TargetDurationSumMs,
    int TargetCount,
    int ErrorCount,
    int LostCount,
    IReadOnlyDictionary<int, int> StatusCounts);
