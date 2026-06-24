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

public sealed class SqliteArchiveQueryServiceTests
{
    [Fact]
    public async Task QueryService_PagesSnapshotsAcrossPartitionsAndFiltersAuditRows()
    {
        using var database = new TempArchiveDatabase();
        var options = CreateOptions(database.DirectoryPath);
        var january = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var february = new DateTimeOffset(2026, 2, 15, 10, 0, 0, TimeSpan.Zero);
        var commandId = Guid.NewGuid();
        await WriteArchiveRowsAsync(
            options,
            [
                Envelope(ArchiveRecordKind.RawModbusSnapshot, Snapshot(january, sequenceNumber: 1)),
                Envelope(ArchiveRecordKind.RawModbusSnapshot, Snapshot(february, sequenceNumber: 2)),
                Envelope(ArchiveRecordKind.ModbusStatus, Status(january)),
                Envelope(ArchiveRecordKind.EquipmentCommandAudit, Command(commandId, january)),
                Envelope(ArchiveRecordKind.PhysicalModbusWriteAudit, PhysicalWrite(commandId, january)),
                Envelope(ArchiveRecordKind.SecurityAudit, SecurityAudit(january))
            ]);
        var queryService = CreateQueryService(options);

        var firstPage = await queryService.QueryRawSnapshotsAsync(
            new ArchiveQuery(
                fromUtc: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                toUtc: new DateTimeOffset(2026, 2, 28, 23, 59, 59, TimeSpan.Zero),
                pageNumber: 1,
                pageSize: 1,
                sortDirection: ArchiveSortDirection.Ascending));
        var secondPage = await queryService.QueryRawSnapshotsAsync(
            new ArchiveQuery(
                fromUtc: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                toUtc: new DateTimeOffset(2026, 2, 28, 23, 59, 59, TimeSpan.Zero),
                pageNumber: 2,
                pageSize: 1,
                sortDirection: ArchiveSortDirection.Ascending));
        var events = await queryService.QueryRuntimeEventsAsync(
            new ArchiveQuery(eventType: "ModbusStatus", sortDirection: ArchiveSortDirection.Ascending));
        var commands = await queryService.QueryEquipmentCommandsAsync(
            new ArchiveQuery(
                signalId: "system.emergency",
                userId: "user-1",
                result: nameof(EquipmentCommandAuditResult.Succeeded)));
        var writes = await queryService.QueryPhysicalWritesAsync(
            new ArchiveQuery(role: ModbusRuntimeRole.Client));
        var security = await queryService.QuerySecurityAuditAsync(
            new ArchiveQuery(eventType: "AuthenticationSucceeded"));

        Assert.True(firstPage.Succeeded, FormatFailure(firstPage));
        Assert.True(firstPage.Value!.HasMore);
        Assert.Equal(2, firstPage.Value.TotalCount);
        Assert.Equal(january, firstPage.Value.Items[0].CapturedAtUtc);
        Assert.True(secondPage.Succeeded, FormatFailure(secondPage));
        Assert.False(secondPage.Value!.HasMore);
        Assert.Equal(february, secondPage.Value.Items[0].CapturedAtUtc);
        Assert.True(events.Succeeded, FormatFailure(events));
        Assert.Equal("ModbusStatus", Assert.Single(events.Value!.Items).EventType);
        Assert.True(commands.Succeeded, FormatFailure(commands));
        var command = Assert.Single(commands.Value!.Items);
        var commandOutcome = command switch { { Result: var value } => value };
        Assert.Equal(EquipmentCommandAuditResult.Succeeded, commandOutcome);
        Assert.True(writes.Succeeded, FormatFailure(writes));
        Assert.Equal(new byte[] { 0x12, 0x34 }, Assert.Single(writes.Value!.Items).PayloadBlob);
        Assert.True(security.Succeeded, FormatFailure(security));
        Assert.Equal("operator", Assert.Single(security.Value!.Items).ActorUsername);
    }

    [Fact]
    public async Task QuerySecurityAudit_EmptyTable_ReturnsEmptyPage()
    {
        using var database = new TempArchiveDatabase();
        var options = CreateOptions(database.DirectoryPath);
        await WriteArchiveRowsAsync(
            options,
            [Envelope(ArchiveRecordKind.RawModbusSnapshot, Snapshot(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), 1))]);
        var queryService = CreateQueryService(options);

        var page = await queryService.QuerySecurityAuditAsync(new ArchiveQuery(recordKind: ArchiveRecordKind.SecurityAudit));

        Assert.True(page.Succeeded, FormatFailure(page));
        Assert.Empty(page.Value!.Items);
        Assert.Equal(0, page.Value.TotalCount);
    }

    [Fact]
    public async Task QueryService_PageSizeAboveConfiguredMaximum_ReturnsArchiveQueryInvalid()
    {
        using var database = new TempArchiveDatabase();
        var options = CreateOptions(database.DirectoryPath);
        options.QueryMaxPageSize = 1;
        var queryService = CreateQueryService(options);

        var page = await queryService.QueryRawSnapshotsAsync(new ArchiveQuery(pageSize: 2));

        Assert.False(page.Succeeded);
        Assert.Equal(ArchivePersistenceErrorCodes.ArchiveQueryInvalid, page.ErrorCode);
    }

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

    private static SecurityAuditRecord SecurityAudit(DateTimeOffset occurredAtUtc)
        => new(
            Guid.NewGuid(),
            occurredAtUtc,
            "AuthenticationSucceeded",
            SecurityAuditSeverity.Information,
            actorUserId: "user-1",
            actorUsername: "operator",
            sessionId: "session-1",
            targetUserId: null,
            SecurityAuditResult.Succeeded,
            reasonCode: null,
            detailsJson: null,
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

    private static SqliteArchiveQueryService CreateQueryService(ArchiveOptions options)
        => new(
            new ArchivePartitionCatalog(new FixedAppDataPathProvider(options.BaseDirectory)),
            new SqliteConnectionFactory(),
            new SqlitePragmaInitializer(),
            new ArchiveSnapshotBlobCodec(),
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
}
