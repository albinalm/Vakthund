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
        Assert.Equal(2, metrics.RequestsPerMin);
        Assert.Equal(150, metrics.AvgResponseTime);
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
            Entry(recentTimestamp.AddMinutes(-11), durationMs: 900)
        ]);

        Assert.Equal(2, Assert.Single(metrics.RequestsOverTime).Value);
        Assert.Equal(200, Assert.Single(metrics.ResponseTimeOverTime).Value);
    }

    private static AuditEntry Entry(DateTimeOffset timestamp, int? statusCode = 200, long durationMs = 25) =>
        new()
        {
            Timestamp = timestamp,
            Scheme = "https",
            Host = "example.test",
            Path = "/api",
            Method = "GET",
            StatusCode = statusCode,
            DurationMs = durationMs
        };
}
