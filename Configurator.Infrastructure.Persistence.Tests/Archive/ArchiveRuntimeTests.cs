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

public sealed class ArchiveRuntimeTests
{
    [Fact]
    public async Task ArchiveRuntime_BatchSizeFlush_WritesRows()
    {
        using var database = new TempArchiveDatabase();
        await using var harness = CreateHarness(database.DirectoryPath, batchSize: 2, flushIntervalMs: 5000);
        var timestamp = new DateTimeOffset(2026, 6, 22, 10, 0, 0, TimeSpan.Zero);
        var start = await harness.Runtime.StartAsync();
        Assert.True(start.Succeeded, FormatFailure(start));

        Assert.True((await harness.Ingestor.EnqueueAsync(CreateEnvelope(timestamp, 1), CancellationToken.None)).Succeeded);
        Assert.True((await harness.Ingestor.EnqueueAsync(CreateEnvelope(timestamp, 2), CancellationToken.None)).Succeeded);

        await WaitForSnapshotCountAsync(harness.GetPartitionPath(timestamp), expectedCount: 2);
    }

    [Fact]
    public async Task ArchiveRuntime_TimerFlush_WritesPartialBatch()
    {
        using var database = new TempArchiveDatabase();
        await using var harness = CreateHarness(database.DirectoryPath, batchSize: 10, flushIntervalMs: 25);
        var timestamp = new DateTimeOffset(2026, 6, 22, 10, 0, 0, TimeSpan.Zero);
        var start = await harness.Runtime.StartAsync();
        Assert.True(start.Succeeded, FormatFailure(start));

        Assert.True((await harness.Ingestor.EnqueueAsync(CreateEnvelope(timestamp, 1), CancellationToken.None)).Succeeded);

        await WaitForSnapshotCountAsync(harness.GetPartitionPath(timestamp), expectedCount: 1);
    }

    [Fact]
    public async Task ArchiveRuntime_FlushAsync_WritesPendingRows()
    {
        using var database = new TempArchiveDatabase();
        await using var harness = CreateHarness(database.DirectoryPath, batchSize: 10, flushIntervalMs: 5000);
        var timestamp = new DateTimeOffset(2026, 6, 22, 10, 0, 0, TimeSpan.Zero);
        var start = await harness.Runtime.StartAsync();
        Assert.True(start.Succeeded, FormatFailure(start));
        Assert.True((await harness.Ingestor.EnqueueAsync(CreateEnvelope(timestamp, 1), CancellationToken.None)).Succeeded);

        var flush = await harness.Runtime.FlushAsync(CancellationToken.None);

        Assert.True(flush.Succeeded, FormatFailure(flush));
        Assert.Equal(1, ReadSnapshotCount(harness.GetPartitionPath(timestamp)));
    }

    [Fact]
    public async Task ArchiveRuntime_StopAsync_WritesPendingRows()
    {
        using var database = new TempArchiveDatabase();
        await using var harness = CreateHarness(database.DirectoryPath, batchSize: 10, flushIntervalMs: 5000);
        var timestamp = new DateTimeOffset(2026, 6, 22, 10, 0, 0, TimeSpan.Zero);
        var start = await harness.Runtime.StartAsync();
        Assert.True(start.Succeeded, FormatFailure(start));
        Assert.True((await harness.Ingestor.EnqueueAsync(CreateEnvelope(timestamp, 1), CancellationToken.None)).Succeeded);

        var stop = await harness.Runtime.StopAsync(CancellationToken.None);

        Assert.True(stop.Succeeded, FormatFailure(stop));
        Assert.Equal(1, ReadSnapshotCount(harness.GetPartitionPath(timestamp)));
        Assert.Equal(ArchiveHealthState.Stopped, harness.Health.Current.State);
    }

    [Fact]
    public async Task ArchiveRuntime_PartitionChange_WritesSeparateMonthlyDatabases()
    {
        using var database = new TempArchiveDatabase();
        await using var harness = CreateHarness(database.DirectoryPath, batchSize: 2, flushIntervalMs: 5000);
        var january = new DateTimeOffset(2026, 1, 31, 23, 59, 0, TimeSpan.Zero);
        var february = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var start = await harness.Runtime.StartAsync();
        Assert.True(start.Succeeded, FormatFailure(start));

        Assert.True((await harness.Ingestor.EnqueueAsync(CreateEnvelope(january, 1), CancellationToken.None)).Succeeded);
        Assert.True((await harness.Ingestor.EnqueueAsync(CreateEnvelope(february, 2), CancellationToken.None)).Succeeded);

        await WaitForSnapshotCountAsync(harness.GetPartitionPath(january), expectedCount: 1);
        await WaitForSnapshotCountAsync(harness.GetPartitionPath(february), expectedCount: 1);
    }

    [Fact]
    public async Task ArchiveRuntime_StartStop_AreIdempotent()
    {
        using var database = new TempArchiveDatabase();
        await using var harness = CreateHarness(database.DirectoryPath, batchSize: 2, flushIntervalMs: 100);

        Assert.True((await harness.Runtime.StartAsync()).Succeeded);
        Assert.True((await harness.Runtime.StartAsync()).Succeeded);
        Assert.True((await harness.Runtime.StopAsync()).Succeeded);
        Assert.True((await harness.Runtime.StopAsync()).Succeeded);
    }

    [Fact]
    public async Task ArchiveRuntime_Dispose_IsTerminal()
    {
        using var database = new TempArchiveDatabase();
        var harness = CreateHarness(database.DirectoryPath, batchSize: 2, flushIntervalMs: 100);
        Assert.True((await harness.Runtime.StartAsync()).Succeeded);

        await harness.DisposeAsync();
        var restart = await harness.Runtime.StartAsync();

        Assert.False(restart.Succeeded);
        Assert.Equal(ArchivePersistenceErrorCodes.ArchiveRuntimeDisposed, restart.ErrorCode);
    }

    [Fact]
    public async Task ArchiveRuntime_InvalidOptions_ReturnsArchiveOptionsInvalidAndFaultedHealth()
    {
        using var database = new TempArchiveDatabase();
        await using var harness = CreateHarness(
            database.DirectoryPath,
            batchSize: 2,
            flushIntervalMs: 100,
            deviceId: string.Empty);

        var start = await harness.Runtime.StartAsync();

        Assert.False(start.Succeeded);
        Assert.Equal(ArchivePersistenceErrorCodes.ArchiveOptionsInvalid, start.ErrorCode);
        Assert.Equal(ArchiveHealthState.Faulted, harness.Health.Current.State);
    }

    [Fact]
    public async Task ArchiveRuntime_TransientWriteFailure_PublishesDegradedAndPersistsAfterRecovery()
    {
        using var database = new TempArchiveDatabase();
        var blockedBaseDirectory = Path.Combine(database.DirectoryPath, "blocked-archive");
        File.WriteAllText(blockedBaseDirectory, "temporary blocker");
        await using var harness = CreateHarness(
            blockedBaseDirectory,
            batchSize: 1,
            flushIntervalMs: 1000,
            busyTimeoutMs: 50,
            backoffBaseMs: 10);
        var timestamp = new DateTimeOffset(2026, 6, 22, 10, 0, 0, TimeSpan.Zero);
        var partitionPath = harness.GetPartitionPath(timestamp);
        Assert.True((await harness.Runtime.StartAsync()).Succeeded);

        Assert.True((await harness.Ingestor.EnqueueAsync(CreateEnvelope(timestamp, 1), CancellationToken.None)).Succeeded);
        await WaitUntilAsync(
            () => harness.Health.Current.State == ArchiveHealthState.Degraded,
            "archive health to become degraded");

        File.Delete(blockedBaseDirectory);
        await WaitForSnapshotCountAsync(partitionPath, expectedCount: 1);

        Assert.Equal(ArchiveHealthState.Healthy, harness.Health.Current.State);
    }

    private static RuntimeHarness CreateHarness(
        string baseDirectory,
        int batchSize,
        int flushIntervalMs,
        string deviceId = "device-1",
        int busyTimeoutMs = 1000,
        int backoffBaseMs = 5)
    {
        var options = new ArchiveOptions
        {
            Enabled = true,
            BaseDirectory = baseDirectory,
            DeviceId = deviceId,
            ChannelCapacity = 32,
            BatchSize = batchSize,
            BatchFlushIntervalMs = flushIntervalMs,
            BusyTimeoutMs = busyTimeoutMs
        };
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
            new ArchiveBackoffPolicy(
                TimeSpan.FromMilliseconds(backoffBaseMs),
                TimeSpan.FromMilliseconds(Math.Max(backoffBaseMs, 25))),
            new ArchiveOptionsValidator(),
            optionsWrapper);
        var ingestor = new ArchiveIngestor(buffer, optionsWrapper);

        return new RuntimeHarness(options, resolver, runtime, ingestor, health);
    }

    private static ArchiveEnvelope CreateEnvelope(DateTimeOffset capturedAtUtc, long sequenceNumber)
        => new(
            Guid.NewGuid(),
            ArchiveRecordKind.RawModbusSnapshot,
            ArchivePriority.Normal,
            CreateSnapshot(capturedAtUtc, sequenceNumber),
            DateTimeOffset.UtcNow);

    private static RawModbusSnapshotArchiveRecord CreateSnapshot(DateTimeOffset capturedAtUtc, long sequenceNumber)
        => new(
            Guid.NewGuid(),
            "device-1",
            ModbusRuntimeRole.Client,
            sequenceNumber,
            capturedAtUtc,
            coilStartAddress: 0,
            holdingRegisterStartAddress: 0,
            [true, false, true],
            [1, 2, 3],
            "config-hash",
            ArchiveResolution.HighResolution,
            schemaVersion: 1);

    private static async Task WaitForSnapshotCountAsync(string databasePath, int expectedCount)
    {
        await WaitUntilAsync(
            () => File.Exists(databasePath) && ReadSnapshotCount(databasePath) == expectedCount,
            $"snapshot count {expectedCount} in {databasePath}");
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, string description)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        Exception? lastException = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                if (predicate())
                {
                    return;
                }
            }
            catch (Exception ex) when (ex is IOException or SqliteException or InvalidOperationException)
            {
                lastException = ex;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"Timed out waiting for {description}. Last exception: {lastException?.Message}");
    }

    private static int ReadSnapshotCount(string databasePath)
    {
        using var connection = OpenConnection(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM modbus_snapshot;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static SqliteConnection OpenConnection(string databasePath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
        var connection = new SqliteConnection(connectionString);
        connection.Open();

        return connection;
    }

    private static string FormatFailure(ArchiveOperationResult result)
        => $"{result.ErrorCode}: {result.ErrorMessage} {result.ErrorDetails}";

    private sealed class RuntimeHarness(
        ArchiveOptions options,
        ArchivePartitionResolver resolver,
        ArchiveRuntime runtime,
        ArchiveIngestor ingestor,
        ArchiveHealthService health) : IAsyncDisposable
    {
        public ArchiveOptions Options { get; } = options;

        public ArchiveRuntime Runtime { get; } = runtime;

        public ArchiveIngestor Ingestor { get; } = ingestor;

        public ArchiveHealthService Health { get; } = health;

        public string GetPartitionPath(DateTimeOffset timestampUtc)
            => resolver.GetWritablePartition(Options, timestampUtc).DatabasePath;

        public async ValueTask DisposeAsync()
            => await Runtime.DisposeAsync();
    }

    private sealed class FixedAppDataPathProvider(string baseDirectory) : IAppDataPathProvider
    {
        public string GetArchiveBaseDirectory(ArchiveOptions options)
            => Path.GetFullPath(baseDirectory);

        public string GetArchiveExportDirectory(ArchiveOptions options)
            => Path.Combine(Path.GetFullPath(baseDirectory), "Exports");
    }
}
