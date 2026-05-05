using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Vakthund.Shared.Models;

namespace Vakthund.UI.Services;

public class AuditHubConnection : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly AuditStore _store;
    private readonly MetricsStore _metricsStore;

    public event Action<IReadOnlyList<AuditEntry>>? Requests;
    public event Action<HubConnectionState>? StateChanged;

    public HubConnectionState State => _connection.State;

    public AuditHubConnection(IConfiguration configuration, AuditStore store, MetricsStore metricsStore)
    {
        _store = store;
        _metricsStore = metricsStore;
        string url = configuration["Proxy:AuditHubUrl"]!;
        _connection = new HubConnectionBuilder()
            .WithUrl(url)
            .WithAutomaticReconnect(new InfiniteRetryPolicy())
            .Build();

        _connection.Closed += _ => { StateChanged?.Invoke(_connection.State); return Task.CompletedTask; };
        _connection.Reconnecting += _ => { StateChanged?.Invoke(_connection.State); return Task.CompletedTask; };
        _connection.Reconnected += _ => { StateChanged?.Invoke(_connection.State); return Task.CompletedTask; };

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
                return;
            }
            catch
            {
                StateChanged?.Invoke(_connection.State);
                await Task.Delay(TimeSpan.FromSeconds(10));
            }
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
        _store.AddRange(entries);
        _metricsStore.AddRange(entries);
        Requests?.Invoke(entries);
    }
}

file class InfiniteRetryPolicy : IRetryPolicy
{
    private static readonly TimeSpan[] Defaults = [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    ];

    public TimeSpan? NextRetryDelay(RetryContext retryContext) =>
        Defaults[Math.Min(retryContext.PreviousRetryCount, Defaults.Length - 1)];
}
