using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Vakthund.Shared.Models;
using Vakthund.UI.Enums;
using Vakthund.UI.Options;
using Vakthund.UI.Services;

namespace Vakthund.Tests.Services;

public class MetricsStorePersistenceTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"vakthund_metrics_test_{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public void InitializeFromDisk_LoadsPersistedAggregateBuckets()
    {
        DateTimeOffset now = CurrentMinuteAtSecond(30);
        MetricsStore first = Store();
        first.AddRange([
            Entry(now.AddSeconds(-10), durationMs: 100),
            Entry(now.AddSeconds(-5), durationMs: 300)
        ]);

        MetricsStore second = Store();
        second.InitializeFromDisk([]);

        MetricsSnapshot snapshot = second.Snapshot();
        MetricsBucketSnapshot bucket = Assert.Single(snapshot.MinuteBuckets);
        Assert.Equal(2, bucket.Count);
        Assert.Equal(400, bucket.DurationSumMs);
    }

    [Fact]
    public void InitializeFromDisk_IgnoresFallbackRows_WhenPersistedMetricsExist()
    {
        DateTimeOffset now = CurrentMinuteAtSecond(30);
        AuditEntry retained = Entry(now, statusCode: 200, durationMs: 100);
        AuditEntry deleted = Entry(now.AddSeconds(-10), statusCode: 500, durationMs: 300);
        MetricsStore first = Store();
        first.AddRange([retained, deleted]);

        MetricsStore second = Store();
        second.InitializeFromDisk([retained]);

        MetricsBucketSnapshot bucket = Assert.Single(second.Snapshot().MinuteBuckets);
        Assert.Equal(2, bucket.Count);
        Assert.Equal(400, bucket.DurationSumMs);
        Assert.Equal(1, bucket.StatusCounts[200]);
        Assert.Equal(1, bucket.StatusCounts[500]);
    }

    [Fact]
    public void InitializeFromDisk_SeedsMetricsFromAuditRows_WhenNoPersistedMetricsExist()
    {
        AuditEntry existing = Entry(DateTimeOffset.UtcNow, durationMs: 125);

        MetricsStore first = Store();
        first.InitializeFromDisk([existing]);

        MetricsStore second = Store();
        second.InitializeFromDisk([]);

        MetricsBucketSnapshot bucket = Assert.Single(second.Snapshot().MinuteBuckets);
        Assert.Equal(1, bucket.Count);
        Assert.Equal(125, bucket.DurationSumMs);
    }

    [Fact]
    public void AddRange_TrimsPersistedMetricBucketsByRetention()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        MetricsStore first = Store(retention: "1h");
        first.AddRange([
            Entry(now.AddHours(-2), durationMs: 100),
            Entry(now.AddMinutes(-30), durationMs: 300)
        ]);

        MetricsStore second = Store(retention: "1h");
        second.InitializeFromDisk([]);

        MetricsBucketSnapshot bucket = Assert.Single(second.Snapshot().MinuteBuckets);
        Assert.Equal(1, bucket.Count);
        Assert.Equal(300, bucket.DurationSumMs);
    }

    private MetricsStore Store(string retention = "") =>
        new(Options.Create(new UiOptions
        {
            StorageMode = StorageMode.Disk,
            StoragePath = _dbPath,
            Retention = retention
        }));

    private static DateTimeOffset CurrentMinuteAtSecond(int second)
    {
        DateTime now = DateTime.UtcNow;
        return new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, second, TimeSpan.Zero);
    }

    private static AuditEntry Entry(
        DateTimeOffset timestamp,
        int? statusCode = 200,
        long durationMs = 25,
        long? targetDurationMs = null) =>
        new()
        {
            Id = Guid.NewGuid(),
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
