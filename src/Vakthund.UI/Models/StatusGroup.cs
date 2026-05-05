using JetBrains.Annotations;

namespace Vakthund.UI.Models;

public record StatusGroup
{
    public required string Label { get; init; }
    public required int Count { [UsedImplicitly] get; init; }
}
