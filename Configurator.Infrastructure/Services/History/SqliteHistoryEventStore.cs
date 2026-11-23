using System.Globalization;
using System.Runtime.CompilerServices;
using Configurator.Application.Services.History;
using Microsoft.Data.Sqlite;

namespace Configurator.Infrastructure.Services.History;

public sealed record SqliteArchiveRun(long FirstEventId, long LastEventId, long EventCount,
    string ArchivePath, string EventsSha256, DateTimeOffset CreatedUtc, DateTimeOffset VerifiedUtc,
    bool Expired = false);

public sealed record SqliteArchiveIntent(string IntentId, string TemporaryPath, string ArchivePath,
    long FirstEventId, long LastEventId, long EventCount, string EventsSha256, DateTimeOffset CreatedUtc);

public sealed class HistoryStorageInUseException(string path, Exception inner)
    : IOException($"Хранилище истории занято другим процессом: {path}", inner);

/// <summary>Owns the process lease and serializes short write transactions. Readers use WAL snapshots.</summary>
public sealed class SqliteHistoryEventStore : IHistoryEventStore, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private FileStream? _lease;
    private SqliteConnection? _anchorConnection;
    private bool _disposed;
    public string DatabasePath { get; }
    public bool IsInitialized { get; private set; }

    public SqliteHistoryEventStore(string? databasePath = null)
    {
        DatabasePath = Path.GetFullPath(databasePath ?? ApplicationConfigPaths.HistoryDatabasePath);
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsInitialized) return;
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            try
            {
                _lease = new FileStream(DatabasePath + ".lock", FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex)
            {
                throw new HistoryStorageInUseException(DatabasePath, ex);
            }
            try
            {
                using var connection = OpenConnection();
                Execute(connection, "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;");
                var version = ScalarLong(connection, "PRAGMA user_version;");
                if (version > 1) throw new InvalidDataException("Версия хранилища истории новее поддерживаемой.");
                Execute(connection, """
                    CREATE TABLE IF NOT EXISTS history_events (
                      id INTEGER PRIMARY KEY AUTOINCREMENT, event_uid TEXT NOT NULL UNIQUE,
                      timestamp_utc TEXT NOT NULL, recorded_utc TEXT NOT NULL, event_type TEXT NOT NULL,
                      name TEXT NOT NULL, roles TEXT NOT NULL, objects TEXT NOT NULL, modbus_area TEXT NULL,
                      address INTEGER NULL, bit_index INTEGER NULL, value_text TEXT NOT NULL,
                      source TEXT NOT NULL, session_id TEXT NOT NULL);
                    CREATE INDEX IF NOT EXISTS ix_history_events_time ON history_events(timestamp_utc,id);
                    CREATE TABLE IF NOT EXISTS archive_runs (
                      run_id INTEGER PRIMARY KEY AUTOINCREMENT, first_event_id INTEGER NOT NULL,
                      last_event_id INTEGER NOT NULL, event_count INTEGER NOT NULL, archive_path TEXT NOT NULL,
                      events_sha256 TEXT NOT NULL, created_utc TEXT NOT NULL, verified_utc TEXT NOT NULL,
                      expired INTEGER NOT NULL DEFAULT 0, UNIQUE(first_event_id,last_event_id));
                    CREATE UNIQUE INDEX IF NOT EXISTS ix_archive_runs_path ON archive_runs(archive_path);
                    CREATE TABLE IF NOT EXISTS history_metadata (key TEXT PRIMARY KEY,value TEXT NOT NULL);
                    CREATE TABLE IF NOT EXISTS archive_intents (
                      intent_id TEXT PRIMARY KEY, temporary_path TEXT NOT NULL, archive_path TEXT NOT NULL,
                      first_event_id INTEGER NOT NULL,last_event_id INTEGER NOT NULL,event_count INTEGER NOT NULL,
                      events_sha256 TEXT NOT NULL,created_utc TEXT NOT NULL);
                    INSERT OR IGNORE INTO history_metadata(key,value) VALUES('lastArchivedId','0');
                    PRAGMA user_version=1;
                    """);
                // Keep WAL open between short operations, avoiding a checkpoint after every batch.
                _anchorConnection = OpenConnection();
                IsInitialized = true;
            }
            catch
            {
                _lease.Dispose();
                _lease = null;
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<int> AppendBatchAsync(IReadOnlyList<HistoryEvent> events, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var connection = OpenInitializedConnection();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            // A duplicate is filtered before INSERT so AUTOINCREMENT does not consume an ID.
            command.CommandText = """
                INSERT INTO history_events(event_uid,timestamp_utc,recorded_utc,event_type,name,roles,objects,
                  modbus_area,address,bit_index,value_text,source,session_id)
                SELECT $uid,$timestamp,$recorded,$type,$name,$roles,$objects,$area,$address,$bit,$value,$source,$session
                WHERE NOT EXISTS(SELECT 1 FROM history_events WHERE event_uid=$uid);
                """;
            string[] names = ["uid","timestamp","recorded","type","name","roles","objects","area","address","bit","value","source","session"];
            foreach (var name in names) command.Parameters.Add(new SqliteParameter("$" + name, DBNull.Value));
            var count = 0;
            foreach (var item in events)
            {
                ct.ThrowIfCancellationRequested();
                object?[] values = [item.EventUid.ToString("D"), Utc(item.TimestampUtc), Utc(item.RecordedUtc),
                    item.EventType, item.Name, item.Roles, item.Objects, item.ModbusArea, item.Address,
                    item.BitIndex, item.Value, item.Source, item.SessionId.ToString("D")];
                for (var i = 0; i < names.Length; i++) command.Parameters[i].Value = values[i] ?? DBNull.Value;
                count += command.ExecuteNonQuery();
            }
            transaction.Commit();
            return count;
        }
        finally { _gate.Release(); }
    }

    public async IAsyncEnumerable<HistoryEvent> ReadRangeAsync(long afterId, long throughId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var connection = OpenInitializedConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT * FROM history_events WHERE id>$after AND id<=$through ORDER BY id;";
        command.Parameters.AddWithValue("$after", afterId);
        command.Parameters.AddWithValue("$through", throughId);
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            yield return new HistoryEvent(Guid.Parse(reader.GetString(1)), ParseUtc(reader.GetString(2)),
                ParseUtc(reader.GetString(3)), reader.GetString(4), reader.GetString(5), reader.GetString(6),
                reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetInt32(9), reader.IsDBNull(10) ? null : reader.GetInt32(10),
                reader.GetString(11), reader.GetString(12), Guid.Parse(reader.GetString(13))) { Id = reader.GetInt64(0) };
        }
    }

    public Task<long> GetMaximumIdAsync(CancellationToken ct = default) => ReadScalarAsync(
        "SELECT COALESCE(MAX(id),0) FROM history_events;", ct);

    public async Task<long> GetLastArchivedIdAsync(CancellationToken ct = default) =>
        long.Parse(await GetMetadataAsync("lastArchivedId", ct) ?? "0", CultureInfo.InvariantCulture);

    public async Task<string?> GetMetadataAsync(string key, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var connection = OpenInitializedConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM history_metadata WHERE key=$key;";
            command.Parameters.AddWithValue("$key", key);
            return command.ExecuteScalar() as string;
        }
        finally { _gate.Release(); }
    }

    public async Task SetMetadataAsync(string key, string value, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var connection = OpenInitializedConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO history_metadata(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value;";
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
            command.ExecuteNonQuery();
        }
        finally { _gate.Release(); }
    }

    public async Task RegisterArchiveAsync(SqliteArchiveRun run, long expectedCursor, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var connection = OpenInitializedConnection();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT CAST(value AS INTEGER) FROM history_metadata WHERE key='lastArchivedId';";
            var cursor = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (cursor != expectedCursor || run.FirstEventId != cursor + 1 || run.LastEventId < run.FirstEventId ||
                run.EventCount != run.LastEventId - run.FirstEventId + 1)
                throw new InvalidDataException("Архивный диапазон конфликтует с подтверждённым курсором.");
            command.CommandText = """
                INSERT INTO archive_runs(first_event_id,last_event_id,event_count,archive_path,events_sha256,created_utc,verified_utc)
                VALUES($first,$last,$count,$path,$hash,$created,$verified);
                UPDATE history_metadata SET value=CAST($last AS TEXT) WHERE key='lastArchivedId';
                DELETE FROM archive_intents WHERE archive_path=$path;
                """;
            command.Parameters.AddWithValue("$first", run.FirstEventId);
            command.Parameters.AddWithValue("$last", run.LastEventId);
            command.Parameters.AddWithValue("$count", run.EventCount);
            command.Parameters.AddWithValue("$path", run.ArchivePath);
            command.Parameters.AddWithValue("$hash", run.EventsSha256);
            command.Parameters.AddWithValue("$created", Utc(run.CreatedUtc));
            command.Parameters.AddWithValue("$verified", Utc(run.VerifiedUtc));
            command.ExecuteNonQuery();
            transaction.Commit();
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<SqliteArchiveRun>> GetArchiveRunsAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var connection = OpenInitializedConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT first_event_id,last_event_id,event_count,archive_path,events_sha256,created_utc,verified_utc,expired FROM archive_runs ORDER BY first_event_id;";
            using var reader = command.ExecuteReader();
            var result = new List<SqliteArchiveRun>();
            while (reader.Read()) result.Add(new(reader.GetInt64(0),reader.GetInt64(1),reader.GetInt64(2),reader.GetString(3),
                reader.GetString(4),ParseUtc(reader.GetString(5)),ParseUtc(reader.GetString(6)),reader.GetBoolean(7)));
            return result;
        }
        finally { _gate.Release(); }
    }

    public Task MarkArchiveExpiredAsync(string path, CancellationToken ct = default) =>
        ExecuteWithParameterAsync("UPDATE archive_runs SET expired=1 WHERE archive_path=$value;", path, ct);

    public async Task<bool> HasArchivedEventsBeforeAsync(long firstId, long lastId, DateTimeOffset cutoff,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var connection = OpenInitializedConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT EXISTS(SELECT 1 FROM history_events WHERE id BETWEEN $first AND $last AND timestamp_utc<$cutoff);";
            command.Parameters.AddWithValue("$first", firstId);
            command.Parameters.AddWithValue("$last", lastId);
            command.Parameters.AddWithValue("$cutoff", Utc(cutoff));
            return Convert.ToBoolean(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        finally { _gate.Release(); }
    }

    public async Task<int> DeleteArchivedEventsAsync(long firstId, long lastId, DateTimeOffset cutoff,
        int batchSize = 1000, CancellationToken ct = default)
    {
        if (batchSize < 1) throw new ArgumentOutOfRangeException(nameof(batchSize));
        await _gate.WaitAsync(ct);
        try
        {
            using var connection = OpenInitializedConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM history_events WHERE id IN (
                  SELECT id FROM history_events WHERE id BETWEEN $first AND $last AND timestamp_utc<$cutoff
                  AND id<=CAST((SELECT value FROM history_metadata WHERE key='lastArchivedId') AS INTEGER)
                  AND EXISTS(SELECT 1 FROM archive_runs WHERE first_event_id=$first AND last_event_id=$last AND expired=0)
                  ORDER BY id LIMIT $limit);
                """;
            command.Parameters.AddWithValue("$first", firstId);
            command.Parameters.AddWithValue("$last", lastId);
            command.Parameters.AddWithValue("$cutoff", Utc(cutoff));
            command.Parameters.AddWithValue("$limit", batchSize);
            return command.ExecuteNonQuery();
        }
        finally { _gate.Release(); }
    }

    public async Task SaveArchiveIntentAsync(SqliteArchiveIntent intent, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var connection = OpenInitializedConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO archive_intents VALUES($id,$tmp,$path,$first,$last,$count,$hash,$created)
                ON CONFLICT(intent_id) DO UPDATE SET temporary_path=excluded.temporary_path,archive_path=excluded.archive_path,
                  first_event_id=excluded.first_event_id,last_event_id=excluded.last_event_id,event_count=excluded.event_count,
                  events_sha256=excluded.events_sha256,created_utc=excluded.created_utc;
                """;
            command.Parameters.AddWithValue("$id", intent.IntentId);
            command.Parameters.AddWithValue("$tmp", intent.TemporaryPath);
            command.Parameters.AddWithValue("$path", intent.ArchivePath);
            command.Parameters.AddWithValue("$first", intent.FirstEventId);
            command.Parameters.AddWithValue("$last", intent.LastEventId);
            command.Parameters.AddWithValue("$count", intent.EventCount);
            command.Parameters.AddWithValue("$hash", intent.EventsSha256);
            command.Parameters.AddWithValue("$created", Utc(intent.CreatedUtc));
            command.ExecuteNonQuery();
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<SqliteArchiveIntent>> GetArchiveIntentsAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var connection = OpenInitializedConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM archive_intents ORDER BY first_event_id;";
            using var reader = command.ExecuteReader();
            var result = new List<SqliteArchiveIntent>();
            while (reader.Read()) result.Add(new(reader.GetString(0),reader.GetString(1),reader.GetString(2),
                reader.GetInt64(3),reader.GetInt64(4),reader.GetInt64(5),reader.GetString(6),ParseUtc(reader.GetString(7))));
            return result;
        }
        finally { _gate.Release(); }
    }

    public Task RemoveArchiveIntentAsync(string intentId, CancellationToken ct = default) =>
        ExecuteWithParameterAsync("DELETE FROM archive_intents WHERE intent_id=$value;", intentId, ct);

    public async Task CheckpointAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var connection = OpenInitializedConnection();
            Execute(connection, "PRAGMA wal_checkpoint(TRUNCATE);");
        }
        finally { _gate.Release(); }
    }

    private async Task ExecuteWithParameterAsync(string sql, string value, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var connection = OpenInitializedConnection();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$value", value);
            command.ExecuteNonQuery();
        }
        finally { _gate.Release(); }
    }

    private async Task<long> ReadScalarAsync(string sql, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var connection = OpenInitializedConnection();
            return ScalarLong(connection, sql);
        }
        finally { _gate.Release(); }
    }

    private SqliteConnection OpenInitializedConnection()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsInitialized) throw new InvalidOperationException("Хранилище истории ещё не открыто.");
        return OpenConnection();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false, DefaultTimeout = 5
        }.ToString());
        try
        {
            connection.Open();
            Execute(connection, "PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON; PRAGMA synchronous=FULL;");
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long ScalarLong(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static string Utc(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseUtc(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            IsInitialized = false;
            _disposed = true;
            _anchorConnection?.Dispose();
            _anchorConnection = null;
            _lease?.Dispose();
            _lease = null;
        }
        finally { _gate.Release(); }
    }
}
