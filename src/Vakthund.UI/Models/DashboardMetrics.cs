using Vakthund.Shared.Models;

namespace Vakthund.UI.Models;

public record DashboardMetrics
{
    public required IReadOnlyList<TimePoint> RequestsOverTime { get; init; }
    public required IReadOnlyList<TimePoint> ResponseTimeOverTime { get; init; }
    public required IReadOnlyList<StatusGroup> StatusDistribution { get; init; }
    public required int TotalRequests { get; init; }
    public required int WindowRequests { get; init; }
    public required int LostAuditEntries { get; init; }
    public required int RequestsPerMin { get; init; }
    public required double AvgResponseTime { get; init; }
    public required double AvgTargetResponseTime { get; init; }
    public required double ErrorRate { get; init; }
    public AuditEntry? LatestRequest { get; init; }
}
