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
        if (queue.IsUncapped)
            logger.LogWarning("MaxQueuedEntries is 0 — the audit queue is uncapped and will grow without bound. This may cause memory exhaustion under sustained load.");

        while (!stoppingToken.IsCancellationRequested)
        {
            AuditBatch batch = await queue.ReadBatchAsync(MaxBatchSize, FlushInterval, stoppingToken);
            await FlushAsync(batch, stoppingToken);
        }
    }

    private async Task FlushAsync(AuditBatch batch, CancellationToken stoppingToken)
    {
        if (batch.Entries.Count == 0 && batch.Loss is null)
        {
            return;
        }

        try
        {
            string json = JsonSerializer.Serialize(batch);
            await hub.Clients.All.SendAsync("OnAuditBatch", json, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to broadcast {Count} audit entries and {LossCount} dropped audit entries.",
                batch.Entries.Count,
                batch.Loss?.Count ?? 0);
        }
    }
}
