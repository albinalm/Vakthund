using Microsoft.AspNetCore.Components;
using Vakthund.Shared.Models;
using Vakthund.UI.Models;
using Vakthund.UI.Services;

namespace Vakthund.UI.Components.Pages;

public partial class Home : IDisposable
{
    [Inject] private AuditHubConnection AuditHubConnection { get; set; } = null!;
    [Inject] private AuditStore AuditStore { get; set; } = null!;
    [Inject] private MetricsService MetricsService { get; set; } = null!;

    private DashboardMetrics _metrics = null!;
    private bool _disposed;
    private bool _refreshScheduled;

    protected override void OnInitialized()
    {
        _metrics = MetricsService.Compute(AuditStore.All);
        AuditHubConnection.Requests += OnRequests;
    }

    private void OnRequests(IReadOnlyList<AuditEntry> entries)
    {
        if (_disposed || entries.Count == 0)
        {
            return;
        }

        _ = ApplyRequestsAsync();
    }

    private async Task ApplyRequestsAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await InvokeAsync(() =>
            {
                if (_disposed)
                {
                    return;
                }

                ScheduleRefresh();
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or OperationCanceledException)
        {
            // A queued batch can race with component disposal under heavy traffic.
        }
    }

    private void ScheduleRefresh()
    {
        if (_refreshScheduled)
        {
            return;
        }

        _refreshScheduled = true;
        _ = RefreshSoonAsync();
    }

    private async Task RefreshSoonAsync()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(250));

        if (_disposed)
        {
            return;
        }

        try
        {
            await InvokeAsync(() =>
            {
                if (_disposed)
                {
                    return;
                }

                _refreshScheduled = false;
                _metrics = MetricsService.Compute(AuditStore.All);
                StateHasChanged();
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or OperationCanceledException)
        {
            // Ignore render work that raced with disposal.
        }
    }

    public void Dispose()
    {
        _disposed = true;
        AuditHubConnection.Requests -= OnRequests;
    }
}
