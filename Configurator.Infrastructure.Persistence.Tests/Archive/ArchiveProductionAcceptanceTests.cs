using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Infrastructure.Persistence.Archive;
using Configurator.Infrastructure.Persistence.Common;
using Configurator.Infrastructure.Persistence.Sqlite;
using Configurator.Infrastructure.Persistence.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Xunit;

namespace Configurator.Infrastructure.Persistence.Tests.Archive;

public sealed class ArchiveProductionAcceptanceTests
{
    [Fact]
    public async Task LoadMatrix_PersistsBoundedPagesAndLeavesQueueEmpty()
    {
        using var database = new TempArchiveDatabase();
        await using var harness = CreateHarness(database.DirectoryPath, batchSize: 3, flushIntervalMs: 5000);
        var started = await harness.Runtime.StartAsync();
        Assert.True(started.Succeeded, FormatFailure(started));
        var baseline = new DateTimeOffset(2026, 6, 24, 8, 0, 0, TimeSpan.Zero);
        var sequence = 0L;

        foreach (var intervalMs in new[] { 100, 250, 500, 1000 })
        {
            for (var offset = 0; offset < 3; offset++)
            {
                sequence++;
                var capturedAtUtc = baseline.AddMilliseconds(intervalMs * offset).AddSeconds(intervalMs / 100);
                var enqueue = await harness.Ingestor.EnqueueAsync(
                    Envelope(ArchivePriority.Normal, Snapshot(capturedAtUtc, sequence)),
                    CancellationToken.None);
                Assert.True(enqueue.Succeeded, FormatFailure(enqueue));
            }
        }

        var flush = await harness.Runtime.FlushAsync(CancellationToken.None);
        var page = await CreateQueryService(harness.Options).QueryRawSnapshotMetadataAsync(
            new ArchiveQuery(
                fromUtc: baseline.AddMinutes(-1),
                toUtc: baseline.AddMinutes(1),
                pageNumber: 1,
                pageSize: 5),
            CancellationToken.None);

        Assert.True(flush.Succeeded, FormatFailure(flush));
        Assert.True(page.Succeeded, FormatFailure(page));
        Assert.Equal(12, page.Value!.TotalCount);
        Assert.Equal(5, page.Value.Items.Count);
        Assert.True(page.Value.HasMore);
        Assert.Equal(0, harness.Health.Current.QueueDepth);
        Assert.NotNull(harness.Health.Current.LastSuccessAtUtc);
        Assert.Equal(12, CountRows(harness.GetPartitionPath(baseline), "modbus_snapshot"));
    }

    [Fact]
    public async Task Burst10x_CoalescesTelemetryAndPreservesCriticalAndNormalRecords()
    {
        var buffer = new ArchivePriorityBuffer(Options.Create(new ArchiveOptions
        {
            Enabled = true,
            DeviceId = "device-1",
            ChannelCapacity = 16,
            BatchSize = 1,
            BatchFlushIntervalMs = 100,
            BusyTimeoutMs = 100
        }));
        buffer.Open();

        var critical = Enumerable.Range(0, 2)
            .Select(index => Envelope(ArchivePriority.Critical, Snapshot(DateTimeOffset.UtcNow, index + 1)))
            .ToArray();
        var normal = Enumerable.Range(0, 2)
            .Select(index => Envelope(ArchivePriority.Normal, Snapshot(DateTimeOffset.UtcNow, index + 10)))
            .ToArray();
        var telemetry = Enumerable.Range(0, 10)
            .Select(index => Envelope(ArchivePriority.Telemetry, Snapshot(DateTimeOffset.UtcNow, index + 100)))
            .ToArray();

        foreach (var envelope in critical.Concat(normal).Concat(telemetry))
        {
            var result = await buffer.EnqueueAsync(envelope, CancellationToken.None);
            Assert.True(result.Succeeded, FormatFailure(result));
        }

        var snapshot = buffer.Snapshot;
        var drained = buffer.Drain(10);

        Assert.Equal(9, snapshot.DroppedTelemetryCount);
        Assert.Equal(5, snapshot.QueueDepth);
        Assert.Equal(
            critical.Select(envelope => envelope.Id)
                .Concat(normal.Select(envelope => envelope.Id))
                .Append(telemetry[^1].Id)
                .ToArray(),
            drained.Select(envelope => envelope.Id).ToArray());
    }

    [Fact]
    public async Task FailedBatch_DoesNotCommitPartialRows()
    {
        using var database = new TempArchiveDatabase();
        var options = CreateOptions(database.DirectoryPath);
        var timestamp = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
        var duplicateId = Guid.NewGuid();
        await using var writer = CreateWriter(options);

        var result = await writer.WriteBatchAsync(
            [
                Envelope(ArchivePriority.Normal, Snapshot(timestamp, 1, duplicateId)),
                Envelope(ArchivePriority.Normal, Snapshot(timestamp.AddMilliseconds(1), 2, duplicateId))
            ],
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ArchivePersistenceErrorCodes.ArchiveWriteFailed, result.ErrorCode);
        Assert.Equal(0, CountRows(GetPartitionPath(options, timestamp), "modbus_snapshot"));
    }

    [Fact]
    public async Task CorruptedOldPartition_ReturnsTypedFailureAndLaterPartitionStillQueries()
    {
        using var database = new TempArchiveDatabase();
        var options = CreateOptions(database.DirectoryPath);
        var oldTimestamp = new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);
        var validTimestamp = new DateTimeOffset(2026, 2, 10, 12, 0, 0, TimeSpan.Zero);
        var oldSnapshot = Snapshot(oldTimestamp, 1);
        await using (var writer = CreateWriter(options))
        {
            var write = await writer.WriteBatchAsync(
                [
                    Envelope(ArchivePriority.Normal, oldSnapshot),
                    Envelope(ArchivePriority.Normal, Snapshot(validTimestamp, 2))
                ],
                CancellationToken.None);
            Assert.True(write.Succeeded, FormatFailure(write));
        }

        CorruptCoilBlob(GetPartitionPath(options, oldTimestamp), oldSnapshot.Id);
        var query = CreateQueryService(options);

        var corrupt = await query.QueryRawSnapshotsAsync(
            new ArchiveQuery(fromUtc: oldTimestamp.AddMinutes(-1), toUtc: oldTimestamp.AddMinutes(1)),
            CancellationToken.None);
        var valid = await query.QueryRawSnapshotsAsync(
            new ArchiveQuery(fromUtc: validTimestamp.AddMinutes(-1), toUtc: validTimestamp.AddMinutes(1)),
            CancellationToken.None);

        Assert.False(corrupt.Succeeded);
        Assert.Equal(ArchivePersistenceErrorCodes.ArchivePartitionCorrupt, corrupt.ErrorCode);
        Assert.True(valid.Succeeded, FormatFailure(valid));
        Assert.Single(valid.Value!.Items);
    }

    [Fact]
    public async Task LargeExportLimitFailure_RemovesTemporaryExportDirectory()
    {
        using var database = new TempArchiveDatabase();
        var options = CreateOptions(database.DirectoryPath);
        options.ExportMaxRecords = 1;
        options.QueryMaxPageSize = 10;
        var timestamp = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        await using (var writer = CreateWriter(options))
        {
            var write = await writer.WriteBatchAsync(
                [
                    Envelope(ArchivePriority.Normal, Snapshot(timestamp, 1)),
                    Envelope(ArchivePriority.Normal, Snapshot(timestamp.AddMilliseconds(1), 2))
                ],
                CancellationToken.None);
            Assert.True(write.Succeeded, FormatFailure(write));
        }

        var exportDirectory = Path.Combine(database.DirectoryPath, "exports");
        var export = await CreateExportWriter(options).ExportAsync(
            new ArchiveExportRequest(
                new ArchiveQuery(
                    fromUtc: timestamp.AddMinutes(-1),
                    toUtc: timestamp.AddMinutes(1),
                    pageSize: 10),
                exportDirectory),
            options,
            CancellationToken.None);

        Assert.False(export.Succeeded);
        Assert.Equal(ArchivePersistenceErrorCodes.ArchiveExportLimitExceeded, export.ErrorCode);
        Assert.Empty(Directory.EnumerateDirectories(exportDirectory, ".promflow-export-*"));
        Assert.Empty(Directory.EnumerateFiles(exportDirectory, "*.zip"));
    }

    private static RuntimeHarness CreateHarness(
        string baseDirectory,
        int batchSize,
        int flushIntervalMs)
    {
        var options = CreateOptions(baseDirectory);
        options.ChannelCapacity = 64;
        options.BatchSize = batchSize;
        options.BatchFlushIntervalMs = flushIntervalMs;
        var optionsWrapper = Options.Create(options);
        var buffer = new ArchivePriorityBuffer(optionsWrapper);
        var health = new ArchiveHealthService();
        var resolver = new ArchivePartitionResolver(new FixedAppDataPathProvider(baseDirectory));
        var writer = new SqliteArchiveWriter(
            new ArchiveSnapshotBlobCodec(),
            resolver,
            new SqliteMigrationRunner(
                new SqliteConnectionFactory(),
                new SqlitePragmaInitializer(),
                new SqliteMigrationCatalog()),
            new SqliteConnectionFactory(),
            new SqlitePragmaInitializer(),
            optionsWrapper);
        var runtime = new ArchiveRuntime(
            buffer,
            writer,
            health,
            new ArchiveBackoffPolicy(TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(25)),
            new ArchiveOptionsValidator(),
            optionsWrapper);
        var ingestor = new ArchiveIngestor(buffer, optionsWrapper);

        return new RuntimeHarness(options, runtime, ingestor, health);
    }

    private static SqliteArchiveQueryService CreateQueryService(ArchiveOptions options)
    {
        var pathProvider = new FixedAppDataPathProvider(options.BaseDirectory);
        return new SqliteArchiveQueryService(
            new ArchivePartitionCatalog(pathProvider),
            new SqliteConnectionFactory(),
            new SqlitePragmaInitializer(),
            new ArchiveSnapshotBlobCodec(),
            Options.Create(options));
    }

    private static ArchiveExportPackageWriter CreateExportWriter(ArchiveOptions options)
    {
        var pathProvider = new FixedAppDataPathProvider(options.BaseDirectory);
        return new ArchiveExportPackageWriter(
            CreateQueryService(options),
            new ArchiveCsvWriter(),
            new ArchiveChecksum(),
            pathProvider);
    }

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

    private static RawModbusSnapshotArchiveRecord Snapshot(
        DateTimeOffset capturedAtUtc,
        long sequenceNumber,
        Guid? id = null)
        => new(
            id ?? Guid.NewGuid(),
            "device-1",
            ModbusRuntimeRole.Client,
            sequenceNumber,
            capturedAtUtc,
            coilStartAddress: 10,
            holdingRegisterStartAddress: 400,
            [true, false],
            [10, 20],
            "hash-1",
            ArchiveResolution.HighResolution,
            schemaVersion: 1);

    private static ArchiveEnvelope Envelope(ArchivePriority priority, object record)
        => new(Guid.NewGuid(), ArchiveRecordKind.RawModbusSnapshot, priority, record, DateTimeOffset.UtcNow);

    private static string GetPartitionPath(ArchiveOptions options, DateTimeOffset timestampUtc)
        => new ArchivePartitionResolver(new FixedAppDataPathProvider(options.BaseDirectory))
            .GetWritablePartition(options, timestampUtc)
            .DatabasePath;

    private static int CountRows(string databasePath, string tableName)
    {
        if (!File.Exists(databasePath))
        {
            return 0;
        }

        using var connection = OpenConnection(databasePath, readOnly: true);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void CorruptCoilBlob(string databasePath, Guid snapshotId)
    {
        using var connection = OpenConnection(databasePath, readOnly: false);
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE modbus_snapshot
            SET coils_blob = @coilsBlob
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@coilsBlob", new byte[] { 0x00 });
        command.Parameters.AddWithValue("@id", snapshotId.ToString("D"));
        Assert.Equal(1, command.ExecuteNonQuery());
    }

    private static SqliteConnection OpenConnection(string databasePath, bool readOnly)
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

    private static string FormatFailure(ArchiveOperationResult result)
        => $"{result.ErrorCode}: {result.ErrorMessage} {result.ErrorDetails}";

    private static string FormatFailure<T>(ArchiveOperationResult<T> result)
        => $"{result.ErrorCode}: {result.ErrorMessage} {result.ErrorDetails}";

    private sealed record RuntimeHarness(
        ArchiveOptions Options,
        ArchiveRuntime Runtime,
        ArchiveIngestor Ingestor,
        ArchiveHealthService Health)
        : IAsyncDisposable
    {
        public string GetPartitionPath(DateTimeOffset timestampUtc)
            => ArchiveProductionAcceptanceTests.GetPartitionPath(Options, timestampUtc);

        public async ValueTask DisposeAsync()
            => await Runtime.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class FixedAppDataPathProvider(string baseDirectory) : IAppDataPathProvider
    {
        public string GetArchiveBaseDirectory(ArchiveOptions options)
            => Path.GetFullPath(baseDirectory);

        public string GetArchiveExportDirectory(ArchiveOptions options)
            => Path.Combine(Path.GetFullPath(baseDirectory), "Exports");
    }
}
