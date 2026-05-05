using Microsoft.Extensions.Options;
using Vakthund.Shared.Models;
using Vakthund.UI.Options;

namespace Vakthund.UI.Services;

public class AuditStore(IOptions<VakthundOptions> options)
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
                _entries[entry.Id] = entry;
            Trim();
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
        int max = options.Value.MaxAuditEntries;
        if (_entries.Count <= max)
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
