namespace Vakthund.UI.Models;

public record TokenHeaderSummary
{
    public string? Algorithm { get; init; }
    public string? KeyId { get; init; }
    public string? Type { get; init; }
}
