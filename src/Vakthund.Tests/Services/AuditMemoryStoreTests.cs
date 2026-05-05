using Microsoft.Extensions.Options;
using Vakthund.Shared.Models;
using Vakthund.UI.Options;
using Vakthund.UI.Services;

namespace Vakthund.Tests.Services;

public class AuditMemoryStoreTests
{
    [Fact]
    public void Add_TrimsOldestEntries_WhenCapacityIsExceeded()
    {
        var store = new AuditMemoryStore(Options.Create(new UiOptions { MaxStoredAuditEntries = 2 }));
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
        var store = new AuditMemoryStore(Options.Create(new UiOptions { MaxStoredAuditEntries = 10 }));
        var id = Guid.NewGuid();

        store.AddRange(
        [
            Entry(id, path: "/before"),
            Entry(id, path: "/after")
        ]);

        AuditEntry stored = Assert.Single(store.All);
        Assert.Equal("/after", stored.Path);
    }

    [Fact]
    public void Delete_RemovesSelectedEntries()
    {
        var store = new AuditMemoryStore(Options.Create(new UiOptions { MaxStoredAuditEntries = 10 }));
        AuditEntry keep = Entry();
        AuditEntry remove = Entry();
        store.AddRange([keep, remove]);

        int deleted = store.Delete([remove.Id, Guid.NewGuid()]);

        Assert.Equal(1, deleted);
        Assert.NotNull(store.Get(keep.Id));
        Assert.Null(store.Get(remove.Id));
    }

    [Fact]
    public void StatusCounts_GroupsStoredStatusCodes()
    {
        var store = new AuditMemoryStore(Options.Create(new UiOptions { MaxStoredAuditEntries = 10 }));
        store.AddRange([Entry(statusCode: 200), Entry(statusCode: 200), Entry(statusCode: 500), Entry(statusCode: null)]);

        IReadOnlyDictionary<int, int> statusCounts = store.StatusCounts;

        Assert.Equal(2, statusCounts[200]);
        Assert.Equal(1, statusCounts[500]);
        Assert.False(statusCounts.ContainsKey(0));
    }

    private static AuditEntry Entry(
        Guid? id = null,
        DateTimeOffset? timestamp = null,
        string path = "/requests",
        int? statusCode = 200) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            Scheme = "https",
            Host = "example.test",
            Path = path,
            Method = "GET",
            StatusCode = statusCode
        };
}
