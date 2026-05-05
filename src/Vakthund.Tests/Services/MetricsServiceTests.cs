using Vakthund.Shared.Models;
using Vakthund.UI.Models;
using Vakthund.UI.Services;

namespace Vakthund.Tests.Services;

public class MetricsServiceTests
{
    [Fact]
    public void Compute_CalculatesTotalsAndStatusDistribution()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        AuditEntry latest = Entry(now.AddSeconds(-10), statusCode: 200, durationMs: 120);
        AuditEntry error = Entry(now.AddSeconds(-20), statusCode: 500, durationMs: 280);
        AuditEntry redirect = Entry(now.AddMinutes(-2), statusCode: 302, durationMs: 50);

        DashboardMetrics metrics = new MetricsService().Compute([latest, error, redirect]);

        Assert.Equal(3, metrics.TotalRequests);
        Assert.Equal(3, metrics.WindowRequests);
        Assert.Equal(0, metrics.LostAuditEntries);
        Assert.Equal(11, metrics.RequestsPerMin);
        Assert.Equal(150, metrics.AvgResponseTime);
        Assert.Equal(0, metrics.AvgTargetResponseTime);
        Assert.Equal(100.0 / 3, metrics.ErrorRate, precision: 6);
        Assert.Same(latest, metrics.LatestRequest);
        Assert.Equal(["200", "302", "500"], metrics.StatusDistribution.Select(group => group.Label));
        Assert.Equal([1, 1, 1], metrics.StatusDistribution.Select(group => group.Count));
    }

    [Fact]
    public void Compute_BuildsTimeSeriesOnlyForRecentWindow()
    {
        DateTimeOffset recentTimestamp = DateTimeOffset.UtcNow.AddSeconds(-5);

        DashboardMetrics metrics = new MetricsService().Compute(
        [
            Entry(recentTimestamp, durationMs: 100),
            Entry(recentTimestamp, durationMs: 300),
            Entry(recentTimestamp.AddMinutes(-61), durationMs: 900)
        ]);

        Assert.Equal(10, metrics.RequestsOverTime.Count);
        Assert.Equal(2, metrics.RequestsOverTime.Sum(point => point.Value));
        Assert.Equal(200, Assert.Single(metrics.ResponseTimeOverTime).Value);
    }

    [Fact]
    public void Compute_AvgResponseTimeExcludesEntriesOutsideWindow()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        AuditEntry recent = Entry(now.AddMinutes(-5), durationMs: 20);
        AuditEntry old = Entry(now.AddMinutes(-61), durationMs: 10000);

        DashboardMetrics metrics = new MetricsService().Compute([recent, old]);

        Assert.Equal(20, metrics.AvgResponseTime);
    }

    [Fact]
    public void Compute_AvgTargetResponseTimeUsesTargetDurationsInsideWindow()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        AuditEntry first = Entry(now.AddSeconds(-5), targetDurationMs: 20);
        AuditEntry second = Entry(now.AddSeconds(-4), targetDurationMs: 40);
        AuditEntry missing = Entry(now.AddSeconds(-3));
        AuditEntry old = Entry(now.AddMinutes(-61), targetDurationMs: 10000);

        DashboardMetrics metrics = new MetricsService().Compute([first, second, missing, old]);

        Assert.Equal(30, metrics.AvgTargetResponseTime);
    }

    [Fact]
    public void Compute_UsesTimestampForLatestRequest()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        AuditEntry newest = Entry(now, path: "/newest");
        AuditEntry oldest = Entry(now.AddMinutes(-1), path: "/oldest");

        DashboardMetrics metrics = new MetricsService().Compute([oldest, newest]);

        Assert.Same(newest, metrics.LatestRequest);
    }

    [Fact]
    public void Compute_AnchorsRecentRateToLatestRequestTimestamp()
    {
        DateTimeOffset oldCapture = DateTimeOffset.UtcNow.AddMinutes(-30);
        AuditEntry latest = Entry(oldCapture, path: "/latest");
        AuditEntry recent = Entry(oldCapture.AddSeconds(-30), path: "/recent");
        AuditEntry outsideWindow = Entry(oldCapture.AddMinutes(-2), path: "/outside");

        DashboardMetrics metrics = new MetricsService().Compute([latest, recent, outsideWindow]);

        Assert.Equal(4, metrics.RequestsPerMin);
    }

    [Fact]
    public void Compute_UsesAggregateSnapshotWhenRetainedEntriesAreTrimmed()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        AuditEntry latest = Entry(now, durationMs: 100);
        AuditEntry retainedEvicted = Entry(now.AddSeconds(-10), durationMs: 200);
        AuditEntry alsoRetainedEvicted = Entry(now.AddSeconds(-20), durationMs: 300);
        var store = new MetricsStore();
        store.AddRange([latest, retainedEvicted, alsoRetainedEvicted]);

        DashboardMetrics metrics = new MetricsService().Compute(store.Snapshot(), 1, latest);

        Assert.Equal(1, metrics.TotalRequests);
        Assert.Equal(3, metrics.WindowRequests);
        Assert.Equal(200, metrics.AvgResponseTime);
    }

    [Fact]
    public void Compute_ProjectsRequestsPerMinFromObservedRate()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var store = new MetricsStore();
        store.AddRange(
        [
            Entry(now),
            Entry(now.AddSeconds(-10)),
            Entry(now.AddSeconds(-20))
        ]);

        DashboardMetrics metrics = new MetricsService().Compute(store.Snapshot(), 2, null);

        Assert.Equal(9, metrics.RequestsPerMin);
    }

    [Fact]
    public void Compute_IncludesAuditLossInAggregateMetrics()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        AuditEntry retained = Entry(now, statusCode: 200, durationMs: 100, targetDurationMs: 90);
        var store = new MetricsStore();
        store.AddRange([retained]);
        store.AddLoss(new AuditLossSummary
        {
            Count = 2,
            MinuteBuckets =
            [
                new AuditLossBucket
                {
                    Start = now.AddSeconds(-10),
                    Count = 2,
                    DurationSumMs = 500,
                    TargetDurationSumMs = 450,
                    TargetCount = 2,
                    ErrorCount = 1,
                    StatusCounts = new Dictionary<int, int>
                    {
                        [200] = 1,
                        [500] = 1
                    }
                }
            ],
            SecondBuckets =
            [
                new AuditLossSecondBucket { Start = now.AddSeconds(-10), Count = 2 }
            ]
        });

        DashboardMetrics metrics = new MetricsService().Compute(store.Snapshot(), 1, retained);

        Assert.Equal(1, metrics.TotalRequests);
        Assert.Equal(3, metrics.WindowRequests);
        Assert.Equal(2, metrics.LostAuditEntries);
        Assert.Equal(16, metrics.RequestsPerMin);
        Assert.Equal(200, metrics.AvgResponseTime);
        Assert.Equal(180, metrics.AvgTargetResponseTime);
        Assert.Equal(100.0 / 3, metrics.ErrorRate, precision: 6);
        Assert.Equal([2, 1], metrics.StatusDistribution.Select(group => group.Count));
    }

    private static AuditEntry Entry(
        DateTimeOffset timestamp,
        int? statusCode = 200,
        long durationMs = 25,
        long? targetDurationMs = null,
        string path = "/api") =>
        new()
        {
            Timestamp = timestamp,
            Scheme = "https",
            Host = "example.test",
            Path = path,
            Method = "GET",
            StatusCode = statusCode,
            TargetDurationMs = targetDurationMs,
            DurationMs = durationMs
        };
}
