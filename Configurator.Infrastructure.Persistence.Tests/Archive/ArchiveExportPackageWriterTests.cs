using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Signals;
using Configurator.Infrastructure.Persistence.Archive;
using Configurator.Infrastructure.Persistence.Common;
using Configurator.Infrastructure.Persistence.Sqlite;
using Configurator.Infrastructure.Persistence.Tests.TestSupport;
using Microsoft.Extensions.Options;
using System.IO.Compression;
using Xunit;

namespace Configurator.Infrastructure.Persistence.Tests.Archive;

public sealed class ArchiveExportPackageWriterTests
{
    [Fact]
    public async Task ExportAsync_CreatesZipPackageWithManifestChecksumsAndNoSnapshotBlobs()
    {
        using var database = new TempArchiveDatabase();
        var options = CreateOptions(database.DirectoryPath);
        var commandId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        await WriteArchiveRowsAsync(
            options,
            [
                Envelope(ArchiveRecordKind.RawModbusSnapshot, Snapshot(timestamp)),
                Envelope(ArchiveRecordKind.ModbusStatus, Status(timestamp)),
                Envelope(ArchiveRecordKind.EquipmentCommandAudit, Command(commandId, timestamp)),
                Envelope(ArchiveRecordKind.PhysicalModbusWriteAudit, PhysicalWrite(commandId, timestamp))
            ]);
        var exportDirectory = Path.Combine(database.DirectoryPath, "exports");
        var writer = CreateExportWriter(options);
        var progress = new RecordingProgress();
        var request = new ArchiveExportRequest(
            new ArchiveQuery(
                fromUtc: new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
                toUtc: new DateTimeOffset(2026, 3, 31, 23, 59, 59, TimeSpan.Zero),
                sortDirection: ArchiveSortDirection.Ascending),
            exportDirectory);

        var outcome = await writer.ExportAsync(request, options, CancellationToken.None, progress);

        Assert.True(outcome.Succeeded, FormatFailure(outcome));
        Assert.True(File.Exists(outcome.Value!.ExportPath));
        Assert.Equal(1, outcome.Value.CommandCount);
        Assert.Equal(1, outcome.Value.PhysicalWriteCount);
        Assert.Equal(1, outcome.Value.RuntimeEventCount);
        Assert.Equal(1, outcome.Value.SnapshotMetadataCount);
        Assert.Empty(Directory.EnumerateDirectories(exportDirectory, ".promflow-export-*"));

        using var archive = ZipFile.OpenRead(outcome.Value.ExportPath);
        var entryNames = archive.Entries.Select(entry => entry.FullName).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        Assert.Equal(
            ["checksums.sha256", "commands.csv", "events.csv", "manifest.json", "modbus_writes.csv", "snapshots.ndjson"],
            entryNames);

        var manifest = ReadEntry(archive, "manifest.json");
        var checksums = ReadEntry(archive, "checksums.sha256");
        var writes = ReadEntry(archive, "modbus_writes.csv");
        var snapshots = ReadEntry(archive, "snapshots.ndjson");

        Assert.Contains("\"modbusWrites\": 1", manifest, StringComparison.Ordinal);
        Assert.Contains("modbus_writes.csv", checksums, StringComparison.Ordinal);
        Assert.Contains("1234", writes, StringComparison.Ordinal);
        Assert.Contains("\"coilCount\":2", snapshots, StringComparison.Ordinal);
        Assert.DoesNotContain("coils_blob", snapshots, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("holding_registers_blob", snapshots, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ArchiveExportPhase.Preparing, progress.Values.Select(value => value.Phase));
        Assert.Contains(ArchiveExportPhase.Commands, progress.Values.Select(value => value.Phase));
        Assert.Contains(ArchiveExportPhase.PhysicalWrites, progress.Values.Select(value => value.Phase));
        Assert.Contains(ArchiveExportPhase.RuntimeEvents, progress.Values.Select(value => value.Phase));
        Assert.Contains(ArchiveExportPhase.Snapshots, progress.Values.Select(value => value.Phase));
        Assert.Contains(ArchiveExportPhase.Packaging, progress.Values.Select(value => value.Phase));
        Assert.Equal(ArchiveExportPhase.Completed, progress.Values.Last().Phase);
    }

    private static string ReadEntry(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName) ?? throw new InvalidOperationException($"Missing {entryName}.");
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static ArchiveExportPackageWriter CreateExportWriter(ArchiveOptions options)
    {
        var pathProvider = new FixedAppDataPathProvider(options.BaseDirectory);
        var queryService = new SqliteArchiveQueryService(
            new ArchivePartitionCatalog(pathProvider),
            new SqliteConnectionFactory(),
            new SqlitePragmaInitializer(),
            new ArchiveSnapshotBlobCodec(),
            Options.Create(options));

        return new ArchiveExportPackageWriter(
            queryService,
            new ArchiveCsvWriter(),
            new ArchiveChecksum(),
            pathProvider);
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

    private static ModbusStatusArchiveRecord Status(DateTimeOffset occurredAtUtc)
        => new(
            Guid.NewGuid(),
            "device-1",
            occurredAtUtc,
            ModbusConnectionState.Running,
            ModbusConnectionState.Stopped,
            "Connected",
            "Stopped",
            lastError: null,
            schemaVersion: 1);

    private static EquipmentCommandAuditRecord Command(Guid commandId, DateTimeOffset requestedAtUtc)
        => new(
            commandId,
            Guid.NewGuid(),
            requestedAtUtc,
            requestedAtUtc.AddMilliseconds(10),
            sessionId: "session-1",
            userId: "user-1",
            username: "operator",
            "device-1",
            "system.emergency",
            SignalValueType.Bool,
            "true",
            ModbusWriteMode.Latched,
            EquipmentCommandAuditResult.Succeeded,
            errorCode: null,
            errorMessage: null,
            CommandConfirmationStatus.Confirmed,
            confirmedAtUtc: requestedAtUtc.AddMilliseconds(15),
            schemaVersion: 1);

    private static PhysicalModbusWriteAuditRecord PhysicalWrite(Guid commandId, DateTimeOffset attemptedAtUtc)
        => new(
            Guid.NewGuid(),
            commandId,
            attemptedAtUtc.AddMilliseconds(2),
            attemptedAtUtc.AddMilliseconds(4),
            ModbusRuntimeRole.Client,
            ModbusDataArea.HoldingRegister,
            address: 401,
            quantity: 1,
            payloadBlob: [0x12, 0x34],
            succeeded: true,
            errorCode: null,
            errorMessage: null,
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

    private sealed class RecordingProgress : IProgress<ArchiveExportProgress>
    {
        public List<ArchiveExportProgress> Values { get; } = [];

        public void Report(ArchiveExportProgress value) => Values.Add(value);
    }
}
