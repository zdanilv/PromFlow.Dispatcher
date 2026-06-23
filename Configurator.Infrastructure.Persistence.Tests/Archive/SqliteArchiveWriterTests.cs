using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Infrastructure.Persistence.Archive;
using Configurator.Infrastructure.Persistence.Common;
using Configurator.Infrastructure.Persistence.Sqlite;
using Configurator.Infrastructure.Persistence.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Xunit;

namespace Configurator.Infrastructure.Persistence.Tests.Archive;

public sealed class SqliteArchiveWriterTests
{
    [Fact]
    public async Task SqliteArchiveWriter_RawSnapshot_WritesEncodedBlobsAndScalarColumns()
    {
        using var database = new TempArchiveDatabase();
        var options = CreateOptions(database.DirectoryPath);
        await using var writer = CreateWriter(options);
        var snapshot = CreateSnapshot(
            capturedAtUtc: new DateTimeOffset(2026, 6, 22, 10, 0, 0, TimeSpan.Zero),
            sequenceNumber: 42,
            coils: [true, false, true],
            registers: [1, 2, ushort.MaxValue]);
        var envelope = new ArchiveEnvelope(
            Guid.NewGuid(),
            ArchiveRecordKind.RawModbusSnapshot,
            ArchivePriority.Normal,
            snapshot,
            DateTimeOffset.UtcNow);

        var result = await writer.WriteBatchAsync([envelope], CancellationToken.None);

        Assert.True(result.Succeeded, FormatFailure(result.ErrorCode, result.ErrorMessage, result.ErrorDetails));
        Assert.NotNull(result.Value);
        var partitionPath = Assert.Single(result.Value!.PartitionPaths);
        using var connection = OpenConnection(partitionPath);
        await using var select = connection.CreateCommand();
        select.CommandText = """
            SELECT id, device_id, runtime_role, captured_at_utc_ms, sequence_number, resolution_class,
                   coil_start_address, holding_register_start_address, coil_count, holding_register_count,
                   coils_blob, holding_registers_blob, configuration_hash, archive_schema_version
            FROM modbus_snapshot
            WHERE id = @id;
            """;
        select.Parameters.AddWithValue("@id", snapshot.Id.ToString("D"));
        await using var reader = await select.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        var codec = new ArchiveSnapshotBlobCodec();
        var decodedCoils = codec.DecodeCoils((byte[])reader["coils_blob"]);
        var decodedRegisters = codec.DecodeHoldingRegisters((byte[])reader["holding_registers_blob"]);

        Assert.Equal(snapshot.Id.ToString("D"), reader.GetString(0));
        Assert.Equal("device-1", reader.GetString(1));
        Assert.Equal((int)ModbusRuntimeRole.Client, reader.GetInt32(2));
        Assert.Equal(snapshot.CapturedAtUtc.ToUnixTimeMilliseconds(), reader.GetInt64(3));
        Assert.Equal(42L, reader.GetInt64(4));
        Assert.Equal((int)ArchiveResolution.HighResolution, reader.GetInt32(5));
        Assert.Equal(10, reader.GetInt32(6));
        Assert.Equal(20, reader.GetInt32(7));
        Assert.Equal(3, reader.GetInt32(8));
        Assert.Equal(3, reader.GetInt32(9));
        Assert.True(decodedCoils.Succeeded, FormatFailure(decodedCoils.ErrorCode, decodedCoils.ErrorMessage, decodedCoils.ErrorDetails));
        Assert.True(decodedRegisters.Succeeded, FormatFailure(decodedRegisters.ErrorCode, decodedRegisters.ErrorMessage, decodedRegisters.ErrorDetails));
        Assert.Equal(snapshot.Coils, decodedCoils.Value);
        Assert.Equal(snapshot.HoldingRegisters, decodedRegisters.Value);
        Assert.Equal("config-hash", reader.GetString(12));
        Assert.Equal(1, reader.GetInt32(13));
    }

    [Fact]
    public async Task SqliteArchiveWriter_UnsupportedKind_ReturnsArchiveRecordKindUnsupported()
    {
        using var database = new TempArchiveDatabase();
        var options = CreateOptions(database.DirectoryPath);
        await using var writer = CreateWriter(options);
        var envelope = new ArchiveEnvelope(
            Guid.NewGuid(),
            ArchiveRecordKind.SecurityAudit,
            ArchivePriority.Critical,
            new object(),
            DateTimeOffset.UtcNow);

        var result = await writer.WriteBatchAsync([envelope], CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ArchivePersistenceErrorCodes.ArchiveRecordKindUnsupported, result.ErrorCode);
        Assert.Empty(Directory.EnumerateFiles(database.DirectoryPath, "*.sqlite", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task SqliteArchiveWriter_ModbusStatus_WritesRuntimeEvent()
    {
        using var database = new TempArchiveDatabase();
        var options = CreateOptions(database.DirectoryPath);
        await using var writer = CreateWriter(options);
        var status = new ModbusStatusArchiveRecord(
            Guid.NewGuid(),
            "device-1",
            new DateTimeOffset(2026, 6, 22, 11, 30, 0, TimeSpan.Zero),
            ModbusConnectionState.Reconnecting,
            ModbusConnectionState.Faulted,
            "client reconnecting",
            "server faulted",
            "socket closed",
            schemaVersion: 1);
        var envelope = new ArchiveEnvelope(
            Guid.NewGuid(),
            ArchiveRecordKind.ModbusStatus,
            ArchivePriority.Normal,
            status,
            DateTimeOffset.UtcNow);

        var result = await writer.WriteBatchAsync([envelope], CancellationToken.None);

        Assert.True(result.Succeeded, FormatFailure(result.ErrorCode, result.ErrorMessage, result.ErrorDetails));
        Assert.NotNull(result.Value);
        var partitionPath = Assert.Single(result.Value!.PartitionPaths);
        using var connection = OpenConnection(partitionPath);
        await using var select = connection.CreateCommand();
        select.CommandText = """
            SELECT id, occurred_at_utc_ms, device_id, event_type, severity, message, details_json
            FROM runtime_event
            WHERE id = @id;
            """;
        select.Parameters.AddWithValue("@id", status.Id.ToString("D"));
        await using var reader = await select.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(status.Id.ToString("D"), reader.GetString(0));
        Assert.Equal(status.OccurredAtUtc.ToUnixTimeMilliseconds(), reader.GetInt64(1));
        Assert.Equal("device-1", reader.GetString(2));
        Assert.Equal("ModbusStatus", reader.GetString(3));
        Assert.Equal(3, reader.GetInt32(4));
        Assert.Equal("Reconnecting/Faulted: client reconnecting server faulted", reader.GetString(5));

        using var details = JsonDocument.Parse(reader.GetString(6));
        Assert.Equal("Reconnecting", details.RootElement.GetProperty("clientState").GetString());
        Assert.Equal("Faulted", details.RootElement.GetProperty("serverState").GetString());
        Assert.Equal("socket closed", details.RootElement.GetProperty("lastError").GetString());
    }

    [Fact]
    public async Task SqliteArchiveWriter_ModbusStatusTypeMismatch_ReturnsArchiveRecordTypeMismatch()
    {
        using var database = new TempArchiveDatabase();
        var options = CreateOptions(database.DirectoryPath);
        await using var writer = CreateWriter(options);
        var envelope = new ArchiveEnvelope(
            Guid.NewGuid(),
            ArchiveRecordKind.ModbusStatus,
            ArchivePriority.Normal,
            new object(),
            DateTimeOffset.UtcNow);

        var result = await writer.WriteBatchAsync([envelope], CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ArchivePersistenceErrorCodes.ArchiveRecordTypeMismatch, result.ErrorCode);
        Assert.Empty(Directory.EnumerateFiles(database.DirectoryPath, "*.sqlite", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task SqliteArchiveWriter_RecordTypeMismatch_ReturnsArchiveRecordTypeMismatch()
    {
        using var database = new TempArchiveDatabase();
        var options = CreateOptions(database.DirectoryPath);
        await using var writer = CreateWriter(options);
        var envelope = new ArchiveEnvelope(
            Guid.NewGuid(),
            ArchiveRecordKind.RawModbusSnapshot,
            ArchivePriority.Critical,
            new object(),
            DateTimeOffset.UtcNow);

        var result = await writer.WriteBatchAsync([envelope], CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ArchivePersistenceErrorCodes.ArchiveRecordTypeMismatch, result.ErrorCode);
        Assert.Empty(Directory.EnumerateFiles(database.DirectoryPath, "*.sqlite", SearchOption.TopDirectoryOnly));
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

    private static RawModbusSnapshotArchiveRecord CreateSnapshot(
        DateTimeOffset capturedAtUtc,
        long sequenceNumber,
        IReadOnlyList<bool> coils,
        IReadOnlyList<ushort> registers)
        => new(
            Guid.NewGuid(),
            "device-1",
            ModbusRuntimeRole.Client,
            sequenceNumber,
            capturedAtUtc,
            coilStartAddress: 10,
            holdingRegisterStartAddress: 20,
            coils,
            registers,
            "config-hash",
            ArchiveResolution.HighResolution,
            schemaVersion: 1);

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
