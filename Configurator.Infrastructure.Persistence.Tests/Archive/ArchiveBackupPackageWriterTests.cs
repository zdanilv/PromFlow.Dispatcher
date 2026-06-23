using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Infrastructure.Persistence.Archive;
using Configurator.Infrastructure.Persistence.Common;
using Configurator.Infrastructure.Persistence.Sqlite;
using Configurator.Infrastructure.Persistence.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using System.IO.Compression;
using Xunit;

namespace Configurator.Infrastructure.Persistence.Tests.Archive;

public sealed class ArchiveBackupPackageWriterTests
{
    [Fact]
    public async Task CreateBackupAsync_UsesReadableSQLiteBackupWhileSourceConnectionIsOpen()
    {
        using var database = new TempArchiveDatabase();
        var options = CreateOptions(database.DirectoryPath);
        var timestamp = new DateTimeOffset(2026, 4, 10, 12, 0, 0, TimeSpan.Zero);
        await WriteArchiveRowsAsync(
            options,
            [Envelope(ArchiveRecordKind.RawModbusSnapshot, Snapshot(timestamp))]);
        var sourcePath = Path.Combine(database.DirectoryPath, "promflow-device-1-2026-04.sqlite");
        using var openSource = OpenConnection(sourcePath);
        await InsertRuntimeEventAsync(openSource, timestamp);
        var backupDirectory = Path.Combine(database.DirectoryPath, "backups");
        var backupWriter = CreateBackupWriter(options);

        var outcome = await backupWriter.CreateBackupAsync(backupDirectory, options, CancellationToken.None);

        Assert.True(outcome.Succeeded, FormatFailure(outcome));
        var partition = Assert.Single(outcome.Value!.Partitions);
        Assert.True(partition.QuickCheckPassed);
        Assert.True(File.Exists(outcome.Value.BackupPath));
        Assert.Equal(partition.Sha256, outcome.Value.Sha256ByEntryName[partition.EntryName]);

        var restoredPath = Path.Combine(database.DirectoryPath, "restored.sqlite");
        using (var archive = ZipFile.OpenRead(outcome.Value.BackupPath))
        {
            var entry = archive.GetEntry(partition.EntryName) ?? throw new InvalidOperationException("Backup entry is missing.");
            entry.ExtractToFile(restoredPath);
        }

        using var restored = OpenConnection(restoredPath, readOnly: true);
        Assert.Equal(1, CountRows(restored, "modbus_snapshot"));
        Assert.Equal(1, CountRows(restored, "runtime_event"));
    }

    private static ArchiveBackupPackageWriter CreateBackupWriter(ArchiveOptions options)
        => new(
            new ArchivePartitionCatalog(new FixedAppDataPathProvider(options.BaseDirectory)),
            new SqliteConnectionFactory(),
            new SqlitePragmaInitializer(),
            new ArchiveChecksum());

    private static async Task InsertRuntimeEventAsync(SqliteConnection connection, DateTimeOffset timestamp)
    {
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL;";
        await pragma.ExecuteNonQueryAsync();

        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO runtime_event
            (id, occurred_at_utc_ms, device_id, event_type, severity, message, details_json)
            VALUES
            (@id, @occurredAtUtcMs, @deviceId, @eventType, @severity, @message, @detailsJson);
            """;
        insert.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("D"));
        insert.Parameters.AddWithValue("@occurredAtUtcMs", timestamp.ToUnixTimeMilliseconds());
        insert.Parameters.AddWithValue("@deviceId", "device-1");
        insert.Parameters.AddWithValue("@eventType", "BackupProbe");
        insert.Parameters.AddWithValue("@severity", 1);
        insert.Parameters.AddWithValue("@message", "Backup probe");
        insert.Parameters.AddWithValue("@detailsJson", "{}");
        await insert.ExecuteNonQueryAsync();
    }

    private static int CountRows(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static async Task WriteArchiveRowsAsync(ArchiveOptions options, IReadOnlyList<ArchiveEnvelope> envelopes)
    {
        await using var writer = CreateWriter(options);
        var outcome = await writer.WriteBatchAsync(envelopes, CancellationToken.None);
        Assert.True(outcome.Succeeded, FormatFailure(outcome));
    }

    private static RawModbusSnapshotArchiveRecord Snapshot(DateTimeOffset capturedAtUtc)
        => new(
            Guid.NewGuid(),
            "device-1",
            ModbusRuntimeRole.Client,
            sequenceNumber: 1,
            capturedAtUtc,
            coilStartAddress: 10,
            holdingRegisterStartAddress: 400,
            [true, false],
            [10, 20],
            "hash-1",
            ArchiveResolution.HighResolution,
            schemaVersion: 1);

    private static ArchiveEnvelope Envelope(ArchiveRecordKind kind, object record)
        => new(Guid.NewGuid(), kind, ArchivePriority.Critical, record, DateTimeOffset.UtcNow);

    private static SqliteArchiveWriter CreateWriter(ArchiveOptions options)
        => new(
            new ArchiveSnapshotBlobCodec(),
            new ArchivePartitionResolver(new FixedAppDataPathProvider(options.BaseDirectory)),
            new SqliteMigrationRunner(
                new SqliteConnectionFactory(),
                new SqlitePragmaInitializer(),
                new SqliteMigrationCatalog()),
            new SqliteConnectionFactory(),
            new SqlitePragmaInitializer(),
            Options.Create(options));

    private static SqliteConnection OpenConnection(string databasePath, bool readOnly = false)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
        var connection = new SqliteConnection(connectionString);
        connection.Open();

        return connection;
    }

    private static ArchiveOptions CreateOptions(string baseDirectory)
        => new()
        {
            Enabled = true,
            BaseDirectory = baseDirectory,
            DeviceId = "device-1",
            ChannelCapacity = 16,
            BatchSize = 4,
            BatchFlushIntervalMs = 100,
            BusyTimeoutMs = 1000
        };

    private static string FormatFailure<T>(ArchiveOperationResult<T> outcome)
        => $"{outcome.ErrorCode}: {outcome.ErrorMessage} {outcome.ErrorDetails}";

    private sealed class FixedAppDataPathProvider(string baseDirectory) : IAppDataPathProvider
    {
        public string GetArchiveBaseDirectory(ArchiveOptions options)
            => Path.GetFullPath(baseDirectory);

        public string GetArchiveExportDirectory(ArchiveOptions options)
            => Path.Combine(Path.GetFullPath(baseDirectory), "Exports");
    }
}
