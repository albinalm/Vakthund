using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Radzen;
using Radzen.Blazor;
using Vakthund.Shared.Models;
using Vakthund.UI.Options;
using Vakthund.UI.Services;

namespace Vakthund.UI.Components.Pages;

public partial class Requests
{
    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;
    [Inject] private AuditStore AuditStore { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;
    [Inject] private IOptions<VakthundOptions> Options { get; set; } = null!;

    private IEnumerable<int> _pageSizeOptions = [10, 20, 30, 50];
    private RadzenDataGrid<AuditEntry> _grid = null!;
    private readonly List<AuditEntry> _entries = [];
    private string _searchText = "";
    private int MaxAuditEntries => Options.Value.MaxAuditEntries;

    private IReadOnlyList<AuditEntry> _visibleEntries => string.IsNullOrWhiteSpace(_searchText)
        ? _entries
        : _entries.Where(MatchesSearch).ToArray();

    protected override void OnInitialized()
    {
        LoadEntries();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
            await SetTitle("Requests — Vakthund");
    }

    private async Task SetTitle(string title) =>
        await JsRuntime.InvokeVoidAsync("setDocumentTitle", title);

    private async Task RefreshAsync()
    {
        LoadEntries();
        await _grid.Reload();
    }

    private void LoadEntries()
    {
        _entries.Clear();
        _entries.AddRange(AuditStore.All.OrderByDescending(e => e.Timestamp));
    }

    private void OnRowClick(DataGridRowMouseEventArgs<AuditEntry> args)
    {
        if (args.Data is null)
        {
            return;
        }

        Nav.NavigateTo($"/request/{args.Data.Id}");
    }

    private bool MatchesSearch(AuditEntry entry)
    {
        string term = _searchText.Trim();
        return Contains(entry.Method, term)
            || Contains(entry.StatusCode?.ToString(), term)
            || Contains(entry.Host, term)
            || Contains(entry.Path, term)
            || Contains(entry.Query, term);
    }

    private static bool Contains(string? value, string term) =>
        value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
}
