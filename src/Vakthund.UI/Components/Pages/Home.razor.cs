using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Vakthund.UI.Models;
using Vakthund.UI.Options;
using Vakthund.UI.Services;

namespace Vakthund.UI.Components.Pages;

public partial class Home : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;
    [Inject] private AuditStore AuditStore { get; set; } = null!;
    [Inject] private MetricsStore MetricsStore { get; set; } = null!;
    [Inject] private MetricsService MetricsService { get; set; } = null!;
    [Inject] private IOptions<UiOptions> Options { get; set; } = null!;

    private DashboardMetrics _metrics = null!;
    private int MaxAuditEntries => Options.Value.MaxStoredAuditEntries;
    private readonly CancellationTokenSource _refreshCts = new();
    private bool _disposed;

    protected override void OnInitialized()
    {
        _metrics = ComputeMetrics();
        _ = RefreshLoopAsync(_refreshCts.Token);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await SetTitle("Dashboard — Vakthund");
        }
    }

    private async Task RefreshLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(RefreshInterval, cancellationToken);

                if (_disposed)
                {
                    return;
                }

                DashboardMetrics metrics = ComputeMetrics();

                await InvokeAsync(() =>
                {
                    if (_disposed)
                    {
                        return;
                    }

                    _metrics = metrics;
                    StateHasChanged();
                });
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                // Ignore render work that raced with disposal.
            }
        }
    }

    private DashboardMetrics ComputeMetrics() =>
        MetricsService.Compute(MetricsStore.Snapshot(), AuditStore.Count, AuditStore.Latest());

    private async Task SetTitle(string title) =>
        await JsRuntime.InvokeVoidAsync("setDocumentTitle", title);

    public void Dispose()
    {
        _disposed = true;
        _refreshCts.Cancel();
        _refreshCts.Dispose();
    }
}
