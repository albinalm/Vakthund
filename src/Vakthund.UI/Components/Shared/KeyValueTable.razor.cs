using Microsoft.AspNetCore.Components;

namespace Vakthund.UI.Components.Shared;

public partial class KeyValueTable
{
    [Parameter, EditorRequired] public Dictionary<string, string> Entries { get; set; } = [];

    private string RowClass(string key)
    {
        var index = Entries.Keys.ToList().IndexOf(key);
        return index % 2 == 0 ? "bg-gray-900" : "bg-gray-950";
    }
}
