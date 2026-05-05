using Microsoft.Extensions.Options;
using Vakthund.Proxy.Options;
using Vakthund.Shared.Models;

namespace Vakthund.Proxy.Services;

public class AuditQueue
{
    private readonly Queue<AuditEntry> _entries = new();
    private readonly AuditLossAccumulator _loss = new();
    private readonly SemaphoreSlim _available;
    private readonly Lock _lock = new();
    private readonly int _capacity;

    public AuditQueue(IOptions<VakthundOptions> options)
    {
        _capacity = Math.Max(1, options.Value.MaxQueuedEntries);
        _available = new SemaphoreSlim(0, _capacity);
    }

    public void Enqueue(AuditEntry entry)
    {
        bool signalReader;

        lock (_lock)
        {
            signalReader = _entries.Count < _capacity;

            if (!signalReader)
            {
                AuditEntry dropped = _entries.Dequeue();
                _loss.Add(dropped);
            }

            _entries.Enqueue(entry);
        }

        if (signalReader)
        {
            _available.Release();
        }
    }

    public async Task<AuditBatch> ReadBatchAsync(int maxBatchSize, TimeSpan flushInterval, CancellationToken cancellationToken)
    {
        int batchSize = Math.Max(1, maxBatchSize);
        var entries = new List<AuditEntry>(batchSize);

        await _available.WaitAsync(cancellationToken);
        DrainOne(entries);
        DrainAvailable(entries, batchSize);

        if (entries.Count < batchSize)
        {
            await Task.Delay(flushInterval, cancellationToken);
            DrainAvailable(entries, batchSize);
        }

        return new AuditBatch
        {
            Entries = entries,
            Loss = DrainLoss()
        };
    }

    private void DrainOne(List<AuditEntry> entries)
    {
        lock (_lock)
        {
            if (_entries.Count > 0)
            {
                entries.Add(_entries.Dequeue());
            }
        }
    }

    private void DrainAvailable(List<AuditEntry> entries, int batchSize)
    {
        while (entries.Count < batchSize && _available.Wait(0))
        {
            DrainOne(entries);
        }
    }

    private AuditLossSummary? DrainLoss()
    {
        lock (_lock)
            return _loss.Drain();
    }

    private sealed class AuditLossAccumulator
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
}
