using Microsoft.Extensions.Options;
using Vakthund.Shared.Models;
using Vakthund.UI.Options;
using Vakthund.UI.Services;

namespace Vakthund.Tests.Services;

public class AuditStoreTests
{
    [Fact]
    public void Add_TrimsOldestEntries_WhenCapacityIsExceeded()
    {
        var store = new AuditStore(Options.Create(new VakthundOptions { MaxAuditEntries = 2 }));
        AuditEntry oldest = Entry(timestamp: DateTimeOffset.UtcNow.AddMinutes(-3));
        AuditEntry newest = Entry(timestamp: DateTimeOffset.UtcNow);
        AuditEntry middle = Entry(timestamp: DateTimeOffset.UtcNow.AddMinutes(-1));

        store.Add(oldest);
        store.Add(newest);
        store.Add(middle);

        Assert.Null(store.Get(oldest.Id));
        Assert.Equal([newest.Id, middle.Id], store.All.Select(entry => entry.Id));
    }

    [Fact]
    public void AddRange_ReplacesEntriesWithSameId()
    {
        var store = new AuditStore(Options.Create(new VakthundOptions { MaxAuditEntries = 10 }));
        var id = Guid.NewGuid();

        store.AddRange(
        [
            Entry(id, path: "/before"),
            Entry(id, path: "/after")
        ]);

        AuditEntry stored = Assert.Single(store.All);
        Assert.Equal("/after", stored.Path);
    }

    private static AuditEntry Entry(
        Guid? id = null,
        DateTimeOffset? timestamp = null,
        string path = "/requests") =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            Scheme = "https",
            Host = "example.test",
            Path = path,
            Method = "GET"
        };
}
