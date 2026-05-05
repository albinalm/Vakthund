using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vakthund.Shared.Models;
using Vakthund.UI.Options;
using Vakthund.UI.Services.Interfaces;

namespace Vakthund.UI.Services;

public class AuditHubConnection : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly IAuditStore _auditStore;
    private readonly MetricsStore _metricsStore;
    private readonly ProxyConfigService _proxyConfigService;
    private readonly ProxyRouteMatcher _proxyRouteMatcher;
    private volatile ProxyConfig? _cachedConfig;

    public event Action<IReadOnlyList<AuditEntry>>? Requests;
    public event Action<HubConnectionState>? StateChanged;

    public HubConnectionState State => _connection.State;

    public AuditHubConnection(IConfiguration configuration, IOptions<UiOptions> uiOptions,
        IServiceProvider serviceProvider, MetricsStore metricsStore,
        ProxyConfigService proxyConfigService, ProxyRouteMatcher proxyRouteMatcher)
    {
        _auditStore = serviceProvider.GetRequiredKeyedService<IAuditStore>(uiOptions.Value.StorageMode);
        _metricsStore = metricsStore;
        _proxyConfigService = proxyConfigService;
        _proxyRouteMatcher = proxyRouteMatcher;
        string url = configuration["Proxy:AuditHubUrl"]!;
        _connection = new HubConnectionBuilder()
            .WithUrl(url)
            .WithAutomaticReconnect(new InfiniteRetryPolicy())
            .Build();

        _connection.Closed += _ =>
        {
            StateChanged?.Invoke(_connection.State);
            return Task.CompletedTask;
        };
        _connection.Reconnecting += _ =>
        {
            StateChanged?.Invoke(_connection.State);
            return Task.CompletedTask;
        };
        _connection.Reconnected += connectionId =>
        {
            StateChanged?.Invoke(_connection.State);
            _ = RefreshConfigAsync();
            return Task.CompletedTask;
        };

        _connection.On<string>("OnAuditBatch", HandleAuditBatch);
        _connection.On<string>("OnRequests", HandleRequests);
        _connection.On<string>("OnRequest", json =>
        {
            var entry = JsonSerializer.Deserialize<AuditEntry>(json);
            if (entry is null)
            {
                return;
            }

            HandleEntries([entry]);
        });
    }

    public void Start() => _ = ConnectWithRetryAsync();

    private async Task ConnectWithRetryAsync()
    {
        while (_connection.State == HubConnectionState.Disconnected)
        {
            try
            {
                await _connection.StartAsync();
                StateChanged?.Invoke(_connection.State);
                _ = RefreshConfigAsync();
                return;
            }
            catch
            {
                StateChanged?.Invoke(_connection.State);
                await Task.Delay(TimeSpan.FromSeconds(10));
            }
        }
    }

    private async Task RefreshConfigAsync()
    {
        ProxyConfigLoadResult result = await _proxyConfigService.GetAsync();
        if (result.Config is not null)
        {
            _cachedConfig = result.Config;
        }
    }

    public async ValueTask DisposeAsync() =>
        await _connection.DisposeAsync();

    private void HandleRequests(string json)
    {
        AuditEntry[]? entries = JsonSerializer.Deserialize<AuditEntry[]>(json);
        if (entries is not { Length: > 0 })
        {
            return;
        }

        HandleEntries(entries);
    }

    private void HandleAuditBatch(string json)
    {
        AuditBatch? batch = JsonSerializer.Deserialize<AuditBatch>(json);
        if (batch is null)
        {
            return;
        }

        if (batch.Entries.Count > 0)
        {
            HandleEntries(batch.Entries);
        }

        if (batch.Loss is not null)
        {
            _metricsStore.AddLoss(batch.Loss);
        }
    }

    private void HandleEntries(IReadOnlyList<AuditEntry> entries)
    {
        ProxyConfig? config = _cachedConfig;
        if (config is not null)
        {
            foreach (AuditEntry entry in entries)
            {
                entry.MatchedRoute = _proxyRouteMatcher.FindMatchingRoute(config.Routes, entry.Path);
            }
        }

        _auditStore.AddRange(entries);
        _metricsStore.AddRange(entries);
        Requests?.Invoke(entries);
    }
}

file class InfiniteRetryPolicy : IRetryPolicy
{
    private static readonly TimeSpan[] Defaults =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    ];

    public TimeSpan? NextRetryDelay(RetryContext retryContext) =>
        Defaults[Math.Min(retryContext.PreviousRetryCount, Defaults.Length - 1)];
}