using Vakthund.UI.Enums;

namespace Vakthund.UI.Models;

public record ToastMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Summary { get; init; }
    public string? Detail { get; init; }
    public ToastColor Color { get; init; } = ToastColor.Info;
    public int Duration { get; init; } = 3000;
}
