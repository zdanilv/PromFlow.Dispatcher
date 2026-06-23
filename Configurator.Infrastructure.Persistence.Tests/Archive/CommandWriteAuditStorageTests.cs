using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Signals;
using Configurator.Infrastructure.Persistence.Archive;
using Configurator.Infrastructure.Persistence.Common;
using Configurator.Infrastructure.Persistence.Sqlite;
using Configurator.Infrastructure.Persistence.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Xunit;

namespace Configurator.Infrastructure.Persistence.Tests.Archive;

public sealed class CommandWriteAuditStorageTests
{
    [Fact]
    public async Task SqliteArchiveWriter_CommandLifecycleUpsertAndPhysicalWrite_PersistRows()
    {
        using var database = new TempArchiveDatabase();
        var options = CreateOptions(database.DirectoryPath);
        await using var writer = CreateWriter(options);
        var commandId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var requestedAtUtc = new DateTimeOffset(2026, 6, 22, 10, 0, 0, TimeSpan.Zero);
        var completedAtUtc = requestedAtUtc.AddMilliseconds(25);
        var requested = CreateCommand(
            commandId,
            correlationId,
            requestedAtUtc,
            completedAtUtc: null,
            EquipmentCommandAuditResult.Requested,
            CommandConfirmationStatus.Pending);
        var terminal = CreateCommand(
            commandId,
            correlationId,
            requestedAtUtc,
            completedAtUtc,
            EquipmentCommandAuditResult.Succeeded,
            CommandConfirmationStatus.Confirmed);
        var write = new PhysicalModbusWriteAuditRecord(
            Guid.NewGuid(),
            commandId,
            requestedAtUtc.AddMilliseconds(2),
            completedAtUtc: requestedAtUtc.AddMilliseconds(4),
            ModbusRuntimeRole.Client,
            ModbusDataArea.HoldingRegister,
            address: 401,
            quantity: 1,
            payloadBlob: [0x12, 0x34],
            succeeded: true,
            errorCode: null,
            errorMessage: null,
            schemaVersion: 1);

        var result = await writer.WriteBatchAsync(
            [
                CreateEnvelope(ArchiveRecordKind.EquipmentCommandAudit, requested),
                CreateEnvelope(ArchiveRecordKind.EquipmentCommandAudit, terminal),
                CreateEnvelope(ArchiveRecordKind.PhysicalModbusWriteAudit, write)
            ],
            CancellationToken.None);

        Assert.True(result.Succeeded, FormatFailure(result.ErrorCode, result.ErrorMessage, result.ErrorDetails));
        var partitionPath = Assert.Single(result.Value!.PartitionPaths);
        using var connection = OpenConnection(partitionPath);
        await using var commandSelect = connection.CreateCommand();
        commandSelect.CommandText = """
            SELECT command_id, correlation_id, requested_at_utc_ms, completed_at_utc_ms,
                   device_id, signal_id, value_type, requested_value_canonical,
                   write_mode, result, confirmation_status, confirmed_at_utc_ms
            FROM equipment_command
            WHERE command_id = @commandId;
            """;
        commandSelect.Parameters.AddWithValue("@commandId", commandId.ToString("D"));
        await using var commandReader = await commandSelect.ExecuteReaderAsync();

        Assert.True(await commandReader.ReadAsync());
        Assert.Equal(commandId.ToString("D"), commandReader.GetString(0));
        Assert.Equal(correlationId.ToString("D"), commandReader.GetString(1));
        Assert.Equal(requestedAtUtc.ToUnixTimeMilliseconds(), commandReader.GetInt64(2));
        Assert.Equal(completedAtUtc.ToUnixTimeMilliseconds(), commandReader.GetInt64(3));
        Assert.Equal("device-1", commandReader.GetString(4));
        Assert.Equal("system.emergency", commandReader.GetString(5));
        Assert.Equal((int)SignalValueType.Bool, commandReader.GetInt32(6));
        Assert.Equal("true", commandReader.GetString(7));
        Assert.Equal((int)ModbusWriteMode.Latched, commandReader.GetInt32(8));
        Assert.Equal((int)EquipmentCommandAuditResult.Succeeded, commandReader.GetInt32(9));
        Assert.Equal((int)CommandConfirmationStatus.Confirmed, commandReader.GetInt32(10));
        Assert.Equal(completedAtUtc.ToUnixTimeMilliseconds(), commandReader.GetInt64(11));
        Assert.False(await commandReader.ReadAsync());

        await using var writeSelect = connection.CreateCommand();
        writeSelect.CommandText = """
            SELECT command_id, runtime_role, area, address, quantity, payload_blob, succeeded
            FROM modbus_write
            WHERE write_id = @writeId;
            """;
        writeSelect.Parameters.AddWithValue("@writeId", write.WriteId.ToString("D"));
        await using var writeReader = await writeSelect.ExecuteReaderAsync();

        Assert.True(await writeReader.ReadAsync());
        Assert.Equal(commandId.ToString("D"), writeReader.GetString(0));
        Assert.Equal((int)ModbusRuntimeRole.Client, writeReader.GetInt32(1));
        Assert.Equal((int)ModbusDataArea.HoldingRegister, writeReader.GetInt32(2));
        Assert.Equal(401, writeReader.GetInt32(3));
        Assert.Equal(1, writeReader.GetInt32(4));
        Assert.Equal(new byte[] { 0x12, 0x34 }, (byte[])writeReader["payload_blob"]);
        Assert.Equal(1, writeReader.GetInt32(6));
        Assert.False(await writeReader.ReadAsync());
    }

    [Fact]
    public async Task SqliteArchiveWriter_CommandAuditTypeMismatch_ReturnsArchiveRecordTypeMismatch()
    {
        using var database = new TempArchiveDatabase();
        var options = CreateOptions(database.DirectoryPath);
        await using var writer = CreateWriter(options);
        var envelope = CreateEnvelope(ArchiveRecordKind.EquipmentCommandAudit, new object());

        var result = await writer.WriteBatchAsync([envelope], CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ArchivePersistenceErrorCodes.ArchiveRecordTypeMismatch, result.ErrorCode);
        Assert.Empty(Directory.EnumerateFiles(database.DirectoryPath, "*.sqlite", SearchOption.TopDirectoryOnly));
    }

    private static EquipmentCommandAuditRecord CreateCommand(
        Guid commandId,
        Guid correlationId,
        DateTimeOffset requestedAtUtc,
        DateTimeOffset? completedAtUtc,
        EquipmentCommandAuditResult result,
        CommandConfirmationStatus confirmationStatus)
        => new(
            commandId,
            correlationId,
            requestedAtUtc,
            completedAtUtc,
            sessionId: null,
            userId: null,
            username: null,
            "device-1",
            "system.emergency",
            SignalValueType.Bool,
            "true",
            ModbusWriteMode.Latched,
            result,
            errorCode: null,
            errorMessage: null,
            confirmationStatus,
            confirmedAtUtc: confirmationStatus == CommandConfirmationStatus.Confirmed ? completedAtUtc : null,
            schemaVersion: 1);

    private static ArchiveEnvelope CreateEnvelope(ArchiveRecordKind kind, object record)
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

    private static string FormatFailure(string? code, string? message, string? details)
        => $"{code}: {message} {details}";

    private sealed class FixedAppDataPathProvider(string baseDirectory) : IAppDataPathProvider
    {
        public string GetArchiveBaseDirectory(ArchiveOptions options)
            => Path.GetFullPath(baseDirectory);

        public string GetArchiveExportDirectory(ArchiveOptions options)
            => Path.Combine(Path.GetFullPath(baseDirectory), "Exports");
    }
}
