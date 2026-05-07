using Microsoft.Extensions.Options;
using Vakthund.Proxy.Options;
using Vakthund.Shared.Models;

namespace Vakthund.Proxy.Services;

public class AuditQueue
{
    private readonly Queue<AuditEntry> _entries = new();
    private readonly AuditLossAccumulator _loss = new();
    private readonly SemaphoreSlim _available;
    private readonly Lock _lock = new();
    private readonly int _capacity;

    public bool IsUncapped { get; }

    public AuditQueue(IOptions<ProxyOptions> options)
    {
        int configured = options.Value.MaxQueuedEntries;
        IsUncapped = configured == 0;
        _capacity = IsUncapped ? int.MaxValue : Math.Max(1, configured);
        _available = new SemaphoreSlim(0, _capacity);
    }

    public void Enqueue(AuditEntry entry)
    {
        bool signalReader;

        lock (_lock)
        {
            signalReader = _entries.Count < _capacity;

            if (!signalReader)
            {
                AuditEntry dropped = _entries.Dequeue();
                _loss.Add(dropped);
            }

            _entries.Enqueue(entry);
        }

        if (signalReader)
        {
            _available.Release();
        }
    }

    public async Task<AuditBatch> ReadBatchAsync(int maxBatchSize, TimeSpan flushInterval, CancellationToken cancellationToken)
    {
        int batchSize = Math.Max(1, maxBatchSize);
        var entries = new List<AuditEntry>(batchSize);

        await _available.WaitAsync(cancellationToken);
        DrainOne(entries);
        DrainAvailable(entries, batchSize);

        if (entries.Count < batchSize)
        {
            await Task.Delay(flushInterval, cancellationToken);
            DrainAvailable(entries, batchSize);
        }

        return new AuditBatch
        {
            Entries = entries,
            Loss = DrainLoss()
        };
    }

    private void DrainOne(List<AuditEntry> entries)
    {
        lock (_lock)
        {
            if (_entries.Count > 0)
            {
                entries.Add(_entries.Dequeue());
            }
        }
    }

    private void DrainAvailable(List<AuditEntry> entries, int batchSize)
    {
        while (entries.Count < batchSize && _available.Wait(0))
        {
            DrainOne(entries);
        }
    }

    private AuditLossSummary? DrainLoss()
    {
        lock (_lock)
            return _loss.Drain();
    }
}
