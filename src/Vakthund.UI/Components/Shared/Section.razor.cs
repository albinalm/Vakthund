using Microsoft.AspNetCore.Components;

namespace Vakthund.UI.Components.Shared;

public partial class Section
{
    [Parameter, EditorRequired] public string Title { get; set; } = "";
    [Parameter] public int? Count { get; set; }
    [Parameter] public string? Tag { get; set; }
    [Parameter, EditorRequired] public bool Expanded { get; set; }
    [Parameter, EditorRequired] public EventCallback OnToggle { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }

    private string ChevronClass => Expanded ? "rotate-180" : "";
}
