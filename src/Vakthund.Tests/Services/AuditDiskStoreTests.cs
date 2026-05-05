using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Vakthund.Shared.Models;
using Vakthund.UI.Options;
using Vakthund.UI.Services;

namespace Vakthund.Tests.Services;

public class AuditDiskStoreTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"vakthund_test_{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private AuditDiskStore Store(string retention = "", int max = 0) =>
        new(Options.Create(new UiOptions
        {
            StoragePath = _dbPath,
            Retention = retention,
            MaxStoredAuditEntries = max
        }));

    [Fact]
    public void Add_Get_RoundTripsAllFields()
    {
        AuditEntry entry = new()
        {
            Timestamp = DateTimeOffset.UtcNow,
            Scheme = "https",
            Host = "example.com",
            Path = "/api/test",
            Query = "?foo=bar",
            Method = "POST",
            Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer token" },
            Cookies = new Dictionary<string, string> { ["session"] = "abc" },
            Queries = new Dictionary<string, string> { ["foo"] = "bar" },
            ContentType = "application/json",
            Body = """{"key":"value"}""",
            ResponseContentType = "application/json",
            ResponseBody = """{"result":"ok"}""",
            StatusCode = 200,
            TargetDurationMs = 500,
            DurationMs = 123
        };

        Store().Add(entry);
        AuditEntry? result = Store().Get(entry.Id);

        Assert.NotNull(result);
        Assert.Equal(entry.Id, result.Id);
        Assert.Equal(entry.Timestamp, result.Timestamp);
        Assert.Equal(entry.Scheme, result.Scheme);
        Assert.Equal(entry.Host, result.Host);
        Assert.Equal(entry.Path, result.Path);
        Assert.Equal(entry.Query, result.Query);
        Assert.Equal(entry.Method, result.Method);
        Assert.Equal(entry.Headers, result.Headers);
        Assert.Equal(entry.Cookies, result.Cookies);
        Assert.Equal(entry.Queries, result.Queries);
        Assert.Equal(entry.ContentType, result.ContentType);
        Assert.Equal(entry.Body, result.Body);
        Assert.Equal(entry.ResponseContentType, result.ResponseContentType);
        Assert.Equal(entry.ResponseBody, result.ResponseBody);
        Assert.Equal(entry.StatusCode, result.StatusCode);
        Assert.Equal(entry.TargetDurationMs, result.TargetDurationMs);
        Assert.Equal(entry.DurationMs, result.DurationMs);
    }

    [Fact]
    public void Add_Get_RoundTripsNullableFieldsAsNull()
    {
        AuditEntry entry = Entry();

        Store().Add(entry);
        AuditEntry? result = Store().Get(entry.Id);

        Assert.NotNull(result);
        Assert.Null(result.Host);
        Assert.Null(result.Query);
        Assert.Null(result.ContentType);
        Assert.Null(result.Body);
        Assert.Null(result.ResponseContentType);
        Assert.Null(result.ResponseBody);
        Assert.Null(result.StatusCode);
        Assert.Null(result.TargetDurationMs);
        Assert.Null(result.MatchedRoute);
    }

    [Fact]
    public void Add_Get_RoundTripsMatchedRoute()
    {
        AuditEntry entry = Entry();
        entry.MatchedRoute = new ProxyRouteInfo
        {
            Path = "/api/**",
            Target = "https://backend.internal",
            Auth = new AuthExpectation
            {
                Issuer = "https://auth.example.com",
                Audience = "my-api",
                Scopes = ["read:users"],
                Jwe = new JweDecryptionConfig { KeyType = JweKeyType.Symmetric, Key = "secret" }
            }
        };

        Store().Add(entry);
        AuditEntry? result = Store().Get(entry.Id);

        Assert.NotNull(result?.MatchedRoute);
        Assert.Equal("/api/**", result.MatchedRoute.Path);
        Assert.Equal("https://backend.internal", result.MatchedRoute.Target);
        Assert.NotNull(result.MatchedRoute.Auth);
        Assert.Equal("https://auth.example.com", result.MatchedRoute.Auth.Issuer);
        Assert.Equal("my-api", result.MatchedRoute.Auth.Audience);
        Assert.Equal(["read:users"], result.MatchedRoute.Auth.Scopes);
        Assert.Equal(JweKeyType.Symmetric, result.MatchedRoute.Auth.Jwe.KeyType);
        Assert.Equal("secret", result.MatchedRoute.Auth.Jwe.Key);
    }

    [Fact]
    public void Get_ReturnsNull_WhenNotFound()
    {
        Assert.Null(Store().Get(Guid.NewGuid()));
    }

    [Fact]
    public void All_ReturnsEntriesOrderedByTimestampDescending()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        AuditEntry oldest = Entry(now.AddMinutes(-2));
        AuditEntry newest = Entry(now);
        AuditEntry middle = Entry(now.AddMinutes(-1));

        Store().AddRange([oldest, newest, middle]);

        Assert.Equal([newest.Id, middle.Id, oldest.Id], Store().All.Select(e => e.Id));
    }

    [Fact]
    public void Count_ReturnsNumberOfStoredEntries()
    {
        Store().AddRange([Entry(), Entry(), Entry()]);

        Assert.Equal(3, Store().Count);
    }

    [Fact]
    public void Delete_RemovesSelectedEntries()
    {
        AuditDiskStore store = Store();
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
        AuditDiskStore store = Store();
        store.AddRange([Entry(statusCode: 200), Entry(statusCode: 200), Entry(statusCode: 500), Entry(statusCode: null)]);

        IReadOnlyDictionary<int, int> statusCounts = store.StatusCounts;

        Assert.Equal(2, statusCounts[200]);
        Assert.Equal(1, statusCounts[500]);
        Assert.False(statusCounts.ContainsKey(0));
    }

    [Fact]
    public void Latest_ReturnsMostRecentEntry()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        AuditEntry newest = Entry(now);

        Store().AddRange([Entry(now.AddMinutes(-2)), newest, Entry(now.AddMinutes(-1))]);

        Assert.Equal(newest.Id, Store().Latest()?.Id);
    }

    [Fact]
    public void AddRange_ReplacesEntryWithSameId()
    {
        Guid id = Guid.NewGuid();

        Store().AddRange([Entry(path: "/before", id: id), Entry(path: "/after", id: id)]);

        AuditEntry stored = Assert.Single(Store().All);
        Assert.Equal("/after", stored.Path);
    }

    [Fact]
    public void Add_TrimsOldestEntries_WhenCapacityExceeded()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        AuditEntry oldest = Entry(now.AddMinutes(-3));
        AuditEntry newest = Entry(now);
        AuditEntry middle = Entry(now.AddMinutes(-1));
        AuditDiskStore store = Store(max: 2);

        store.Add(oldest);
        store.Add(newest);
        store.Add(middle);

        Assert.Null(store.Get(oldest.Id));
        Assert.Equal([newest.Id, middle.Id], store.All.Select(e => e.Id));
    }

    [Fact]
    public void Add_RemovesEntriesOlderThanRetention()
    {
        AuditEntry recent = Entry(DateTimeOffset.UtcNow.AddMinutes(-30));
        AuditEntry expired = Entry(DateTimeOffset.UtcNow.AddHours(-2));
        AuditDiskStore store = Store(retention: "1h");

        store.Add(recent);
        store.Add(expired);

        Assert.NotNull(store.Get(recent.Id));
        Assert.Null(store.Get(expired.Id));
    }

    [Fact]
    public void EnsureCreated_MigratesExistingDatabaseWithoutMatchedRouteJson()
    {
        using (SqliteConnection conn = new($"Data Source={_dbPath}"))
        {
            conn.Open();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = """
                              CREATE TABLE AuditEntries (
                                  Id TEXT NOT NULL PRIMARY KEY,
                                  Timestamp TEXT NOT NULL,
                                  Scheme TEXT NOT NULL,
                                  Host TEXT NULL,
                                  Path TEXT NOT NULL,
                                  Query TEXT NULL,
                                  Method TEXT NOT NULL,
                                  HeadersJson TEXT NOT NULL,
                                  CookiesJson TEXT NOT NULL,
                                  QueriesJson TEXT NOT NULL,
                                  ContentType TEXT NULL,
                                  Body TEXT NULL,
                                  ResponseContentType TEXT NULL,
                                  ResponseBody TEXT NULL,
                                  StatusCode INTEGER NULL,
                                  TargetDurationMs INTEGER NULL,
                                  DurationMs INTEGER NOT NULL
                              );
                              """;
            cmd.ExecuteNonQuery();
        }

        AuditEntry entry = Entry();
        entry.MatchedRoute = new ProxyRouteInfo { Path = "/api/**", Target = "https://backend" };
        AuditDiskStore store = Store();

        store.Add(entry);

        Assert.Equal("/api/**", store.Get(entry.Id)?.MatchedRoute?.Path);
    }

    private static AuditEntry Entry(
        DateTimeOffset? timestamp = null,
        string path = "/api",
        Guid? id = null,
        int? statusCode = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            Scheme = "https",
            Host = null,
            Path = path,
            Method = "GET",
            StatusCode = statusCode
        };
}
