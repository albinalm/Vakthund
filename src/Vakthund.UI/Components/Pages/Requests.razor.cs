using Microsoft.AspNetCore.Components;
using Radzen;
using Radzen.Blazor;
using Vakthund.Shared.Models;
using Vakthund.UI.Services;

namespace Vakthund.UI.Components.Pages;

public partial class Requests : IDisposable
{
    [Inject] private AuditHubConnection AuditHubConnection { get; set; } = null!;
    [Inject] private AuditStore AuditStore { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;

    private IEnumerable<int> _pageSizeOptions = [10, 20, 30, 50];
    private RadzenDataGrid<AuditEntry> _grid = null!;
    private readonly List<AuditEntry> _entries = [];
    private string _searchText = "";
    private bool _disposed;
    private bool _reloadScheduled;

    private IReadOnlyList<AuditEntry> _visibleEntries => string.IsNullOrWhiteSpace(_searchText)
        ? _entries
        : _entries.Where(MatchesSearch).ToArray();

    protected override void OnInitialized()
    {
        _entries.AddRange(AuditStore.All.OrderByDescending(e => e.Timestamp));
        AuditHubConnection.Requests += OnRequests;
    }

    private void OnRequests(IReadOnlyList<AuditEntry> entries)
    {
        if (_disposed || entries.Count == 0)
        {
            return;
        }

        _ = ApplyRequestsAsync(entries);
    }

    private async Task ApplyRequestsAsync(IReadOnlyList<AuditEntry> entries)
    {
        try
        {
            await InvokeAsync(() =>
            {
                if (_disposed)
                {
                    return;
                }

                _entries.InsertRange(0, entries.OrderByDescending(e => e.Timestamp));
                ScheduleReload();
            });
        }
        catch (InvalidOperationException)
        {
            // A queued batch can race with component disposal under heavy traffic.
        }
    }

    private void ScheduleReload()
    {
        if (_reloadScheduled)
        {
            return;
        }

        _reloadScheduled = true;
        _ = ReloadSoonAsync();
    }

    private async Task ReloadSoonAsync()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(250));

        if (_disposed)
        {
            return;
        }

        try
        {
            await InvokeAsync(async () =>
            {
                if (_disposed)
                {
                    return;
                }

                _reloadScheduled = false;
                await _grid.Reload();
            });
        }
        catch (InvalidOperationException)
        {
            // Ignore render work that raced with disposal.
        }
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

    public void Dispose()
    {
        _disposed = true;
        AuditHubConnection.Requests -= OnRequests;
    }
}
