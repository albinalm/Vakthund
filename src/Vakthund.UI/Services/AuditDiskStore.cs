using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Vakthund.Shared.Models;
using Vakthund.UI.Options;
using Vakthund.UI.Services.Interfaces;

namespace Vakthund.UI.Services;

public class AuditDiskStore(IOptions<UiOptions> options) : IAuditStore
{
    private readonly Lock _lock = new();
    private bool _initialized;
    private string? _connectionString;

    private string ConnectionString
    {
        get
        {
            if (_connectionString is not null)
            {
                return _connectionString;
            }

            string configuredPath = options.Value.StoragePath;
            string rawPath = !string.IsNullOrWhiteSpace(configuredPath)
                ? configuredPath
                : "data/audit.db";
            string dbPath = Path.IsPathRooted(rawPath)
                ? rawPath
                : Path.Combine(AppContext.BaseDirectory, rawPath);

            string? directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString();
            return _connectionString;
        }
    }

    public void Add(AuditEntry entry)
    {
        lock (_lock)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Upsert(connection, transaction, entry);
            Trim(connection, transaction);

            transaction.Commit();
        }
    }

    public void AddRange(IEnumerable<AuditEntry> entries)
    {
        lock (_lock)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            foreach (AuditEntry entry in entries)
            {
                Upsert(connection, transaction, entry);
            }

            Trim(connection, transaction);

            transaction.Commit();
        }
    }

    public int Delete(IEnumerable<Guid> ids)
    {
        string[] idsToDelete = ids
            .Distinct()
            .Select(id => id.ToString())
            .ToArray();

        if (idsToDelete.Length == 0)
        {
            return 0;
        }

        lock (_lock)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            int deleted = 0;

            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM AuditEntries WHERE Id = $id;";
            SqliteParameter idParameter = command.Parameters.Add("$id", SqliteType.Text);

            foreach (string id in idsToDelete)
            {
                idParameter.Value = id;
                deleted += command.ExecuteNonQuery();
            }

            transaction.Commit();
            return deleted;
        }
    }

    public AuditEntry? Get(Guid id)
    {
        lock (_lock)
        {
            using SqliteConnection connection = OpenConnection();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                                  SELECT *
                                  FROM AuditEntries
                                  WHERE Id = $id
                                  LIMIT 1;
                                  """;

            command.Parameters.AddWithValue("$id", id.ToString());

            using SqliteDataReader reader = command.ExecuteReader();

            return reader.Read()
                ? ReadEntry(reader)
                : null;
        }
    }

    public IReadOnlyCollection<AuditEntry> All
    {
        get
        {
            lock (_lock)
            {
                using SqliteConnection connection = OpenConnection();

                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                                      SELECT *
                                      FROM AuditEntries
                                      ORDER BY Timestamp DESC;
                                      """;

                return ReadEntries(command);
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                using SqliteConnection connection = OpenConnection();

                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                                      SELECT COUNT(*)
                                      FROM AuditEntries;
                                      """;

                return Convert.ToInt32(command.ExecuteScalar());
            }
        }
    }

    public IReadOnlyDictionary<int, int> StatusCounts
    {
        get
        {
            lock (_lock)
            {
                using SqliteConnection connection = OpenConnection();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                                      SELECT StatusCode, COUNT(*)
                                      FROM AuditEntries
                                      WHERE StatusCode IS NOT NULL AND Upstreamed = 1
                                      GROUP BY StatusCode;
                                      """;

                Dictionary<int, int> statusCounts = [];
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    statusCounts[reader.GetInt32(0)] = Convert.ToInt32(reader.GetInt64(1));
                }

                return statusCounts;
            }
        }
    }

    public AuditEntry? Latest()
    {
        lock (_lock)
        {
            using SqliteConnection connection = OpenConnection();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                                  SELECT *
                                  FROM AuditEntries
                                  ORDER BY Timestamp DESC
                                  LIMIT 1;
                                  """;

            using SqliteDataReader reader = command.ExecuteReader();

            return reader.Read()
                ? ReadEntry(reader)
                : null;
        }
    }

    public IReadOnlyCollection<AuditEntry> Snapshot()
    {
        lock (_lock)
        {
            using SqliteConnection connection = OpenConnection();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                                  SELECT *
                                  FROM AuditEntries;
                                  """;

            return ReadEntries(command);
        }
    }

    private SqliteConnection OpenConnection()
    {
        SqliteConnection connection = new(ConnectionString);
        connection.Open();

        if (!_initialized)
        {
            EnsureCreated(connection);
            _initialized = true;
        }

        return connection;
    }

    private static void EnsureCreated(SqliteConnection connection)
    {
        using SqliteCommand create = connection.CreateCommand();
        create.CommandText = """
                             CREATE TABLE IF NOT EXISTS AuditEntries
                             (
                                 Id TEXT NOT NULL PRIMARY KEY,
                                 Timestamp TEXT NOT NULL,
                                 Scheme TEXT NOT NULL,
                                 Host TEXT NULL,
                                 IncomingHost TEXT NULL,
                                 Path TEXT NOT NULL,
                                 Query TEXT NULL,
                                 Method TEXT NOT NULL,
                                 ClientIp TEXT NULL,
                                 HeadersJson TEXT NOT NULL,
                                 CookiesJson TEXT NOT NULL,
                                 QueriesJson TEXT NOT NULL,
                                 ContentType TEXT NULL,
                                 Body TEXT NULL,
                                 ResponseContentType TEXT NULL,
                                 ResponseBody TEXT NULL,
                                 StatusCode INTEGER NULL,
                                 TargetDurationMs INTEGER NULL,
                                 DurationMs INTEGER NOT NULL,
                                 MatchedRouteJson TEXT NULL,
                                 Upstreamed INTEGER NOT NULL DEFAULT 1
                             );

                             CREATE INDEX IF NOT EXISTS IX_AuditEntries_Timestamp
                             ON AuditEntries (Timestamp DESC);
                             """;
        create.ExecuteNonQuery();

        try
        {
            using SqliteCommand migrate = connection.CreateCommand();
            migrate.CommandText = "ALTER TABLE AuditEntries ADD COLUMN MatchedRouteJson TEXT NULL;";
            migrate.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // Column already exists in pre-existing databases.
        }

        try
        {
            using SqliteCommand migrate = connection.CreateCommand();
            migrate.CommandText = "ALTER TABLE AuditEntries ADD COLUMN ClientIp TEXT NULL;";
            migrate.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // Column already exists in pre-existing databases.
        }

        try
        {
            using SqliteCommand migrate = connection.CreateCommand();
            migrate.CommandText = "ALTER TABLE AuditEntries ADD COLUMN Upstreamed INTEGER NOT NULL DEFAULT 1;";
            migrate.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // Column already exists in pre-existing databases.
        }

        try
        {
            using SqliteCommand migrate = connection.CreateCommand();
            migrate.CommandText = "ALTER TABLE AuditEntries ADD COLUMN IncomingHost TEXT NULL;";
            migrate.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // Column already exists in pre-existing databases.
        }
    }

    private static void Upsert(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AuditEntry entry)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;

        command.CommandText = """
                              INSERT INTO AuditEntries
                              (
                                  Id,
                                  Timestamp,
                                  Scheme,
                                  Host,
                                  IncomingHost,
                                  Path,
                                  Query,
                                  Method,
                                  ClientIp,
                                  HeadersJson,
                                  CookiesJson,
                                  QueriesJson,
                                  ContentType,
                                  Body,
                                  ResponseContentType,
                                  ResponseBody,
                                  StatusCode,
                                  TargetDurationMs,
                                  DurationMs,
                                  MatchedRouteJson,
                                  Upstreamed
                              )
                              VALUES
                              (
                                  $id,
                                  $timestamp,
                                  $scheme,
                                  $host,
                                  $incomingHost,
                                  $path,
                                  $query,
                                  $method,
                                  $clientIp,
                                  $headersJson,
                                  $cookiesJson,
                                  $queriesJson,
                                  $contentType,
                                  $body,
                                  $responseContentType,
                                  $responseBody,
                                  $statusCode,
                                  $targetDurationMs,
                                  $durationMs,
                                  $matchedRouteJson,
                                  $upstreamed
                              )
                              ON CONFLICT(Id) DO UPDATE SET
                                  Timestamp = excluded.Timestamp,
                                  Scheme = excluded.Scheme,
                                  Host = excluded.Host,
                                  IncomingHost = excluded.IncomingHost,
                                  Path = excluded.Path,
                                  Query = excluded.Query,
                                  Method = excluded.Method,
                                  ClientIp = excluded.ClientIp,
                                  HeadersJson = excluded.HeadersJson,
                                  CookiesJson = excluded.CookiesJson,
                                  QueriesJson = excluded.QueriesJson,
                                  ContentType = excluded.ContentType,
                                  Body = excluded.Body,
                                  ResponseContentType = excluded.ResponseContentType,
                                  ResponseBody = excluded.ResponseBody,
                                  StatusCode = excluded.StatusCode,
                                  TargetDurationMs = excluded.TargetDurationMs,
                                  DurationMs = excluded.DurationMs,
                                  MatchedRouteJson = excluded.MatchedRouteJson,
                                  Upstreamed = excluded.Upstreamed;
                              """;

        command.Parameters.AddWithValue("$id", entry.Id.ToString());
        command.Parameters.AddWithValue("$timestamp", entry.Timestamp.ToString("O"));
        command.Parameters.AddWithValue("$scheme", entry.Scheme);
        command.Parameters.AddWithValue("$host", ToDbValue(entry.Host));
        command.Parameters.AddWithValue("$incomingHost", ToDbValue(entry.IncomingHost));
        command.Parameters.AddWithValue("$path", entry.Path);
        command.Parameters.AddWithValue("$query", ToDbValue(entry.Query));
        command.Parameters.AddWithValue("$method", entry.Method);
        command.Parameters.AddWithValue("$clientIp", ToDbValue(entry.ClientIp));
        command.Parameters.AddWithValue("$headersJson", JsonSerializer.Serialize(entry.Headers));
        command.Parameters.AddWithValue("$cookiesJson", JsonSerializer.Serialize(entry.Cookies));
        command.Parameters.AddWithValue("$queriesJson", JsonSerializer.Serialize(entry.Queries));
        command.Parameters.AddWithValue("$contentType", ToDbValue(entry.ContentType));
        command.Parameters.AddWithValue("$body", ToDbValue(entry.Body));
        command.Parameters.AddWithValue("$responseContentType", ToDbValue(entry.ResponseContentType));
        command.Parameters.AddWithValue("$responseBody", ToDbValue(entry.ResponseBody));
        command.Parameters.AddWithValue("$statusCode", ToDbValue(entry.StatusCode));
        command.Parameters.AddWithValue("$targetDurationMs", ToDbValue(entry.TargetDurationMs));
        command.Parameters.AddWithValue("$durationMs", entry.DurationMs);
        command.Parameters.AddWithValue("$matchedRouteJson",
            entry.MatchedRoute is not null ? JsonSerializer.Serialize(entry.MatchedRoute) : DBNull.Value);
        command.Parameters.AddWithValue("$upstreamed", entry.Upstreamed ? 1 : 0);

        command.ExecuteNonQuery();
    }

    private void Trim(SqliteConnection connection, SqliteTransaction transaction)
    {
        TimeSpan? retention = options.Value.RetentionPeriod;
        if (retention.HasValue)
        {
            using SqliteCommand retentionCmd = connection.CreateCommand();
            retentionCmd.Transaction = transaction;
            retentionCmd.CommandText = "DELETE FROM AuditEntries WHERE Timestamp < $cutoff;";
            retentionCmd.Parameters.AddWithValue("$cutoff",
                DateTimeOffset.UtcNow.Subtract(retention.Value).ToString("O"));
            retentionCmd.ExecuteNonQuery();
        }

        int max = options.Value.MaxStoredAuditEntries;

        if (max == 0)
        {
            return;
        }

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;

        command.CommandText = """
                              DELETE FROM AuditEntries
                              WHERE Id IN
                              (
                                  SELECT Id
                                  FROM AuditEntries
                                  ORDER BY Timestamp ASC
                                  LIMIT
                                  (
                                      SELECT MAX(COUNT(*) - $max, 0)
                                      FROM AuditEntries
                                  )
                              );
                              """;

        command.Parameters.AddWithValue("$max", max);

        command.ExecuteNonQuery();
    }

    private static IReadOnlyCollection<AuditEntry> ReadEntries(SqliteCommand command)
    {
        List<AuditEntry> entries = [];

        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
            entries.Add(ReadEntry(reader));

        return entries;
    }

    private static AuditEntry ReadEntry(SqliteDataReader reader)
    {
        return new AuditEntry
        {
            Id = Guid.Parse(reader.GetString(reader.GetOrdinal("Id"))),
            Timestamp = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("Timestamp"))),
            Scheme = reader.GetString(reader.GetOrdinal("Scheme")),
            Host = GetNullableString(reader, "Host"),
            IncomingHost = GetNullableString(reader, "IncomingHost"),
            Path = reader.GetString(reader.GetOrdinal("Path")),
            Query = GetNullableString(reader, "Query"),
            Method = reader.GetString(reader.GetOrdinal("Method")),
            ClientIp = GetNullableString(reader, "ClientIp"),
            Headers = DeserializeDictionary(reader.GetString(reader.GetOrdinal("HeadersJson"))),
            Cookies = DeserializeDictionary(reader.GetString(reader.GetOrdinal("CookiesJson"))),
            Queries = DeserializeDictionary(reader.GetString(reader.GetOrdinal("QueriesJson"))),
            ContentType = GetNullableString(reader, "ContentType"),
            Body = GetNullableString(reader, "Body"),
            ResponseContentType = GetNullableString(reader, "ResponseContentType"),
            ResponseBody = GetNullableString(reader, "ResponseBody"),
            StatusCode = GetNullableInt(reader, "StatusCode"),
            TargetDurationMs = GetNullableLong(reader, "TargetDurationMs"),
            DurationMs = reader.GetInt64(reader.GetOrdinal("DurationMs")),
            MatchedRoute = DeserializeRoute(GetNullableString(reader, "MatchedRouteJson")),
            Upstreamed = reader.GetInt64(reader.GetOrdinal("Upstreamed")) != 0
        };
    }

    private static Dictionary<string, string> DeserializeDictionary(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];

    private static ProxyRouteInfo? DeserializeRoute(string? json) =>
        json is not null ? JsonSerializer.Deserialize<ProxyRouteInfo>(json) : null;

    private static string? GetNullableString(SqliteDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);

        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetString(ordinal);
    }

    private static int? GetNullableInt(SqliteDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);

        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetInt32(ordinal);
    }

    private static long? GetNullableLong(SqliteDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);

        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetInt64(ordinal);
    }

    private static object ToDbValue<T>(T? value)
    {
        return value is null
            ? DBNull.Value
            : value;
    }
}
