using System.Text.Json.Serialization;

namespace Vakthund.Shared.Models;

public class AuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public required string Scheme { get; set; }
    public required string? Host { get; set; }
    public string? IncomingHost { get; set; }
    public required string Path { get; set; }
    public string? Query { get; set; }
    public required string Method { get; set; }
    public string? ClientIp { get; set; }

    public string Uri => string.IsNullOrEmpty(Query)
        ? $"{Scheme}://{Host}{Path}"
        : $"{Scheme}://{Host}{Path}{Query}";
    public Dictionary<string, string> Headers { get; set; } = [];
    public Dictionary<string, string> Cookies { get; set; } = [];
    public Dictionary<string, string> Queries { get; set; } = [];
    public string? ContentType { get; set; }
    public string? Body { get; set; }
    public string? ResponseContentType { get; set; }
    public string? ResponseBody { get; set; }
    public int? StatusCode { get; set; }
    public long? TargetDurationMs { get; set; }
    public long DurationMs { get; set; }
    public bool Upstreamed { get; set; }
    public DateTimeOffset CompletedAt => Timestamp.AddMilliseconds(DurationMs);

    [JsonIgnore]
    public ProxyRouteInfo? MatchedRoute { get; set; }
}
