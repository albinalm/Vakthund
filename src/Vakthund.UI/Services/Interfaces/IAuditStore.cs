using Vakthund.Shared.Models;

namespace Vakthund.UI.Services.Interfaces;

public interface IAuditStore
{
    void Add(AuditEntry entry);
    void AddRange(IEnumerable<AuditEntry> entries);
    AuditEntry? Get(Guid id);
    IReadOnlyCollection<AuditEntry> All { get; }
    int Count { get; }
    AuditEntry? Latest();
    IReadOnlyCollection<AuditEntry> Snapshot();
}