using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Infrastructure.Persistence.Archive;
using Configurator.Infrastructure.Persistence.Sqlite;
using Configurator.Infrastructure.Persistence.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Configurator.Infrastructure.Persistence.Tests.Archive;

public sealed class ArchiveSnapshotStorageContractTests
{
    [Fact]
    public async Task StorageContract_EncodedSnapshotBlobs_InsertSelectDecode_RoundTripsFromSqlite()
    {
        using var database = new TempArchiveDatabase();
        var runner = new SqliteMigrationRunner(
            new SqliteConnectionFactory(),
            new SqlitePragmaInitializer(),
            new SqliteMigrationCatalog());
        var initializeResult = await runner.InitializeAsync(database.CreateOptions(), CancellationToken.None);
        Assert.True(initializeResult.Succeeded, FormatFailure(initializeResult.ErrorCode, initializeResult.ErrorMessage, initializeResult.ErrorDetails));
        var codec = new ArchiveSnapshotBlobCodec();
        var coils = new[] { true, false, true, true, false, false, true, false, true };
        var registers = new ushort[] { 0, 1, ushort.MaxValue, 0x1234 };
        var coilsBlob = codec.EncodeCoils(coils);
        var registersBlob = codec.EncodeHoldingRegisters(registers);

        using var connection = database.OpenConnection();
        await InsertSnapshotAsync(connection, coils.Length, registers.Length, coilsBlob, registersBlob);
        await using var select = connection.CreateCommand();
        select.CommandText = """
            SELECT coil_count, holding_register_count, coils_blob, holding_registers_blob
            FROM modbus_snapshot
            WHERE id = @id;
            """;
        select.Parameters.AddWithValue("@id", "snapshot-1");
        await using var reader = await select.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        var coilCount = reader.GetInt32(0);
        var holdingRegisterCount = reader.GetInt32(1);
        var storedCoilsBlob = (byte[])reader["coils_blob"];
        var storedRegistersBlob = (byte[])reader["holding_registers_blob"];
        var decodedCoils = codec.DecodeCoils(storedCoilsBlob);
        var decodedRegisters = codec.DecodeHoldingRegisters(storedRegistersBlob);

        Assert.Equal(coils.Length, coilCount);
        Assert.Equal(registers.Length, holdingRegisterCount);
        Assert.True(decodedCoils.Succeeded, FormatFailure(decodedCoils.ErrorCode, decodedCoils.ErrorMessage, decodedCoils.ErrorDetails));
        Assert.True(decodedRegisters.Succeeded, FormatFailure(decodedRegisters.ErrorCode, decodedRegisters.ErrorMessage, decodedRegisters.ErrorDetails));
        Assert.Equal(coils, decodedCoils.Value);
        Assert.Equal(registers, decodedRegisters.Value);
    }

    private static async Task InsertSnapshotAsync(
        SqliteConnection connection,
        int coilCount,
        int holdingRegisterCount,
        byte[] coilsBlob,
        byte[] registersBlob)
    {
        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO modbus_snapshot
            (id, device_id, runtime_role, captured_at_utc_ms, sequence_number, resolution_class,
             coil_start_address, holding_register_start_address, coil_count, holding_register_count,
             coils_blob, holding_registers_blob, configuration_hash, archive_schema_version, created_at_utc_ms)
            VALUES
            (@id, @deviceId, @runtimeRole, @capturedAtUtcMs, @sequenceNumber, @resolutionClass,
             @coilStartAddress, @holdingRegisterStartAddress, @coilCount, @holdingRegisterCount,
             @coilsBlob, @holdingRegistersBlob, @configurationHash, @archiveSchemaVersion, @createdAtUtcMs);
            """;
        insert.Parameters.AddWithValue("@id", "snapshot-1");
        insert.Parameters.AddWithValue("@deviceId", "device-1");
        insert.Parameters.AddWithValue("@runtimeRole", (int)ModbusRuntimeRole.Client);
        insert.Parameters.AddWithValue("@capturedAtUtcMs", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        insert.Parameters.AddWithValue("@sequenceNumber", 1L);
        insert.Parameters.AddWithValue("@resolutionClass", (int)ArchiveResolution.HighResolution);
        insert.Parameters.AddWithValue("@coilStartAddress", 0);
        insert.Parameters.AddWithValue("@holdingRegisterStartAddress", 0);
        insert.Parameters.AddWithValue("@coilCount", coilCount);
        insert.Parameters.AddWithValue("@holdingRegisterCount", holdingRegisterCount);
        insert.Parameters.AddWithValue("@coilsBlob", coilsBlob);
        insert.Parameters.AddWithValue("@holdingRegistersBlob", registersBlob);
        insert.Parameters.AddWithValue("@configurationHash", "config-hash");
        insert.Parameters.AddWithValue("@archiveSchemaVersion", 1);
        insert.Parameters.AddWithValue("@createdAtUtcMs", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        await insert.ExecuteNonQueryAsync();
    }

    private static string FormatFailure(string? code, string? message, string? details)
        => $"{code}: {message} {details}";
}
