namespace Vakthund.UI.Models;

public record AuthVerdict
{
    public required AuthVerdictSeverity Severity { get; init; }
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public IReadOnlyList<string> Hints { get; init; } = [];
}
