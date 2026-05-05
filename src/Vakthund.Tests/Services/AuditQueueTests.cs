using Microsoft.Extensions.Options;
using Vakthund.Proxy.Options;
using Vakthund.Proxy.Services;
using Vakthund.Shared.Models;

namespace Vakthund.Tests.Services;

public class AuditQueueTests
{
    [Fact]
    public async Task ReadBatchAsync_SummarizesDroppedEntriesWhenQueueIsFull()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var queue = new AuditQueue(Options.Create(new ProxyOptions { MaxQueuedEntries = 1 }));
        AuditEntry dropped = Entry(now, statusCode: 500, durationMs: 100, targetDurationMs: 90);
        AuditEntry retained = Entry(now.AddSeconds(1), statusCode: 200, durationMs: 20);

        queue.Enqueue(dropped);
        queue.Enqueue(retained);

        AuditBatch batch = await queue.ReadBatchAsync(10, TimeSpan.Zero, CancellationToken.None);

        AuditEntry entry = Assert.Single(batch.Entries);
        Assert.Same(retained, entry);

        AuditLossSummary loss = Assert.IsType<AuditLossSummary>(batch.Loss);
        Assert.Equal(1, loss.Count);

        AuditLossBucket minuteBucket = Assert.Single(loss.MinuteBuckets);
        Assert.Equal(1, minuteBucket.Count);
        Assert.Equal(100, minuteBucket.DurationSumMs);
        Assert.Equal(90, minuteBucket.TargetDurationSumMs);
        Assert.Equal(1, minuteBucket.TargetCount);
        Assert.Equal(1, minuteBucket.ErrorCount);
        Assert.Equal(1, minuteBucket.StatusCounts[500]);

        AuditLossSecondBucket secondBucket = Assert.Single(loss.SecondBuckets);
        Assert.Equal(1, secondBucket.Count);
    }

    private static AuditEntry Entry(
        DateTimeOffset timestamp,
        int statusCode,
        long durationMs,
        long? targetDurationMs = null) =>
        new()
        {
            Timestamp = timestamp,
            Scheme = "https",
            Host = "example.test",
            Path = "/api",
            Method = "GET",
            StatusCode = statusCode,
            TargetDurationMs = targetDurationMs,
            DurationMs = durationMs
        };
}
