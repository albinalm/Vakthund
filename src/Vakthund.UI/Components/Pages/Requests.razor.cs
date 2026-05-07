using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Radzen;
using Radzen.Blazor;
using Vakthund.Shared.Models;
using Vakthund.UI.Enums;
using Vakthund.UI.Options;
using Vakthund.UI.Services;
using Vakthund.UI.Services.Interfaces;

namespace Vakthund.UI.Components.Pages;

public partial class Requests : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = null!;
    [Inject] private IOptions<UiOptions> Options { get; set; } = null!;

    private IAuditStore AuditStore { get; set; } = null!;
    [Inject] private DialogService DialogService { get; set; } = null!;
    [Inject] private ToastService ToastService { get; set; } = null!;

    private const string ProfilesKey = "Requests_GridProfiles";
    private const string ActiveProfileKey = "Requests_GridActiveProfile";
    private const string DefaultProfileName = "Default";

    private readonly IEnumerable<int> _pageSizeOptions = [10, 20, 30, 50];
    private RadzenDataGrid<AuditEntry> _grid = null!;
    private readonly List<AuditEntry> _entries = [];
    private IList<AuditEntry> _selectedEntries = [];
    private string _searchText = "";
    private int _loadedCount;
    private bool _hasNewRequests;
    private bool _disposed;
    private readonly CancellationTokenSource _cts = new();
    private int MaxAuditEntries => Options.Value.MaxStoredAuditEntries;

    private Dictionary<string, DataGridSettings?> _profiles = new() { [DefaultProfileName] = null };
    private string _activeProfileName = DefaultProfileName;

    private DataGridSettings? Settings { get; set; }

    private IReadOnlyList<AuditEntry> VisibleEntries => string.IsNullOrWhiteSpace(_searchText)
        ? _entries
        : _entries.Where(MatchesSearch).ToArray();

    private int SelectedCount => _selectedEntries.Count;

    private bool AreAllVisibleSelected =>
        VisibleEntries.Count > 0 && VisibleEntries.All(IsEntrySelected);

    protected override void OnInitialized()
    {
        AuditStore = ServiceProvider.GetRequiredKeyedService<IAuditStore>(Options.Value.StorageMode);
        LoadEntries();
        _ = PollLoopAsync(_cts.Token);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        await SetTitle("Requests — Vakthund");
        await LoadAsync();
        StateHasChanged();
    }

    private async Task LoadAsync()
    {
        string? profilesJson = await JsRuntime.InvokeAsync<string?>("window.localStorage.getItem", ProfilesKey);
        if (!string.IsNullOrEmpty(profilesJson))
        {
            Dictionary<string, DataGridSettings?>? loaded =
                JsonSerializer.Deserialize<Dictionary<string, DataGridSettings?>>(profilesJson);
            if (loaded is not null)
            {
                _profiles = loaded;
            }
        }

        if (!_profiles.ContainsKey(DefaultProfileName))
        {
            _profiles[DefaultProfileName] = null;
        }

        string? active = await JsRuntime.InvokeAsync<string?>("window.localStorage.getItem", ActiveProfileKey);
        if (!string.IsNullOrEmpty(active) && _profiles.ContainsKey(active))
        {
            _activeProfileName = active;
        }

        Settings = _profiles[_activeProfileName];
    }

    private async Task OnProfileChangedAsync(ChangeEventArgs e)
    {
        string? value = e.Value?.ToString();

        if (value == "__create__")
        {
            await OpenCreateDialogAsync();
            return;
        }

        if (string.IsNullOrEmpty(value) || !_profiles.ContainsKey(value) || value == _activeProfileName)
        {
            return;
        }
        
        _activeProfileName = value;
        Settings = _profiles[value];
        await SaveActiveProfileAsync();
        await _grid.ReloadSettings(true);
    }

    private async Task OpenCreateDialogAsync()
    {
        dynamic? result = await DialogService.OpenAsync<CreateViewDialog>(
            "Create view",
            options: new DialogOptions
            {
                CloseDialogOnEsc = true,
                CloseDialogOnOverlayClick = true,
                Width = "360px",
                ShowClose = true
            });

        string? name = result as string;
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        _profiles[name] = Settings;
        _activeProfileName = name;
        await SaveProfilesAsync();
        await SaveActiveProfileAsync();
        ToastService.Show("View created", $"\"{name}\" saved with the current layout.", ToastColor.Accent);
    }

    private async Task TryDeleteProfileAsync()
    {
        bool? confirmed = await DialogService.Confirm(
            $"Delete \"{_activeProfileName}\"? This cannot be undone.",
            "Delete view",
            new ConfirmOptions { OkButtonText = "Delete", CancelButtonText = "Cancel" });

        if (confirmed != true)
        {
            return;
        }

        string deleted = _activeProfileName;
        _profiles.Remove(deleted);
        _activeProfileName = DefaultProfileName;
        Settings = _profiles[DefaultProfileName];
        await SaveProfilesAsync();
        await SaveActiveProfileAsync();
        ToastService.Show("View deleted", $"\"{deleted}\" has been removed.", ToastColor.Accent);
    }

    private async Task SaveActiveProfileAsync()
    {
        await JsRuntime.InvokeVoidAsync("window.localStorage.setItem", ActiveProfileKey, _activeProfileName);
    }

    private async Task SaveProfilesAsync()
    {
        await JsRuntime.InvokeVoidAsync("window.localStorage.setItem", ProfilesKey,
            JsonSerializer.Serialize(_profiles));
    }

    private async Task SaveCurrentLayoutAsync()
    {
        _profiles[_activeProfileName] = Settings;
        await SaveProfilesAsync();
        ToastService.Show("Layout saved", $"View \"{_activeProfileName}\" updated.", ToastColor.Accent);
    }

    private void SelectAllVisible()
    {
        Dictionary<Guid, AuditEntry> selectedById = _selectedEntries.ToDictionary(entry => entry.Id);
        foreach (AuditEntry entry in VisibleEntries)
        {
            selectedById[entry.Id] = entry;
        }

        _selectedEntries = selectedById.Values.ToList();
    }

    private void ClearSelection() => _selectedEntries = [];

    private bool IsEntrySelected(AuditEntry entry) =>
        _selectedEntries.Any(selectedEntry => selectedEntry.Id == entry.Id);

    private void ToggleSelectAllVisible(bool selected)
    {
        if (selected)
        {
            SelectAllVisible();
            return;
        }

        HashSet<Guid> visibleIds = VisibleEntries.Select(entry => entry.Id).ToHashSet();
        _selectedEntries = _selectedEntries.Where(entry => !visibleIds.Contains(entry.Id)).ToList();
    }

    private void ToggleEntrySelection(AuditEntry entry, bool selected)
    {
        List<AuditEntry> selectedEntries = _selectedEntries
            .Where(selectedEntry => selectedEntry.Id != entry.Id)
            .ToList();

        if (selected)
        {
            selectedEntries.Add(entry);
        }

        _selectedEntries = selectedEntries;
    }

    private async Task TryDeleteSelectedAsync()
    {
        Guid[] selectedIds = _selectedEntries
            .Select(entry => entry.Id)
            .Distinct()
            .ToArray();

        if (selectedIds.Length == 0)
        {
            return;
        }

        bool? confirmed = await DialogService.Confirm(
            $"Delete {selectedIds.Length:N0} selected request{(selectedIds.Length == 1 ? "" : "s")}? This cannot be undone.",
            "Delete requests",
            new ConfirmOptions { OkButtonText = "Delete", CancelButtonText = "Cancel" });

        if (confirmed != true)
        {
            return;
        }

        int deleted = AuditStore.Delete(selectedIds);
        ClearSelection();
        LoadEntries();
        await _grid.Reload();

        ToastService.Show(
            "Requests deleted",
            $"{deleted:N0} request{(deleted == 1 ? "" : "s")} removed.",
            ToastColor.Accent);
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, ct);

                if (_disposed)
                {
                    return;
                }

                int current = AuditStore.Count;
                bool hasNew = current != _loadedCount;

                await InvokeAsync(() =>
                {
                    if (_disposed)
                    {
                        return;
                    }

                    if (_hasNewRequests != hasNew)
                    {
                        _hasNewRequests = hasNew;
                        StateHasChanged();
                    }
                });
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException
                                           or OperationCanceledException)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }
            }
        }
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
        _loadedCount = AuditStore.Count;
        _hasNewRequests = false;
        PruneSelectionToLoadedEntries();
    }

    private void PruneSelectionToLoadedEntries()
    {
        if (_selectedEntries.Count == 0)
        {
            return;
        }

        HashSet<Guid> selectedIds = _selectedEntries.Select(entry => entry.Id).ToHashSet();
        _selectedEntries = _entries.Where(entry => selectedIds.Contains(entry.Id)).ToList();
    }

    public void Dispose()
    {
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
    }

    private bool MatchesSearch(AuditEntry entry)
    {
        string term = _searchText.Trim();
        return Contains(entry.Method, term)
               || Contains(entry.StatusCode?.ToString(), term)
               || Contains(entry.Host, term)
               || Contains(entry.ClientIp, term)
               || Contains(entry.Path, term)
               || Contains(entry.Query, term);
    }

    private static bool Contains(string? value, string term) =>
        value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
}
