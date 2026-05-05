using JetBrains.Annotations;

namespace Vakthund.UI.Models;

public record TimePoint
{
    public required string Time { [UsedImplicitly] get; init; }
    public required double Value { [UsedImplicitly] get; init; }
}
