using Vakthund.Shared.Models;

namespace Vakthund.UI.Services.Interfaces;

public interface IAuditStore
{
    void Add(AuditEntry entry);
    void AddRange(IEnumerable<AuditEntry> entries);
    int Delete(IEnumerable<Guid> ids);
    AuditEntry? Get(Guid id);
    IReadOnlyCollection<AuditEntry> All { get; }
    int Count { get; }
    IReadOnlyDictionary<int, int> StatusCounts { get; }
    AuditEntry? Latest();
    IReadOnlyCollection<AuditEntry> Snapshot();
}
