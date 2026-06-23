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
using Xunit;

namespace Configurator.Infrastructure.Persistence.Tests.Archive;

public sealed class ArchiveRetentionServiceTests
{
    [Fact]
    public async Task ApplyRetentionAsync_DeletesExpiredRowsDeletesOldEmptyPartitionAndAuditsCurrentPartition()
    {
        using var database = new TempArchiveDatabase();
        var options = CreateOptions(database.DirectoryPath);
        var oldTimestamp = new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);
        var activeTimestamp = new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero);
        var nowUtc = new DateTimeOffset(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);
        var oldCommandId = Guid.NewGuid();
        await WriteArchiveRowsAsync(
            options,
            [
                Envelope(ArchiveRecordKind.RawModbusSnapshot, Snapshot(oldTimestamp, sequenceNumber: 1)),
                Envelope(ArchiveRecordKind.ModbusStatus, Status(oldTimestamp)),
                Envelope(ArchiveRecordKind.EquipmentCommandAudit, Command(oldCommandId, oldTimestamp)),
                Envelope(ArchiveRecordKind.PhysicalModbusWriteAudit, PhysicalWrite(oldCommandId, oldTimestamp)),
                Envelope(ArchiveRecordKind.RawModbusSnapshot, Snapshot(activeTimestamp, sequenceNumber: 2))
            ]);
        var oldPath = Path.Combine(database.DirectoryPath, "promflow-device-1-2026-01.sqlite");
        var activePath = Path.Combine(database.DirectoryPath, "promflow-device-1-2026-06.sqlite");
        var service = CreateMaintenanceService(options);

        var outcome = await service.ApplyRetentionAsync(nowUtc, CancellationToken.None);

        Assert.True(outcome.Succeeded, FormatFailure(outcome));
        Assert.Equal(2, outcome.Value!.DeletedHighResolutionSnapshotRows);
        Assert.Equal(1, outcome.Value.DeletedRuntimeEventRows);
        Assert.Equal(1, outcome.Value.DeletedCommandRows);
        Assert.Equal(1, outcome.Value.DeletedPhysicalWriteRows);
        Assert.Equal(1, outcome.Value.DeletedDatabaseFiles);
        Assert.False(File.Exists(oldPath));
        Assert.True(File.Exists(activePath));

        var queryService = CreateQueryService(options);
        var auditEvents = await queryService.QueryRuntimeEventsAsync(new ArchiveQuery(eventType: "ArchiveRetention"));
        Assert.True(auditEvents.Succeeded, FormatFailure(auditEvents));
        Assert.Single(auditEvents.Value!.Items);
    }

    private static ArchiveMaintenanceService CreateMaintenanceService(ArchiveOptions options)
    {
        var pathProvider = new FixedAppDataPathProvider(options.BaseDirectory);
        var catalog = new ArchivePartitionCatalog(pathProvider);
        var connectionFactory = new SqliteConnectionFactory();
        var pragmaInitializer = new SqlitePragmaInitializer();
        var migrationRunner = new SqliteMigrationRunner(connectionFactory, pragmaInitializer, new SqliteMigrationCatalog());
        var queryService = new SqliteArchiveQueryService(
            catalog,
            connectionFactory,
            pragmaInitializer,
            new ArchiveSnapshotBlobCodec(),
            Options.Create(options));
        var exportWriter = new ArchiveExportPackageWriter(
            queryService,
            new ArchiveCsvWriter(),
            new ArchiveChecksum(),
            pathProvider);
        var backupWriter = new ArchiveBackupPackageWriter(
            catalog,
            connectionFactory,
            pragmaInitializer,
            new ArchiveChecksum());

        return new ArchiveMaintenanceService(
            catalog,
            connectionFactory,
            pragmaInitializer,
            new ArchiveRuntimeEventWriter(catalog, migrationRunner, connectionFactory, pragmaInitializer),
            exportWriter,
            backupWriter,
            new ArchiveOptionsValidator(),
            Options.Create(options));
    }

    private static SqliteArchiveQueryService CreateQueryService(ArchiveOptions options)
        => new(
            new ArchivePartitionCatalog(new FixedAppDataPathProvider(options.BaseDirectory)),
            new SqliteConnectionFactory(),
            new SqlitePragmaInitializer(),
            new ArchiveSnapshotBlobCodec(),
            Options.Create(options));

    private static async Task WriteArchiveRowsAsync(ArchiveOptions options, IReadOnlyList<ArchiveEnvelope> envelopes)
    {
        await using var writer = CreateWriter(options);
        var outcome = await writer.WriteBatchAsync(envelopes, CancellationToken.None);
        Assert.True(outcome.Succeeded, FormatFailure(outcome));
    }

    private static RawModbusSnapshotArchiveRecord Snapshot(DateTimeOffset capturedAtUtc, long sequenceNumber)
        => new(
            Guid.NewGuid(),
            "device-1",
            ModbusRuntimeRole.Client,
            sequenceNumber,
            capturedAtUtc,
            coilStartAddress: 10,
            holdingRegisterStartAddress: 400,
            [true, false],
            [10, 20],
            "hash-" + sequenceNumber,
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
            BusyTimeoutMs = 1000,
            HighResolutionRetentionHours = 1,
            LongTermRetentionDays = 1,
            CommandAuditRetentionDays = 1,
            SecurityAuditRetentionDays = 1
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
