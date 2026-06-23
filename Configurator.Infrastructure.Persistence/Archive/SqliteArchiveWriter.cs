using Configurator.Application.Services.Archiving;
using Configurator.Infrastructure.Persistence.Common;
using Configurator.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Configurator.Infrastructure.Persistence.Archive;

public sealed class SqliteArchiveWriter : IAsyncDisposable
{
    private readonly ArchiveSnapshotBlobCodec _blobCodec;
    private readonly ArchivePartitionResolver _partitionResolver;
    private readonly SqliteMigrationRunner _migrationRunner;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SqlitePragmaInitializer _pragmaInitializer;
    private readonly IOptions<ArchiveOptions> _options;
    private SqliteConnection? _connection;
    private string? _databasePath;

    public SqliteArchiveWriter(
        ArchiveSnapshotBlobCodec blobCodec,
        ArchivePartitionResolver partitionResolver,
        SqliteMigrationRunner migrationRunner,
        SqliteConnectionFactory connectionFactory,
        SqlitePragmaInitializer pragmaInitializer,
        IOptions<ArchiveOptions> options)
    {
        _blobCodec = blobCodec ?? throw new ArgumentNullException(nameof(blobCodec));
        _partitionResolver = partitionResolver ?? throw new ArgumentNullException(nameof(partitionResolver));
        _migrationRunner = migrationRunner ?? throw new ArgumentNullException(nameof(migrationRunner));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _pragmaInitializer = pragmaInitializer ?? throw new ArgumentNullException(nameof(pragmaInitializer));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<ArchiveOperationResult<ArchiveWriteBatchResult>> WriteBatchAsync(
        IReadOnlyList<ArchiveEnvelope> envelopes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelopes);

        if (envelopes.Count == 0)
        {
            return ArchiveOperationResult<ArchiveWriteBatchResult>.Success(new ArchiveWriteBatchResult(
                attemptedCount: 0,
                persistedCount: 0,
                partitionPaths: Array.Empty<string>(),
                writtenAtUtc: DateTimeOffset.UtcNow));
        }

        var options = _options.Value.Clone();
        var prepareResult = PrepareSnapshotItems(envelopes, options);
        if (!prepareResult.Succeeded || prepareResult.Value is null)
        {
            return ArchiveOperationResult<ArchiveWriteBatchResult>.Failure(
                prepareResult.ErrorCode ?? ArchivePersistenceErrorCodes.ArchiveWriteFailed,
                prepareResult.ErrorMessage ?? "Archive batch preparation failed.",
                prepareResult.ErrorDetails);
        }

        var items = prepareResult.Value;
        var partitionPaths = new List<string>();
        var persistedCount = 0;
        var index = 0;

        while (index < items.Count)
        {
            var partition = items[index].Partition;
            var path = partition.DatabasePath;
            var group = new List<SnapshotWriteItem>();

            while (index < items.Count
                && string.Equals(items[index].Partition.DatabasePath, path, StringComparison.Ordinal))
            {
                group.Add(items[index]);
                index++;
            }

            var connectionResult = await EnsureConnectionAsync(path, options, cancellationToken).ConfigureAwait(false);
            if (!connectionResult.Succeeded || connectionResult.Value is null)
            {
                return ArchiveOperationResult<ArchiveWriteBatchResult>.Failure(
                    connectionResult.ErrorCode ?? ArchivePersistenceErrorCodes.ArchiveWriteFailed,
                    connectionResult.ErrorMessage ?? "Archive database connection failed.",
                    connectionResult.ErrorDetails);
            }

            var writeResult = await InsertGroupAsync(connectionResult.Value, group, cancellationToken).ConfigureAwait(false);
            if (!writeResult.Succeeded)
            {
                return ArchiveOperationResult<ArchiveWriteBatchResult>.Failure(
                    writeResult.ErrorCode ?? ArchivePersistenceErrorCodes.ArchiveWriteFailed,
                    writeResult.ErrorMessage ?? "Archive batch write failed.",
                    writeResult.ErrorDetails);
            }

            persistedCount += group.Count;
            if (!partitionPaths.Contains(path, StringComparer.Ordinal))
            {
                partitionPaths.Add(path);
            }
        }

        return ArchiveOperationResult<ArchiveWriteBatchResult>.Success(new ArchiveWriteBatchResult(
            envelopes.Count,
            persistedCount,
            partitionPaths,
            DateTimeOffset.UtcNow));
    }

    public async ValueTask DisposeAsync()
        => await DisposeConnectionAsync().ConfigureAwait(false);

    private ArchiveOperationResult<IReadOnlyList<SnapshotWriteItem>> PrepareSnapshotItems(
        IReadOnlyList<ArchiveEnvelope> envelopes,
        ArchiveOptions options)
    {
        var items = new List<SnapshotWriteItem>(envelopes.Count);

        try
        {
            foreach (var envelope in envelopes)
            {
                if (envelope.Kind != ArchiveRecordKind.RawModbusSnapshot)
                {
                    return ArchiveOperationResult<IReadOnlyList<SnapshotWriteItem>>.Failure(
                        ArchivePersistenceErrorCodes.ArchiveRecordKindUnsupported,
                        "Archive writer supports only raw Modbus snapshot records.",
                        envelope.Kind.ToString());
                }

                if (envelope.Record is not RawModbusSnapshotArchiveRecord record)
                {
                    return ArchiveOperationResult<IReadOnlyList<SnapshotWriteItem>>.Failure(
                        ArchivePersistenceErrorCodes.ArchiveRecordTypeMismatch,
                        "Archive envelope record type does not match raw snapshot kind.",
                        envelope.Record.GetType().FullName);
                }

                var partition = _partitionResolver.GetWritablePartition(options, record.CapturedAtUtc);
                items.Add(new SnapshotWriteItem(
                    record,
                    partition,
                    _blobCodec.EncodeCoils(record.Coils),
                    _blobCodec.EncodeHoldingRegisters(record.HoldingRegisters)));
            }

            return ArchiveOperationResult<IReadOnlyList<SnapshotWriteItem>>.Success(items);
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or OverflowException)
        {
            return ArchiveOperationResult<IReadOnlyList<SnapshotWriteItem>>.Failure(
                ArchivePersistenceErrorCodes.ArchiveWriteFailed,
                "Archive batch preparation failed.",
                ex.Message);
        }
    }

    private async Task<ArchiveOperationResult<SqliteConnection>> EnsureConnectionAsync(
        string databasePath,
        ArchiveOptions options,
        CancellationToken cancellationToken)
    {
        if (_connection is not null
            && string.Equals(_databasePath, databasePath, StringComparison.Ordinal)
            && _connection.State == System.Data.ConnectionState.Open)
        {
            return ArchiveOperationResult<SqliteConnection>.Success(_connection);
        }

        await DisposeConnectionAsync().ConfigureAwait(false);

        var initializationOptions = new ArchiveDatabaseInitializationOptions
        {
            DatabasePath = databasePath,
            DeviceId = options.DeviceId.Trim(),
            ApplicationVersion = GetApplicationVersion(),
            ArchiveSchemaVersion = 1,
            BusyTimeoutMs = options.BusyTimeoutMs
        };

        var initializeResult = await _migrationRunner
            .InitializeAsync(initializationOptions, cancellationToken)
            .ConfigureAwait(false);
        if (!initializeResult.Succeeded)
        {
            return ArchiveOperationResult<SqliteConnection>.Failure(
                initializeResult.ErrorCode ?? ArchivePersistenceErrorCodes.ArchiveWriteFailed,
                initializeResult.ErrorMessage ?? "Archive database initialization failed.",
                initializeResult.ErrorDetails);
        }

        var connectionResult = await _connectionFactory.OpenAsync(initializationOptions, cancellationToken).ConfigureAwait(false);
        if (!connectionResult.Succeeded || connectionResult.Value is null)
        {
            return connectionResult;
        }

        var pragmaResult = await _pragmaInitializer
            .ApplyAsync(connectionResult.Value, options.BusyTimeoutMs, cancellationToken)
            .ConfigureAwait(false);
        if (!pragmaResult.Succeeded)
        {
            await connectionResult.Value.DisposeAsync().ConfigureAwait(false);

            return ArchiveOperationResult<SqliteConnection>.Failure(
                pragmaResult.ErrorCode ?? ArchivePersistenceErrorCodes.ArchiveWriteFailed,
                pragmaResult.ErrorMessage ?? "Archive database PRAGMA initialization failed.",
                pragmaResult.ErrorDetails);
        }

        _connection = connectionResult.Value;
        _databasePath = databasePath;

        return ArchiveOperationResult<SqliteConnection>.Success(_connection);
    }

    private static async Task<ArchiveOperationResult> InsertGroupAsync(
        SqliteConnection connection,
        IReadOnlyList<SnapshotWriteItem> items,
        CancellationToken cancellationToken)
    {
        try
        {
            using var transaction = connection.BeginTransaction();

            foreach (var item in items)
            {
                await InsertSnapshotAsync(connection, transaction, item, cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();

            return ArchiveOperationResult.Success();
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return ArchiveOperationResult.Failure(
                ArchivePersistenceErrorCodes.ArchiveWriteFailed,
                "Archive snapshot batch write failed.",
                ex.Message);
        }
    }

    private static async Task InsertSnapshotAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SnapshotWriteItem item,
        CancellationToken cancellationToken)
    {
        var record = item.Record;
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
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
        insert.Parameters.AddWithValue("@id", record.Id.ToString("D"));
        insert.Parameters.AddWithValue("@deviceId", record.DeviceId);
        insert.Parameters.AddWithValue("@runtimeRole", (int)record.Role);
        insert.Parameters.AddWithValue("@capturedAtUtcMs", record.CapturedAtUtc.ToUnixTimeMilliseconds());
        insert.Parameters.AddWithValue("@sequenceNumber", record.SequenceNumber);
        insert.Parameters.AddWithValue("@resolutionClass", (int)record.Resolution);
        insert.Parameters.AddWithValue("@coilStartAddress", record.CoilStartAddress);
        insert.Parameters.AddWithValue("@holdingRegisterStartAddress", record.HoldingRegisterStartAddress);
        insert.Parameters.AddWithValue("@coilCount", record.Coils.Count);
        insert.Parameters.AddWithValue("@holdingRegisterCount", record.HoldingRegisters.Count);
        insert.Parameters.AddWithValue("@coilsBlob", item.CoilsBlob);
        insert.Parameters.AddWithValue("@holdingRegistersBlob", item.HoldingRegistersBlob);
        insert.Parameters.AddWithValue("@configurationHash", record.ConfigurationHash);
        insert.Parameters.AddWithValue("@archiveSchemaVersion", record.SchemaVersion);
        insert.Parameters.AddWithValue("@createdAtUtcMs", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask DisposeConnectionAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
            _databasePath = null;
        }
    }

    private static string GetApplicationVersion()
        => typeof(SqliteArchiveWriter).Assembly.GetName().Version?.ToString() ?? "unknown";

    private sealed record SnapshotWriteItem(
        RawModbusSnapshotArchiveRecord Record,
        ArchivePartitionInfo Partition,
        byte[] CoilsBlob,
        byte[] HoldingRegistersBlob);
}
