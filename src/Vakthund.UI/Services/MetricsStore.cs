using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Vakthund.Shared.Models;
using Vakthund.UI.Enums;
using Vakthund.UI.Options;

namespace Vakthund.UI.Services;

public class MetricsStore(IOptions<UiOptions> options)
{
    private static readonly TimeSpan MinBucketWindow = TimeSpan.FromHours(1);
    private const int RecentWindowSeconds = 120;

    private readonly Dictionary<DateTime, MetricsBucket> _minuteBuckets = [];
    private readonly Dictionary<DateTime, int> _secondBuckets = [];
    private readonly Lock _lock = new();
    private bool _initialized;
    private string? _connectionString;
    private DateTimeOffset? _latestTimestamp;

    private bool PersistMetrics => options.Value.StorageMode == StorageMode.Disk;

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

    public void InitializeFromDisk(IReadOnlyCollection<AuditEntry> fallbackEntries)
    {
        if (!PersistMetrics)
        {
            return;
        }

        bool seedFromAuditEntries = false;

        lock (_lock)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            MetricsSnapshot persisted = LoadPersistedSnapshot(connection, transaction);
            bool hasPersistedMetrics = persisted.MinuteBuckets.Count > 0 || persisted.SecondBuckets.Count > 0;

            if (hasPersistedMetrics)
            {
                LoadSnapshot(persisted);
                Trim();
                TrimPersisted(connection, transaction);
            }
            else
            {
                seedFromAuditEntries = fallbackEntries.Count > 0;
            }

            transaction.Commit();
        }

        if (seedFromAuditEntries)
        {
            AddRange(fallbackEntries);
        }
    }

    public void AddRange(IEnumerable<AuditEntry> entries)
    {
        AuditEntry[] entriesArray = entries.ToArray();
        if (entriesArray.Length == 0)
        {
            return;
        }

        lock (_lock)
        {
            foreach (AuditEntry entry in entriesArray)
            {
                AddEntryToMemory(entry);
            }

            Trim();

            if (PersistMetrics)
            {
                using SqliteConnection connection = OpenConnection();
                using SqliteTransaction transaction = connection.BeginTransaction();

                PersistEntries(connection, transaction, entriesArray);
                TrimPersisted(connection, transaction);

                transaction.Commit();
            }
        }
    }

    public MetricsSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new MetricsSnapshot(
                _latestTimestamp,
                _minuteBuckets.Values
                    .Select(bucket => bucket.ToSnapshot())
                    .OrderBy(bucket => bucket.Start)
                    .ToArray(),
                new Dictionary<DateTime, int>(_secondBuckets));
        }
    }

    public void AddLoss(AuditLossSummary loss)
    {
        lock (_lock)
        {
            foreach (AuditLossBucket lossBucket in loss.MinuteBuckets)
            {
                DateTime minute = MinuteBucket(lossBucket.Start);
                if (!_minuteBuckets.TryGetValue(minute, out MetricsBucket? minuteBucket))
                {
                    minuteBucket = new MetricsBucket(minute);
                    _minuteBuckets[minute] = minuteBucket;
                }

                minuteBucket.AddLoss(lossBucket);

                if (_latestTimestamp is null || lossBucket.Start > _latestTimestamp.Value)
                {
                    _latestTimestamp = lossBucket.Start;
                }
            }

            foreach (AuditLossSecondBucket secondBucket in loss.SecondBuckets)
            {
                DateTime second = SecondBucket(secondBucket.Start);
                _secondBuckets[second] = _secondBuckets.GetValueOrDefault(second) + secondBucket.Count;

                if (_latestTimestamp is null || secondBucket.Start > _latestTimestamp.Value)
                {
                    _latestTimestamp = secondBucket.Start;
                }
            }

            Trim();

            if (PersistMetrics)
            {
                using SqliteConnection connection = OpenConnection();
                using SqliteTransaction transaction = connection.BeginTransaction();

                PersistLoss(connection, transaction, loss);
                TrimPersisted(connection, transaction);

                transaction.Commit();
            }
        }
    }

    private void AddEntryToMemory(AuditEntry entry)
    {
        if (_latestTimestamp is null || entry.Timestamp > _latestTimestamp.Value)
        {
            _latestTimestamp = entry.Timestamp;
        }

        DateTime minute = MinuteBucket(entry.Timestamp);
        if (!_minuteBuckets.TryGetValue(minute, out MetricsBucket? minuteBucket))
        {
            minuteBucket = new MetricsBucket(minute);
            _minuteBuckets[minute] = minuteBucket;
        }

        minuteBucket.Add(entry);

        DateTime second = SecondBucket(entry.Timestamp);
        _secondBuckets[second] = _secondBuckets.GetValueOrDefault(second) + 1;
    }

    private void LoadSnapshot(MetricsSnapshot snapshot)
    {
        _minuteBuckets.Clear();
        _secondBuckets.Clear();
        _latestTimestamp = snapshot.LatestTimestamp;

        foreach (MetricsBucketSnapshot bucket in snapshot.MinuteBuckets)
        {
            _minuteBuckets[bucket.Start] = MetricsBucket.FromSnapshot(bucket);
        }

        foreach ((DateTime start, int count) in snapshot.SecondBuckets)
        {
            _secondBuckets[start] = count;
        }
    }

    private void Trim()
    {
        TimeSpan bucketWindow = MetricsRetentionWindow();

        DateTime minuteCutoff = MinuteBucket(DateTimeOffset.UtcNow - bucketWindow);
        foreach (DateTime minute in _minuteBuckets.Keys.Where(m => m < minuteCutoff).ToArray())
        {
            _minuteBuckets.Remove(minute);
        }

        DateTime secondCutoff = SecondBucket(DateTimeOffset.UtcNow).AddSeconds(-RecentWindowSeconds + 1);
        foreach (DateTime second in _secondBuckets.Keys.Where(s => s < secondCutoff).ToArray())
        {
            _secondBuckets.Remove(second);
        }
    }

    private TimeSpan MetricsRetentionWindow() => options.Value.RetentionPeriod ?? MinBucketWindow;

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
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
                              CREATE TABLE IF NOT EXISTS MetricMinuteBuckets
                              (
                                  Start TEXT NOT NULL PRIMARY KEY,
                                  Count INTEGER NOT NULL,
                                  DurationSumMs INTEGER NOT NULL,
                                  TargetDurationSumMs INTEGER NOT NULL,
                                  TargetCount INTEGER NOT NULL,
                                  ErrorCount INTEGER NOT NULL,
                                  LostCount INTEGER NOT NULL,
                                  StatusCountsJson TEXT NOT NULL
                              );

                              CREATE TABLE IF NOT EXISTS MetricSecondBuckets
                              (
                                  Start TEXT NOT NULL PRIMARY KEY,
                                  Count INTEGER NOT NULL
                              );
                              """;
        command.ExecuteNonQuery();
    }

    private MetricsSnapshot LoadPersistedSnapshot(SqliteConnection connection, SqliteTransaction transaction)
    {
        List<MetricsBucketSnapshot> minuteBuckets = [];
        Dictionary<DateTime, int> secondBuckets = [];
        DateTimeOffset? latest = null;

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                                  SELECT Start, Count, DurationSumMs, TargetDurationSumMs, TargetCount, ErrorCount, LostCount, StatusCountsJson
                                  FROM MetricMinuteBuckets
                                  ORDER BY Start;
                                  """;

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                DateTime start = ParseBucketStart(reader.GetString(0));
                latest = Max(latest, new DateTimeOffset(start));
                minuteBuckets.Add(new MetricsBucketSnapshot(
                    start,
                    reader.GetInt32(1),
                    reader.GetInt64(2),
                    reader.GetInt64(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetInt32(6),
                    DeserializeStatusCounts(reader.GetString(7))));
            }
        }

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                                  SELECT Start, Count
                                  FROM MetricSecondBuckets
                                  ORDER BY Start;
                                  """;

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                DateTime start = ParseBucketStart(reader.GetString(0));
                latest = Max(latest, new DateTimeOffset(start));
                secondBuckets[start] = reader.GetInt32(1);
            }
        }

        return new MetricsSnapshot(latest, minuteBuckets, secondBuckets);
    }

    private void PersistEntries(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<AuditEntry> entries)
    {
        Dictionary<DateTime, MetricsBucket> minuteBuckets = [];
        Dictionary<DateTime, int> secondBuckets = [];

        foreach (AuditEntry entry in entries)
        {
            DateTime minute = MinuteBucket(entry.Timestamp);
            if (!minuteBuckets.TryGetValue(minute, out MetricsBucket? minuteBucket))
            {
                minuteBucket = new MetricsBucket(minute);
                minuteBuckets[minute] = minuteBucket;
            }

            minuteBucket.Add(entry);

            DateTime second = SecondBucket(entry.Timestamp);
            secondBuckets[second] = secondBuckets.GetValueOrDefault(second) + 1;
        }

        foreach (MetricsBucket bucket in minuteBuckets.Values)
        {
            PersistMinuteBucket(connection, transaction, bucket.ToSnapshot());
        }

        foreach ((DateTime second, int count) in secondBuckets)
        {
            IncrementSecondBucket(connection, transaction, second, count);
        }
    }

    private void PersistLoss(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AuditLossSummary loss)
    {
        foreach (AuditLossBucket lossBucket in loss.MinuteBuckets)
        {
            DateTime start = MinuteBucket(lossBucket.Start);
            PersistMinuteBucket(
                connection,
                transaction,
                new MetricsBucketSnapshot(
                    start,
                    lossBucket.Count,
                    lossBucket.DurationSumMs,
                    lossBucket.TargetDurationSumMs,
                    lossBucket.TargetCount,
                    lossBucket.ErrorCount,
                    lossBucket.Count,
                    lossBucket.StatusCounts));
        }

        foreach (AuditLossSecondBucket secondBucket in loss.SecondBuckets)
        {
            IncrementSecondBucket(connection, transaction, SecondBucket(secondBucket.Start), secondBucket.Count);
        }
    }

    private void PersistMinuteBucket(
        SqliteConnection connection,
        SqliteTransaction transaction,
        MetricsBucketSnapshot bucket)
    {
        MetricsBucketSnapshot? existing = ReadMinuteBucket(connection, transaction, bucket.Start);
        MetricsBucketSnapshot combined = existing is null
            ? bucket
            : Combine(existing, bucket);

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
                              INSERT INTO MetricMinuteBuckets
                              (Start, Count, DurationSumMs, TargetDurationSumMs, TargetCount, ErrorCount, LostCount, StatusCountsJson)
                              VALUES ($start, $count, $durationSumMs, $targetDurationSumMs, $targetCount, $errorCount, $lostCount, $statusCountsJson)
                              ON CONFLICT(Start) DO UPDATE SET
                                  Count = excluded.Count,
                                  DurationSumMs = excluded.DurationSumMs,
                                  TargetDurationSumMs = excluded.TargetDurationSumMs,
                                  TargetCount = excluded.TargetCount,
                                  ErrorCount = excluded.ErrorCount,
                                  LostCount = excluded.LostCount,
                                  StatusCountsJson = excluded.StatusCountsJson;
                              """;
        command.Parameters.AddWithValue("$start", FormatBucketStart(combined.Start));
        command.Parameters.AddWithValue("$count", combined.Count);
        command.Parameters.AddWithValue("$durationSumMs", combined.DurationSumMs);
        command.Parameters.AddWithValue("$targetDurationSumMs", combined.TargetDurationSumMs);
        command.Parameters.AddWithValue("$targetCount", combined.TargetCount);
        command.Parameters.AddWithValue("$errorCount", combined.ErrorCount);
        command.Parameters.AddWithValue("$lostCount", combined.LostCount);
        command.Parameters.AddWithValue("$statusCountsJson", JsonSerializer.Serialize(combined.StatusCounts));
        command.ExecuteNonQuery();
    }

    private static MetricsBucketSnapshot? ReadMinuteBucket(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTime start)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
                              SELECT Count, DurationSumMs, TargetDurationSumMs, TargetCount, ErrorCount, LostCount, StatusCountsJson
                              FROM MetricMinuteBuckets
                              WHERE Start = $start
                              LIMIT 1;
                              """;
        command.Parameters.AddWithValue("$start", FormatBucketStart(start));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? new MetricsBucketSnapshot(
                start,
                reader.GetInt32(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                DeserializeStatusCounts(reader.GetString(6)))
            : null;
    }

    private static MetricsBucketSnapshot Combine(MetricsBucketSnapshot first, MetricsBucketSnapshot second)
    {
        Dictionary<int, int> statusCounts = new(first.StatusCounts);
        foreach ((int statusCode, int count) in second.StatusCounts)
        {
            statusCounts[statusCode] = statusCounts.GetValueOrDefault(statusCode) + count;
        }

        return new MetricsBucketSnapshot(
            first.Start,
            first.Count + second.Count,
            first.DurationSumMs + second.DurationSumMs,
            first.TargetDurationSumMs + second.TargetDurationSumMs,
            first.TargetCount + second.TargetCount,
            first.ErrorCount + second.ErrorCount,
            first.LostCount + second.LostCount,
            statusCounts);
    }

    private static void IncrementSecondBucket(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTime start,
        int count)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
                              INSERT INTO MetricSecondBuckets (Start, Count)
                              VALUES ($start, $count)
                              ON CONFLICT(Start) DO UPDATE SET Count = Count + excluded.Count;
                              """;
        command.Parameters.AddWithValue("$start", FormatBucketStart(start));
        command.Parameters.AddWithValue("$count", count);
        command.ExecuteNonQuery();
    }

    private void TrimPersisted(SqliteConnection connection, SqliteTransaction transaction)
    {
        DateTime minuteCutoff = MinuteBucket(DateTimeOffset.UtcNow - MetricsRetentionWindow());
        DateTime secondCutoff = SecondBucket(DateTimeOffset.UtcNow).AddSeconds(-RecentWindowSeconds + 1);

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM MetricMinuteBuckets WHERE Start < $cutoff;";
            command.Parameters.AddWithValue("$cutoff", FormatBucketStart(minuteCutoff));
            command.ExecuteNonQuery();
        }

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM MetricSecondBuckets WHERE Start < $cutoff;";
            command.Parameters.AddWithValue("$cutoff", FormatBucketStart(secondCutoff));
            command.ExecuteNonQuery();
        }
    }

    private static DateTime ParseBucketStart(string value) =>
        DateTime.SpecifyKind(DateTime.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind), DateTimeKind.Utc);

    private static string FormatBucketStart(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("O");

    private static Dictionary<int, int> DeserializeStatusCounts(string json) =>
        JsonSerializer.Deserialize<Dictionary<int, int>>(json) ?? [];

    private static DateTimeOffset? Max(DateTimeOffset? first, DateTimeOffset second) =>
        first is null || second > first.Value ? second : first;

    public static DateTime MinuteBucket(DateTimeOffset timestamp)
    {
        DateTime utc = timestamp.UtcDateTime;
        return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc);
    }

    public static DateTime SecondBucket(DateTimeOffset timestamp)
    {
        DateTime utc = timestamp.UtcDateTime;
        return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, utc.Second, DateTimeKind.Utc);
    }

    private sealed class MetricsBucket(DateTime start)
    {
        private readonly Dictionary<int, int> _statusCounts = [];

        public DateTime Start { get; } = start;
        public int Count { get; private set; }
        public long DurationSumMs { get; private set; }
        public long TargetDurationSumMs { get; private set; }
        public int TargetCount { get; private set; }
        public int ErrorCount { get; private set; }
        public int LostCount { get; private set; }

        public void Add(AuditEntry entry)
        {
            Count++;
            DurationSumMs += entry.DurationMs;

            if (entry.TargetDurationMs.HasValue)
            {
                TargetDurationSumMs += entry.TargetDurationMs.Value;
                TargetCount++;
            }

            if (entry.StatusCode >= 400)
            {
                ErrorCount++;
            }

            if (entry.StatusCode.HasValue)
            {
                int statusCode = entry.StatusCode.Value;
                _statusCounts[statusCode] = _statusCounts.GetValueOrDefault(statusCode) + 1;
            }
        }

        public void AddLoss(AuditLossBucket loss)
        {
            Count += loss.Count;
            LostCount += loss.Count;
            DurationSumMs += loss.DurationSumMs;
            TargetDurationSumMs += loss.TargetDurationSumMs;
            TargetCount += loss.TargetCount;
            ErrorCount += loss.ErrorCount;

            foreach ((int statusCode, int count) in loss.StatusCounts)
            {
                _statusCounts[statusCode] = _statusCounts.GetValueOrDefault(statusCode) + count;
            }
        }

        public MetricsBucketSnapshot ToSnapshot() =>
            new(Start, Count, DurationSumMs, TargetDurationSumMs, TargetCount, ErrorCount, LostCount, new Dictionary<int, int>(_statusCounts));

        public static MetricsBucket FromSnapshot(MetricsBucketSnapshot snapshot)
        {
            MetricsBucket bucket = new(snapshot.Start)
            {
                Count = snapshot.Count,
                DurationSumMs = snapshot.DurationSumMs,
                TargetDurationSumMs = snapshot.TargetDurationSumMs,
                TargetCount = snapshot.TargetCount,
                ErrorCount = snapshot.ErrorCount,
                LostCount = snapshot.LostCount
            };

            foreach ((int statusCode, int count) in snapshot.StatusCounts)
            {
                bucket._statusCounts[statusCode] = count;
            }

            return bucket;
        }
    }
}

public sealed record MetricsSnapshot(
    DateTimeOffset? LatestTimestamp,
    IReadOnlyList<MetricsBucketSnapshot> MinuteBuckets,
    IReadOnlyDictionary<DateTime, int> SecondBuckets);

public sealed record MetricsBucketSnapshot(
    DateTime Start,
    int Count,
    long DurationSumMs,
    long TargetDurationSumMs,
    int TargetCount,
    int ErrorCount,
    int LostCount,
    IReadOnlyDictionary<int, int> StatusCounts);
