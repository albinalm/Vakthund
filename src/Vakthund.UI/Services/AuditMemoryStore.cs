using Microsoft.Extensions.Options;
using Vakthund.Shared.Models;
using Vakthund.UI.Options;
using Vakthund.UI.Services.Interfaces;

namespace Vakthund.UI.Services;

public class AuditMemoryStore(IOptions<UiOptions> options) : IAuditStore
{
    private readonly Dictionary<Guid, AuditEntry> _entries = [];
    private readonly Lock _lock = new();

    public void Add(AuditEntry entry)
    {
        lock (_lock)
        {
            _entries[entry.Id] = entry;
            Trim();
        }
    }

    public void AddRange(IEnumerable<AuditEntry> entries)
    {
        lock (_lock)
        {
            foreach (AuditEntry entry in entries)
            {
                _entries[entry.Id] = entry;
            }

            Trim();
        }
    }

    public int Delete(IEnumerable<Guid> ids)
    {
        lock (_lock)
        {
            int deleted = 0;
            foreach (Guid id in ids.Distinct())
            {
                if (_entries.Remove(id))
                {
                    deleted++;
                }
            }

            return deleted;
        }
    }

    public AuditEntry? Get(Guid id)
    {
        lock (_lock)
            return _entries.GetValueOrDefault(id);
    }

    public IReadOnlyCollection<AuditEntry> All
    {
        get
        {
            lock (_lock)
                return _entries.Values.OrderByDescending(e => e.Timestamp).ToArray();
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
                return _entries.Count;
        }
    }

    public IReadOnlyDictionary<int, int> StatusCounts
    {
        get
        {
            lock (_lock)
                return _entries.Values
                    .Where(entry => entry.StatusCode.HasValue)
                    .GroupBy(entry => entry.StatusCode!.Value)
                    .ToDictionary(group => group.Key, group => group.Count());
        }
    }

    public AuditEntry? Latest()
    {
        lock (_lock)
            return _entries.Values.OrderByDescending(e => e.Timestamp).FirstOrDefault();
    }

    public IReadOnlyCollection<AuditEntry> Snapshot()
    {
        lock (_lock)
            return _entries.Values.ToArray();
    }

    private void Trim()
    {
        TimeSpan? retention = options.Value.RetentionPeriod;
        if (retention.HasValue)
        {
            DateTimeOffset cutoff = DateTimeOffset.UtcNow - retention.Value;
            foreach (Guid id in _entries.Values
                         .Where(e => e.Timestamp < cutoff)
                         .Select(e => e.Id)
                         .ToArray())
            {
                _entries.Remove(id);
            }
        }

        int max = options.Value.MaxStoredAuditEntries;
        if (max == 0 || _entries.Count <= max)
        {
            return;
        }

        foreach (Guid id in _entries.Values
                     .OrderBy(e => e.Timestamp)
                     .Take(_entries.Count - max)
                     .Select(e => e.Id))
        {
            _entries.Remove(id);
        }
    }
}
