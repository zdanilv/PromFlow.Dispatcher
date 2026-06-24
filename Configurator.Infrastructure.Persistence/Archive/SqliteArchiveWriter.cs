using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Infrastructure.Persistence.Common;
using Configurator.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using System.Text.Json;

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
        var prepareResult = PrepareBatchItems(envelopes, options);
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
            var group = new List<ArchiveWriteItem>();

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

    private ArchiveOperationResult<IReadOnlyList<ArchiveWriteItem>> PrepareBatchItems(
        IReadOnlyList<ArchiveEnvelope> envelopes,
        ArchiveOptions options)
    {
        var items = new List<ArchiveWriteItem>(envelopes.Count);

        try
        {
            foreach (var envelope in envelopes)
            {
                if (envelope.Kind == ArchiveRecordKind.RawModbusSnapshot)
                {
                    if (envelope.Record is not RawModbusSnapshotArchiveRecord record)
                    {
                        return ArchiveOperationResult<IReadOnlyList<ArchiveWriteItem>>.Failure(
                            ArchivePersistenceErrorCodes.ArchiveRecordTypeMismatch,
                            "Archive envelope record type does not match raw snapshot kind.",
                            envelope.Record.GetType().FullName);
                    }

                    var partition = _partitionResolver.GetWritablePartition(options, record.CapturedAtUtc);
                    items.Add(ArchiveWriteItem.ForSnapshot(
                        record,
                        partition,
                        _blobCodec.EncodeCoils(record.Coils),
                        _blobCodec.EncodeHoldingRegisters(record.HoldingRegisters)));
                    continue;
                }

                if (envelope.Kind == ArchiveRecordKind.ModbusStatus)
                {
                    if (envelope.Record is not ModbusStatusArchiveRecord record)
                    {
                        return ArchiveOperationResult<IReadOnlyList<ArchiveWriteItem>>.Failure(
                            ArchivePersistenceErrorCodes.ArchiveRecordTypeMismatch,
                            "Archive envelope record type does not match Modbus status kind.",
                            envelope.Record.GetType().FullName);
                    }

                    var partition = _partitionResolver.GetWritablePartition(options, record.OccurredAtUtc);
                    items.Add(ArchiveWriteItem.ForStatus(record, partition));
                    continue;
                }

                if (envelope.Kind == ArchiveRecordKind.EquipmentCommandAudit)
                {
                    if (envelope.Record is not EquipmentCommandAuditRecord record)
                    {
                        return ArchiveOperationResult<IReadOnlyList<ArchiveWriteItem>>.Failure(
                            ArchivePersistenceErrorCodes.ArchiveRecordTypeMismatch,
                            "Archive envelope record type does not match equipment command audit kind.",
                            envelope.Record.GetType().FullName);
                    }

                    var partition = _partitionResolver.GetWritablePartition(options, record.RequestedAtUtc);
                    items.Add(ArchiveWriteItem.ForCommand(record, partition));
                    continue;
                }

                if (envelope.Kind == ArchiveRecordKind.PhysicalModbusWriteAudit)
                {
                    if (envelope.Record is not PhysicalModbusWriteAuditRecord record)
                    {
                        return ArchiveOperationResult<IReadOnlyList<ArchiveWriteItem>>.Failure(
                            ArchivePersistenceErrorCodes.ArchiveRecordTypeMismatch,
                            "Archive envelope record type does not match physical Modbus write audit kind.",
                            envelope.Record.GetType().FullName);
                    }

                    var partition = _partitionResolver.GetWritablePartition(options, record.AttemptedAtUtc);
                    items.Add(ArchiveWriteItem.ForPhysicalWrite(record, partition));
                    continue;
                }

                if (envelope.Kind == ArchiveRecordKind.SecurityAudit)
                {
                    if (envelope.Record is not SecurityAuditRecord record)
                    {
                        return ArchiveOperationResult<IReadOnlyList<ArchiveWriteItem>>.Failure(
                            ArchivePersistenceErrorCodes.ArchiveRecordTypeMismatch,
                            "Archive envelope record type does not match security audit kind.",
                            envelope.Record.GetType().FullName);
                    }

                    var partition = _partitionResolver.GetWritablePartition(options, record.OccurredAtUtc);
                    items.Add(ArchiveWriteItem.ForSecurityAudit(record, partition));
                    continue;
                }

                return ArchiveOperationResult<IReadOnlyList<ArchiveWriteItem>>.Failure(
                    ArchivePersistenceErrorCodes.ArchiveRecordKindUnsupported,
                    "Archive writer does not support the supplied archive record kind.",
                    envelope.Kind.ToString());
            }

            return ArchiveOperationResult<IReadOnlyList<ArchiveWriteItem>>.Success(items);
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or OverflowException)
        {
            return ArchiveOperationResult<IReadOnlyList<ArchiveWriteItem>>.Failure(
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
        IReadOnlyList<ArchiveWriteItem> items,
        CancellationToken cancellationToken)
    {
        try
        {
            using var transaction = connection.BeginTransaction();

            foreach (var item in items)
            {
                if (item.SnapshotRecord is not null)
                {
                    await InsertSnapshotAsync(connection, transaction, item, cancellationToken).ConfigureAwait(false);
                }
                else if (item.StatusRecord is not null)
                {
                    await InsertStatusAsync(connection, transaction, item.StatusRecord, cancellationToken).ConfigureAwait(false);
                }
                else if (item.CommandRecord is not null)
                {
                    await UpsertCommandAsync(connection, transaction, item.CommandRecord, cancellationToken).ConfigureAwait(false);
                }
                else if (item.PhysicalWriteRecord is not null)
                {
                    await InsertPhysicalWriteAsync(connection, transaction, item.PhysicalWriteRecord, cancellationToken).ConfigureAwait(false);
                }
                else if (item.SecurityAuditRecord is not null)
                {
                    await InsertSecurityAuditAsync(connection, transaction, item.SecurityAuditRecord, cancellationToken).ConfigureAwait(false);
                }
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
        ArchiveWriteItem item,
        CancellationToken cancellationToken)
    {
        var record = item.SnapshotRecord ?? throw new InvalidOperationException("Snapshot write item is missing a record.");
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
        insert.Parameters.AddWithValue("@coilsBlob", item.CoilsBlob ?? Array.Empty<byte>());
        insert.Parameters.AddWithValue("@holdingRegistersBlob", item.HoldingRegistersBlob ?? Array.Empty<byte>());
        insert.Parameters.AddWithValue("@configurationHash", record.ConfigurationHash);
        insert.Parameters.AddWithValue("@archiveSchemaVersion", record.SchemaVersion);
        insert.Parameters.AddWithValue("@createdAtUtcMs", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpsertCommandAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        EquipmentCommandAuditRecord record,
        CancellationToken cancellationToken)
    {
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO equipment_command
            (command_id, correlation_id, requested_at_utc_ms, completed_at_utc_ms,
             session_id, user_id, username, device_id, signal_id, value_type,
             requested_value_canonical, write_mode, result, error_code, error_message,
             confirmation_status, confirmed_at_utc_ms, archive_schema_version,
             created_at_utc_ms, updated_at_utc_ms)
            VALUES
            (@commandId, @correlationId, @requestedAtUtcMs, @completedAtUtcMs,
             @sessionId, @userId, @username, @deviceId, @signalId, @valueType,
             @requestedValueCanonical, @writeMode, @result, @errorCode, @errorMessage,
             @confirmationStatus, @confirmedAtUtcMs, @archiveSchemaVersion,
             @createdAtUtcMs, @updatedAtUtcMs)
            ON CONFLICT(command_id) DO UPDATE SET
                correlation_id = excluded.correlation_id,
                requested_at_utc_ms = excluded.requested_at_utc_ms,
                completed_at_utc_ms = excluded.completed_at_utc_ms,
                session_id = excluded.session_id,
                user_id = excluded.user_id,
                username = excluded.username,
                device_id = excluded.device_id,
                signal_id = excluded.signal_id,
                value_type = excluded.value_type,
                requested_value_canonical = excluded.requested_value_canonical,
                write_mode = excluded.write_mode,
                result = excluded.result,
                error_code = excluded.error_code,
                error_message = excluded.error_message,
                confirmation_status = excluded.confirmation_status,
                confirmed_at_utc_ms = excluded.confirmed_at_utc_ms,
                archive_schema_version = excluded.archive_schema_version,
                updated_at_utc_ms = excluded.updated_at_utc_ms;
            """;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        insert.Parameters.AddWithValue("@commandId", record.CommandId.ToString("D"));
        insert.Parameters.AddWithValue("@correlationId", record.CorrelationId.ToString("D"));
        insert.Parameters.AddWithValue("@requestedAtUtcMs", record.RequestedAtUtc.ToUnixTimeMilliseconds());
        insert.Parameters.AddWithValue("@completedAtUtcMs", DbValue(record.CompletedAtUtc?.ToUnixTimeMilliseconds()));
        insert.Parameters.AddWithValue("@sessionId", DbValue(record.SessionId));
        insert.Parameters.AddWithValue("@userId", DbValue(record.UserId));
        insert.Parameters.AddWithValue("@username", DbValue(record.Username));
        insert.Parameters.AddWithValue("@deviceId", record.DeviceId);
        insert.Parameters.AddWithValue("@signalId", record.SignalId);
        insert.Parameters.AddWithValue("@valueType", (int)record.ValueType);
        insert.Parameters.AddWithValue("@requestedValueCanonical", record.RequestedValueCanonical);
        insert.Parameters.AddWithValue(
            "@writeMode",
            DbValue(record.WriteMode.HasValue ? (int?)record.WriteMode.Value : null));
        var commandOutcome = record switch { { Result: var value } => value };
        insert.Parameters.AddWithValue("@result", (int)commandOutcome);
        insert.Parameters.AddWithValue("@errorCode", DbValue(record.ErrorCode));
        insert.Parameters.AddWithValue("@errorMessage", DbValue(record.ErrorMessage));
        insert.Parameters.AddWithValue("@confirmationStatus", (int)record.ConfirmationStatus);
        insert.Parameters.AddWithValue("@confirmedAtUtcMs", DbValue(record.ConfirmedAtUtc?.ToUnixTimeMilliseconds()));
        insert.Parameters.AddWithValue("@archiveSchemaVersion", record.SchemaVersion);
        insert.Parameters.AddWithValue("@createdAtUtcMs", now);
        insert.Parameters.AddWithValue("@updatedAtUtcMs", now);

        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertPhysicalWriteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PhysicalModbusWriteAuditRecord record,
        CancellationToken cancellationToken)
    {
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO modbus_write
            (write_id, command_id, attempted_at_utc_ms, completed_at_utc_ms,
             runtime_role, area, address, quantity, payload_blob, succeeded,
             error_code, error_message, archive_schema_version, created_at_utc_ms)
            VALUES
            (@writeId, @commandId, @attemptedAtUtcMs, @completedAtUtcMs,
             @runtimeRole, @area, @address, @quantity, @payloadBlob, @succeeded,
             @errorCode, @errorMessage, @archiveSchemaVersion, @createdAtUtcMs);
            """;
        insert.Parameters.AddWithValue("@writeId", record.WriteId.ToString("D"));
        insert.Parameters.AddWithValue("@commandId", DbValue(record.CommandId?.ToString("D")));
        insert.Parameters.AddWithValue("@attemptedAtUtcMs", record.AttemptedAtUtc.ToUnixTimeMilliseconds());
        insert.Parameters.AddWithValue("@completedAtUtcMs", DbValue(record.CompletedAtUtc?.ToUnixTimeMilliseconds()));
        insert.Parameters.AddWithValue("@runtimeRole", (int)record.Role);
        insert.Parameters.AddWithValue("@area", (int)record.Area);
        insert.Parameters.AddWithValue("@address", record.Address);
        insert.Parameters.AddWithValue("@quantity", record.Quantity);
        insert.Parameters.AddWithValue("@payloadBlob", record.PayloadBlob.ToArray());
        insert.Parameters.AddWithValue("@succeeded", record.Succeeded ? 1 : 0);
        insert.Parameters.AddWithValue("@errorCode", DbValue(record.ErrorCode));
        insert.Parameters.AddWithValue("@errorMessage", DbValue(record.ErrorMessage));
        insert.Parameters.AddWithValue("@archiveSchemaVersion", record.SchemaVersion);
        insert.Parameters.AddWithValue("@createdAtUtcMs", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertSecurityAuditAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SecurityAuditRecord record,
        CancellationToken cancellationToken)
    {
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO security_audit
            (id, occurred_at_utc_ms, event_type, severity, actor_user_id, actor_username,
             session_id, target_user_id, result, reason_code, details_json)
            VALUES
            (@id, @occurredAtUtcMs, @eventType, @severity, @actorUserId, @actorUsername,
             @sessionId, @targetUserId, @result, @reasonCode, @detailsJson);
            """;
        insert.Parameters.AddWithValue("@id", record.Id.ToString("D"));
        insert.Parameters.AddWithValue("@occurredAtUtcMs", record.OccurredAtUtc.ToUnixTimeMilliseconds());
        insert.Parameters.AddWithValue("@eventType", record.EventType);
        insert.Parameters.AddWithValue("@severity", (int)record.Severity);
        insert.Parameters.AddWithValue("@actorUserId", DbValue(record.ActorUserId));
        insert.Parameters.AddWithValue("@actorUsername", DbValue(record.ActorUsername));
        insert.Parameters.AddWithValue("@sessionId", DbValue(record.SessionId));
        insert.Parameters.AddWithValue("@targetUserId", DbValue(record.TargetUserId));
        var auditResult = record switch { { Result: var value } => value };
        insert.Parameters.AddWithValue("@result", (int)auditResult);
        insert.Parameters.AddWithValue("@reasonCode", DbValue(record.ReasonCode));
        insert.Parameters.AddWithValue("@detailsJson", DbValue(record.DetailsJson));

        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertStatusAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ModbusStatusArchiveRecord record,
        CancellationToken cancellationToken)
    {
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO runtime_event
            (id, occurred_at_utc_ms, device_id, event_type, severity, message, details_json)
            VALUES
            (@id, @occurredAtUtcMs, @deviceId, @eventType, @severity, @message, @detailsJson);
            """;
        insert.Parameters.AddWithValue("@id", record.Id.ToString("D"));
        insert.Parameters.AddWithValue("@occurredAtUtcMs", record.OccurredAtUtc.ToUnixTimeMilliseconds());
        insert.Parameters.AddWithValue("@deviceId", record.DeviceId);
        insert.Parameters.AddWithValue("@eventType", "ModbusStatus");
        insert.Parameters.AddWithValue("@severity", GetStatusSeverity(record));
        insert.Parameters.AddWithValue("@message", BuildStatusMessage(record));
        insert.Parameters.AddWithValue("@detailsJson", BuildStatusDetailsJson(record));

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

    private static int GetStatusSeverity(ModbusStatusArchiveRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.LastError)
            || record.ClientState == ModbusConnectionState.Faulted
            || record.ServerState == ModbusConnectionState.Faulted)
        {
            return 3;
        }

        if (record.ClientState == ModbusConnectionState.Reconnecting
            || record.ServerState == ModbusConnectionState.Reconnecting)
        {
            return 2;
        }

        return 1;
    }

    private static string BuildStatusMessage(ModbusStatusArchiveRecord record)
        => $"{record.ClientState}/{record.ServerState}: {record.ClientMessage} {record.ServerMessage}".Trim();

    private static string BuildStatusDetailsJson(ModbusStatusArchiveRecord record)
        => JsonSerializer.Serialize(new
        {
            clientState = record.ClientState.ToString(),
            serverState = record.ServerState.ToString(),
            clientMessage = record.ClientMessage,
            serverMessage = record.ServerMessage,
            lastError = record.LastError
        });

    private static object DbValue<T>(T? value)
        => value is null ? DBNull.Value : value;

    private sealed record ArchiveWriteItem(
        RawModbusSnapshotArchiveRecord? SnapshotRecord,
        ModbusStatusArchiveRecord? StatusRecord,
        EquipmentCommandAuditRecord? CommandRecord,
        PhysicalModbusWriteAuditRecord? PhysicalWriteRecord,
        SecurityAuditRecord? SecurityAuditRecord,
        ArchivePartitionInfo Partition,
        byte[]? CoilsBlob,
        byte[]? HoldingRegistersBlob)
    {
        public static ArchiveWriteItem ForSnapshot(
            RawModbusSnapshotArchiveRecord record,
            ArchivePartitionInfo partition,
            byte[] coilsBlob,
            byte[] holdingRegistersBlob)
            => new(record, null, null, null, null, partition, coilsBlob, holdingRegistersBlob);

        public static ArchiveWriteItem ForStatus(
            ModbusStatusArchiveRecord record,
            ArchivePartitionInfo partition)
            => new(null, record, null, null, null, partition, null, null);

        public static ArchiveWriteItem ForCommand(
            EquipmentCommandAuditRecord record,
            ArchivePartitionInfo partition)
            => new(null, null, record, null, null, partition, null, null);

        public static ArchiveWriteItem ForPhysicalWrite(
            PhysicalModbusWriteAuditRecord record,
            ArchivePartitionInfo partition)
            => new(null, null, null, record, null, partition, null, null);

        public static ArchiveWriteItem ForSecurityAudit(
            SecurityAuditRecord record,
            ArchivePartitionInfo partition)
            => new(null, null, null, null, record, partition, null, null);
    }
}
