using Vakthund.Shared.Models;

namespace Vakthund.UI.Models;

internal sealed class MetricsBucket(DateTime start)
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

    public static MetricsBucket FromSnapshot(MetricsBucketSnapshot snapshot)
    {
        MetricsBucket bucket = new(snapshot.Start)
        {
            Count = snapshot.Count,
            DurationSumMs = snapshot.DurationSumMs,
            TargetDurationSumMs = snapshot.TargetDurationSumMs,
            TargetCount = snapshot.TargetCount,
            ErrorCount = snapshot.ErrorCount,
            LostCount = snapshot.LostCount
        };

        foreach ((int statusCode, int count) in snapshot.StatusCounts)
        {
            bucket._statusCounts[statusCode] = count;
        }

        return bucket;
    }
}
