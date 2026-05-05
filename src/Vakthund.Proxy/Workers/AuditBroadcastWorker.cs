using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Vakthund.Proxy.Services;
using Vakthund.Shared.Models;
using Vakthund.UI.Hubs;

namespace Vakthund.Proxy.Workers;

public class AuditBroadcastWorker(
    AuditQueue queue,
    IHubContext<AuditHub> hub,
    ILogger<AuditBroadcastWorker> logger) : BackgroundService
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(250);
    private const int MaxBatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<AuditEntry>(MaxBatchSize);

        while (await queue.Reader.WaitToReadAsync(stoppingToken))
        {
            DrainBatch(batch);

            if (batch.Count < MaxBatchSize)
            {
                await Task.Delay(FlushInterval, stoppingToken);
                DrainBatch(batch);
            }

            await FlushAsync(batch, stoppingToken);
        }
    }

    private void DrainBatch(List<AuditEntry> batch)
    {
        while (batch.Count < MaxBatchSize && queue.Reader.TryRead(out AuditEntry? entry))
            batch.Add(entry);
    }

    private async Task FlushAsync(List<AuditEntry> batch, CancellationToken stoppingToken)
    {
        if (batch.Count == 0)
        {
            return;
        }

        AuditEntry[] entries = batch.ToArray();
        batch.Clear();

        try
        {
            string json = JsonSerializer.Serialize(entries);
            await hub.Clients.All.SendAsync("OnRequests", json, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to broadcast {Count} audit entries.", entries.Length);
        }
    }
}
