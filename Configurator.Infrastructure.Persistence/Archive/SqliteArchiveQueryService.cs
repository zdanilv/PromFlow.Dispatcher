using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Signals;
using Configurator.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Configurator.Infrastructure.Persistence.Archive;

public sealed class SqliteArchiveQueryService : IArchiveQueryService
{
    private readonly ArchivePartitionCatalog _partitionCatalog;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SqlitePragmaInitializer _pragmaInitializer;
    private readonly ArchiveSnapshotBlobCodec _blobCodec;
    private readonly IOptions<ArchiveOptions> _options;

    public SqliteArchiveQueryService(
        ArchivePartitionCatalog partitionCatalog,
        SqliteConnectionFactory connectionFactory,
        SqlitePragmaInitializer pragmaInitializer,
        ArchiveSnapshotBlobCodec blobCodec,
        IOptions<ArchiveOptions> options)
    {
        _partitionCatalog = partitionCatalog ?? throw new ArgumentNullException(nameof(partitionCatalog));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _pragmaInitializer = pragmaInitializer ?? throw new ArgumentNullException(nameof(pragmaInitializer));
        _blobCodec = blobCodec ?? throw new ArgumentNullException(nameof(blobCodec));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public Task<ArchiveOperationResult<ArchivePage<RawModbusSnapshotArchiveRecord>>> QueryRawSnapshotsAsync(
        ArchiveQuery query,
        CancellationToken cancellationToken = default)
        => QueryPartitionsAsync(
            query,
            ArchiveRecordKind.RawModbusSnapshot,
            "modbus_snapshot",
            CountSnapshotsAsync,
            ReadSnapshotsAsync,
            cancellationToken);

    public Task<ArchiveOperationResult<ArchivePage<ModbusStatusArchiveRecord>>> QueryModbusStatusesAsync(
        ArchiveQuery query,
        CancellationToken cancellationToken = default)
        => QueryPartitionsAsync(
            query,
            ArchiveRecordKind.ModbusStatus,
            "runtime_event",
            CountModbusStatusesAsync,
            ReadModbusStatusesAsync,
            cancellationToken);

    public Task<ArchiveOperationResult<ArchivePage<ArchiveRuntimeEventRecord>>> QueryRuntimeEventsAsync(
        ArchiveQuery query,
        CancellationToken cancellationToken = default)
        => QueryPartitionsAsync(
            query,
            null,
            "runtime_event",
            CountRuntimeEventsAsync,
            ReadRuntimeEventsAsync,
            cancellationToken);

    public Task<ArchiveOperationResult<ArchivePage<EquipmentCommandAuditRecord>>> QueryEquipmentCommandsAsync(
        ArchiveQuery query,
        CancellationToken cancellationToken = default)
        => QueryPartitionsAsync(
            query,
            ArchiveRecordKind.EquipmentCommandAudit,
            "equipment_command",
            CountEquipmentCommandsAsync,
            ReadEquipmentCommandsAsync,
            cancellationToken);

    public Task<ArchiveOperationResult<ArchivePage<PhysicalModbusWriteAuditRecord>>> QueryPhysicalWritesAsync(
        ArchiveQuery query,
        CancellationToken cancellationToken = default)
        => QueryPartitionsAsync(
            query,
            ArchiveRecordKind.PhysicalModbusWriteAudit,
            "modbus_write",
            CountPhysicalWritesAsync,
            ReadPhysicalWritesAsync,
            cancellationToken);

    public Task<ArchiveOperationResult<ArchivePage<SecurityAuditRecord>>> QuerySecurityAuditAsync(
        ArchiveQuery query,
        CancellationToken cancellationToken = default)
        => QueryPartitionsAsync(
            query,
            ArchiveRecordKind.SecurityAudit,
            "security_audit",
            CountSecurityAuditAsync,
            ReadSecurityAuditAsync,
            cancellationToken);

    private async Task<ArchiveOperationResult<ArchivePage<T>>> QueryPartitionsAsync<T>(
        ArchiveQuery query,
        ArchiveRecordKind? expectedKind,
        string tableName,
        Func<SqliteConnection, ArchiveQuery, CancellationToken, Task<long>> countRowsAsync,
        Func<SqliteConnection, ArchiveQuery, long, int, CancellationToken, Task<IReadOnlyList<T>>> readRowsAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var options = _options.Value.Clone();
        var validation = ValidateQuery(query, expectedKind, options);
        if (!validation.Succeeded)
        {
            return ArchiveOperationResult<ArchivePage<T>>.Failure(
                validation.ErrorCode ?? ArchivePersistenceErrorCodes.ArchiveQueryInvalid,
                validation.ErrorMessage ?? "Archive query is invalid.",
                validation.ErrorDetails);
        }

        if (expectedKind is not null && query.RecordKind is ArchiveRecordKind concreteKind && concreteKind != expectedKind)
        {
            return ArchiveOperationResult<ArchivePage<T>>.Success(CreatePage(Array.Empty<T>(), query, totalCount: 0));
        }

        var partitions = _partitionCatalog.GetExistingPartitions(options, query);
        if (query.SortDirection == ArchiveSortDirection.Descending)
        {
            partitions = partitions.Reverse().ToArray();
        }

        var pageOffset = GetPageOffset(query);
        var remainingSkip = pageOffset;
        var totalCount = 0L;
        var items = new List<T>(query.PageSize);

        try
        {
            foreach (var partition in partitions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var connectionResult = await _connectionFactory
                    .OpenReadOnlyAsync(partition.DatabasePath, cancellationToken)
                    .ConfigureAwait(false);
                if (!connectionResult.Succeeded || connectionResult.Value is null)
                {
                    if (connectionResult.ErrorCode == ArchivePersistenceErrorCodes.ArchivePartitionMissing)
                    {
                        continue;
                    }

                    return ArchiveOperationResult<ArchivePage<T>>.Failure(
                        connectionResult.ErrorCode ?? ArchivePersistenceErrorCodes.ArchiveQueryFailed,
                        connectionResult.ErrorMessage ?? "Archive partition could not be opened.",
                        connectionResult.ErrorDetails ?? partition.DatabasePath);
                }

                await using var connection = connectionResult.Value;
                var pragmaResult = await _pragmaInitializer
                    .ApplyReadOnlyAsync(connection, options.BusyTimeoutMs, cancellationToken)
                    .ConfigureAwait(false);
                if (!pragmaResult.Succeeded)
                {
                    return ArchiveOperationResult<ArchivePage<T>>.Failure(
                        pragmaResult.ErrorCode ?? ArchivePersistenceErrorCodes.ArchiveQueryFailed,
                        pragmaResult.ErrorMessage ?? "Archive partition read-only PRAGMA failed.",
                        pragmaResult.ErrorDetails ?? partition.DatabasePath);
                }

                if (!await TableExistsAsync(connection, tableName, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                var partitionCount = await countRowsAsync(connection, query, cancellationToken).ConfigureAwait(false);
                totalCount += partitionCount;

                if (remainingSkip >= partitionCount)
                {
                    remainingSkip -= partitionCount;
                    continue;
                }

                if (items.Count >= query.PageSize)
                {
                    continue;
                }

                var take = query.PageSize - items.Count;
                var rows = await readRowsAsync(connection, query, remainingSkip, take, cancellationToken).ConfigureAwait(false);
                items.AddRange(rows);
                remainingSkip = 0;
            }

            return ArchiveOperationResult<ArchivePage<T>>.Success(CreatePage(items, query, totalCount));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArchiveQueryMappingException ex)
        {
            return ArchiveOperationResult<ArchivePage<T>>.Failure(
                ArchivePersistenceErrorCodes.ArchivePartitionCorrupt,
                "Archive partition row could not be mapped.",
                ex.Message);
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or IOException or ArgumentException)
        {
            return ArchiveOperationResult<ArchivePage<T>>.Failure(
                ArchivePersistenceErrorCodes.ArchiveQueryFailed,
                "Archive query failed.",
                ex.Message);
        }
    }

    private static ArchiveOperationResult ValidateQuery(
        ArchiveQuery query,
        ArchiveRecordKind? expectedKind,
        ArchiveOptions options)
    {
        var requestedOutcome = query switch { { Result: var value } => value };

        if (query.PageSize > options.QueryMaxPageSize)
        {
            return ArchiveOperationResult.Failure(
                ArchivePersistenceErrorCodes.ArchiveQueryInvalid,
                "Archive query page size exceeds configured maximum.",
                options.QueryMaxPageSize.ToString(CultureInfo.InvariantCulture));
        }

        if (expectedKind == ArchiveRecordKind.EquipmentCommandAudit && !TryParseCommandOutcome(requestedOutcome, out _))
        {
            return ArchiveOperationResult.Failure(
                ArchivePersistenceErrorCodes.ArchiveQueryInvalid,
                "Archive command query result filter is invalid.",
                requestedOutcome);
        }

        if (expectedKind == ArchiveRecordKind.SecurityAudit && !TryParseSecurityOutcome(requestedOutcome, out _))
        {
            return ArchiveOperationResult.Failure(
                ArchivePersistenceErrorCodes.ArchiveQueryInvalid,
                "Archive security query result filter is invalid.",
                requestedOutcome);
        }

        return ArchiveOperationResult.Success();
    }

    private static ArchivePage<T> CreatePage<T>(IReadOnlyList<T> items, ArchiveQuery query, long totalCount)
    {
        var visibleCount = checked((long)query.PageNumber * query.PageSize);
        return new ArchivePage<T>(items, query.PageNumber, query.PageSize, totalCount, visibleCount < totalCount);
    }

    private static long GetPageOffset(ArchiveQuery query)
        => checked((long)(query.PageNumber - 1) * query.PageSize);

    private async Task<long> CountSnapshotsAsync(
        SqliteConnection connection,
        ArchiveQuery query,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var where = new StringBuilder("WHERE 1 = 1");
        AddTimeFilter(command, where, "captured_at_utc_ms", query);
        AddStringFilter(command, where, "device_id", "@deviceId", query.DeviceId);
        AddEnumFilter(command, where, "runtime_role", "@role", query.Role);
        command.CommandText = "SELECT COUNT(*) FROM modbus_snapshot " + where + ";";

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private async Task<IReadOnlyList<RawModbusSnapshotArchiveRecord>> ReadSnapshotsAsync(
        SqliteConnection connection,
        ArchiveQuery query,
        long offset,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var where = new StringBuilder("WHERE 1 = 1");
        AddTimeFilter(command, where, "captured_at_utc_ms", query);
        AddStringFilter(command, where, "device_id", "@deviceId", query.DeviceId);
        AddEnumFilter(command, where, "runtime_role", "@role", query.Role);
        AddPaging(command, offset, limit);
        var sort = GetSortSql(query);
        command.CommandText = $"""
            SELECT id, device_id, runtime_role, captured_at_utc_ms, sequence_number, resolution_class,
                   coil_start_address, holding_register_start_address, coils_blob, holding_registers_blob,
                   configuration_hash, archive_schema_version
            FROM modbus_snapshot
            {where}
            ORDER BY captured_at_utc_ms {sort}, sequence_number {sort}, id {sort}
            LIMIT @limit OFFSET @offset;
            """;

        var rows = new List<RawModbusSnapshotArchiveRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var coilsBlob = (byte[])reader["coils_blob"];
            var registersBlob = (byte[])reader["holding_registers_blob"];
            var coils = _blobCodec.DecodeCoils(coilsBlob);
            if (!coils.Succeeded || coils.Value is null)
            {
                throw new ArchiveQueryMappingException(coils.ErrorMessage ?? "Snapshot coil blob is invalid.");
            }

            var registers = _blobCodec.DecodeHoldingRegisters(registersBlob);
            if (!registers.Succeeded || registers.Value is null)
            {
                throw new ArchiveQueryMappingException(registers.ErrorMessage ?? "Snapshot register blob is invalid.");
            }

            rows.Add(new RawModbusSnapshotArchiveRecord(
                reader.GetGuidFromString(0),
                reader.GetString(1),
                (ModbusRuntimeRole)reader.GetInt32(2),
                reader.GetInt64(4),
                reader.GetUtcFromUnixMilliseconds(3),
                reader.GetInt32(6),
                reader.GetInt32(7),
                coils.Value,
                registers.Value,
                reader.GetString(10),
                (ArchiveResolution)reader.GetInt32(5),
                reader.GetInt32(11)));
        }

        return rows;
    }

    private static async Task<long> CountRuntimeEventsAsync(
        SqliteConnection connection,
        ArchiveQuery query,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var where = BuildRuntimeEventWhere(command, query, restrictToModbusStatus: false);
        command.CommandText = "SELECT COUNT(*) FROM runtime_event " + where + ";";

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private static async Task<IReadOnlyList<ArchiveRuntimeEventRecord>> ReadRuntimeEventsAsync(
        SqliteConnection connection,
        ArchiveQuery query,
        long offset,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var where = BuildRuntimeEventWhere(command, query, restrictToModbusStatus: false);
        AddPaging(command, offset, limit);
        var sort = GetSortSql(query);
        command.CommandText = $"""
            SELECT id, occurred_at_utc_ms, device_id, event_type, severity, message, details_json
            FROM runtime_event
            {where}
            ORDER BY occurred_at_utc_ms {sort}, id {sort}
            LIMIT @limit OFFSET @offset;
            """;

        var rows = new List<ArchiveRuntimeEventRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new ArchiveRuntimeEventRecord(
                reader.GetGuidFromString(0),
                reader.GetUtcFromUnixMilliseconds(1),
                reader.GetNullableString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetString(5),
                reader.GetNullableString(6)));
        }

        return rows;
    }

    private static async Task<long> CountModbusStatusesAsync(
        SqliteConnection connection,
        ArchiveQuery query,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var where = BuildRuntimeEventWhere(command, query, restrictToModbusStatus: true);
        command.CommandText = "SELECT COUNT(*) FROM runtime_event " + where + ";";

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private static async Task<IReadOnlyList<ModbusStatusArchiveRecord>> ReadModbusStatusesAsync(
        SqliteConnection connection,
        ArchiveQuery query,
        long offset,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var where = BuildRuntimeEventWhere(command, query, restrictToModbusStatus: true);
        AddPaging(command, offset, limit);
        var sort = GetSortSql(query);
        command.CommandText = $"""
            SELECT id, occurred_at_utc_ms, device_id, details_json
            FROM runtime_event
            {where}
            ORDER BY occurred_at_utc_ms {sort}, id {sort}
            LIMIT @limit OFFSET @offset;
            """;

        var rows = new List<ModbusStatusArchiveRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(CreateModbusStatusRecord(reader));
        }

        return rows;
    }

    private static async Task<long> CountEquipmentCommandsAsync(
        SqliteConnection connection,
        ArchiveQuery query,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var where = BuildCommandWhere(command, query);
        command.CommandText = "SELECT COUNT(*) FROM equipment_command " + where + ";";

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private static async Task<IReadOnlyList<EquipmentCommandAuditRecord>> ReadEquipmentCommandsAsync(
        SqliteConnection connection,
        ArchiveQuery query,
        long offset,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var where = BuildCommandWhere(command, query);
        AddPaging(command, offset, limit);
        var sort = GetSortSql(query);
        command.CommandText = $"""
            SELECT command_id, correlation_id, requested_at_utc_ms, completed_at_utc_ms,
                   session_id, user_id, username, device_id, signal_id, value_type,
                   requested_value_canonical, write_mode, result, error_code, error_message,
                   confirmation_status, confirmed_at_utc_ms, archive_schema_version
            FROM equipment_command
            {where}
            ORDER BY requested_at_utc_ms {sort}, command_id {sort}
            LIMIT @limit OFFSET @offset;
            """;

        var rows = new List<EquipmentCommandAuditRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var writeMode = reader.GetNullableInt32(11);
            rows.Add(new EquipmentCommandAuditRecord(
                reader.GetGuidFromString(0),
                reader.GetGuidFromString(1),
                reader.GetUtcFromUnixMilliseconds(2),
                reader.GetNullableUtcFromUnixMilliseconds(3),
                reader.GetNullableString(4),
                reader.GetNullableString(5),
                reader.GetNullableString(6),
                reader.GetString(7),
                reader.GetString(8),
                (SignalValueType)reader.GetInt32(9),
                reader.GetString(10),
                writeMode is null ? null : (ModbusWriteMode)writeMode.Value,
                (EquipmentCommandAuditResult)reader.GetInt32(12),
                reader.GetNullableString(13),
                reader.GetNullableString(14),
                (CommandConfirmationStatus)reader.GetInt32(15),
                reader.GetNullableUtcFromUnixMilliseconds(16),
                reader.GetInt32(17)));
        }

        return rows;
    }

    private static async Task<long> CountPhysicalWritesAsync(
        SqliteConnection connection,
        ArchiveQuery query,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var where = BuildPhysicalWriteWhere(command, query);
        command.CommandText = "SELECT COUNT(*) FROM modbus_write w LEFT JOIN equipment_command c ON c.command_id = w.command_id " + where + ";";

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private static async Task<IReadOnlyList<PhysicalModbusWriteAuditRecord>> ReadPhysicalWritesAsync(
        SqliteConnection connection,
        ArchiveQuery query,
        long offset,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var where = BuildPhysicalWriteWhere(command, query);
        AddPaging(command, offset, limit);
        var sort = GetSortSql(query);
        command.CommandText = $"""
            SELECT w.write_id, w.command_id, w.attempted_at_utc_ms, w.completed_at_utc_ms,
                   w.runtime_role, w.area, w.address, w.quantity, w.payload_blob, w.succeeded,
                   w.error_code, w.error_message, w.archive_schema_version
            FROM modbus_write w
            LEFT JOIN equipment_command c ON c.command_id = w.command_id
            {where}
            ORDER BY w.attempted_at_utc_ms {sort}, w.write_id {sort}
            LIMIT @limit OFFSET @offset;
            """;

        var rows = new List<PhysicalModbusWriteAuditRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var commandIdText = reader.GetNullableString(1);
            rows.Add(new PhysicalModbusWriteAuditRecord(
                reader.GetGuidFromString(0),
                commandIdText is null ? null : Guid.Parse(commandIdText),
                reader.GetUtcFromUnixMilliseconds(2),
                reader.GetNullableUtcFromUnixMilliseconds(3),
                (ModbusRuntimeRole)reader.GetInt32(4),
                (ModbusDataArea)reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                (byte[])reader["payload_blob"],
                reader.GetInt32(9) != 0,
                reader.GetNullableString(10),
                reader.GetNullableString(11),
                reader.GetInt32(12)));
        }

        return rows;
    }

    private static async Task<long> CountSecurityAuditAsync(
        SqliteConnection connection,
        ArchiveQuery query,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var where = BuildSecurityAuditWhere(command, query);
        command.CommandText = "SELECT COUNT(*) FROM security_audit " + where + ";";

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private static async Task<IReadOnlyList<SecurityAuditRecord>> ReadSecurityAuditAsync(
        SqliteConnection connection,
        ArchiveQuery query,
        long offset,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var where = BuildSecurityAuditWhere(command, query);
        AddPaging(command, offset, limit);
        var sort = GetSortSql(query);
        command.CommandText = $"""
            SELECT id, occurred_at_utc_ms, event_type, severity, actor_user_id, actor_username,
                   session_id, target_user_id, result, reason_code, details_json
            FROM security_audit
            {where}
            ORDER BY occurred_at_utc_ms {sort}, id {sort}
            LIMIT @limit OFFSET @offset;
            """;

        var rows = new List<SecurityAuditRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new SecurityAuditRecord(
                reader.GetGuidFromString(0),
                reader.GetUtcFromUnixMilliseconds(1),
                reader.GetString(2),
                (SecurityAuditSeverity)reader.GetInt32(3),
                reader.GetNullableString(4),
                reader.GetNullableString(5),
                reader.GetNullableString(6),
                reader.GetNullableString(7),
                (SecurityAuditResult)reader.GetInt32(8),
                reader.GetNullableString(9),
                reader.GetNullableString(10),
                schemaVersion: 1));
        }

        return rows;
    }

    private static StringBuilder BuildRuntimeEventWhere(
        SqliteCommand command,
        ArchiveQuery query,
        bool restrictToModbusStatus)
    {
        var where = new StringBuilder("WHERE 1 = 1");
        AddTimeFilter(command, where, "occurred_at_utc_ms", query);
        AddStringFilter(command, where, "device_id", "@deviceId", query.DeviceId);

        if (restrictToModbusStatus)
        {
            AddStringFilter(command, where, "event_type", "@eventType", "ModbusStatus");
        }
        else
        {
            AddStringFilter(command, where, "event_type", "@eventType", query.EventType);
        }

        return where;
    }

    private static StringBuilder BuildCommandWhere(SqliteCommand command, ArchiveQuery query)
    {
        var requestedOutcome = query switch { { Result: var value } => value };
        var where = new StringBuilder("WHERE 1 = 1");
        AddTimeFilter(command, where, "requested_at_utc_ms", query);
        AddStringFilter(command, where, "device_id", "@deviceId", query.DeviceId);
        AddStringFilter(command, where, "signal_id", "@signalId", query.SignalId);
        AddStringFilter(command, where, "user_id", "@userId", query.UserId);
        if (TryParseCommandOutcome(requestedOutcome, out var commandOutcome) && commandOutcome is not null)
        {
            AddIntFilter(command, where, "result", "@commandOutcome", commandOutcome.Value);
        }

        return where;
    }

    private static StringBuilder BuildPhysicalWriteWhere(SqliteCommand command, ArchiveQuery query)
    {
        var requestedOutcome = query switch { { Result: var value } => value };
        var where = new StringBuilder("WHERE 1 = 1");
        AddTimeFilter(command, where, "w.attempted_at_utc_ms", query);
        AddEnumFilter(command, where, "w.runtime_role", "@role", query.Role);
        AddStringFilter(command, where, "c.device_id", "@deviceId", query.DeviceId);
        AddStringFilter(command, where, "c.signal_id", "@signalId", query.SignalId);
        AddStringFilter(command, where, "c.user_id", "@userId", query.UserId);
        if (TryParseCommandOutcome(requestedOutcome, out var commandOutcome) && commandOutcome is not null)
        {
            AddIntFilter(command, where, "c.result", "@commandOutcome", commandOutcome.Value);
        }

        return where;
    }

    private static StringBuilder BuildSecurityAuditWhere(SqliteCommand command, ArchiveQuery query)
    {
        var requestedOutcome = query switch { { Result: var value } => value };
        var where = new StringBuilder("WHERE 1 = 1");
        AddTimeFilter(command, where, "occurred_at_utc_ms", query);
        AddStringFilter(command, where, "event_type", "@eventType", query.EventType);
        AddStringFilter(command, where, "actor_user_id", "@userId", query.UserId);
        if (TryParseSecurityOutcome(requestedOutcome, out var securityOutcome) && securityOutcome is not null)
        {
            AddIntFilter(command, where, "result", "@securityOutcome", securityOutcome.Value);
        }

        return where;
    }

    private static void AddTimeFilter(SqliteCommand command, StringBuilder where, string columnName, ArchiveQuery query)
    {
        if (query.FromUtc is DateTimeOffset fromUtc)
        {
            where.Append(CultureInfo.InvariantCulture, $" AND {columnName} >= @fromUtcMs");
            command.Parameters.AddWithValue("@fromUtcMs", fromUtc.ToUnixTimeMilliseconds());
        }

        if (query.ToUtc is DateTimeOffset toUtc)
        {
            where.Append(CultureInfo.InvariantCulture, $" AND {columnName} <= @toUtcMs");
            command.Parameters.AddWithValue("@toUtcMs", toUtc.ToUnixTimeMilliseconds());
        }
    }

    private static void AddStringFilter(
        SqliteCommand command,
        StringBuilder where,
        string columnName,
        string parameterName,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        where.Append(CultureInfo.InvariantCulture, $" AND {columnName} = {parameterName}");
        command.Parameters.AddWithValue(parameterName, value.Trim());
    }

    private static void AddEnumFilter<T>(
        SqliteCommand command,
        StringBuilder where,
        string columnName,
        string parameterName,
        T? value)
        where T : struct, Enum
    {
        if (value is null)
        {
            return;
        }

        where.Append(CultureInfo.InvariantCulture, $" AND {columnName} = {parameterName}");
        command.Parameters.AddWithValue(parameterName, Convert.ToInt32(value.Value, CultureInfo.InvariantCulture));
    }

    private static void AddIntFilter(
        SqliteCommand command,
        StringBuilder where,
        string columnName,
        string parameterName,
        int value)
    {
        where.Append(CultureInfo.InvariantCulture, $" AND {columnName} = {parameterName}");
        command.Parameters.AddWithValue(parameterName, value);
    }

    private static void AddPaging(SqliteCommand command, long offset, int limit)
    {
        command.Parameters.AddWithValue("@offset", offset);
        command.Parameters.AddWithValue("@limit", limit);
    }

    private static string GetSortSql(ArchiveQuery query)
        => query.SortDirection == ArchiveSortDirection.Ascending ? "ASC" : "DESC";

    private static bool TryParseCommandOutcome(string? value, out int? commandOutcome)
    {
        commandOutcome = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (Enum.TryParse<EquipmentCommandAuditResult>(value.Trim(), ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed))
        {
            commandOutcome = (int)parsed;
            return true;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)
            && Enum.IsDefined((EquipmentCommandAuditResult)integer))
        {
            commandOutcome = integer;
            return true;
        }

        return false;
    }

    private static bool TryParseSecurityOutcome(string? value, out int? securityOutcome)
    {
        securityOutcome = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (Enum.TryParse<SecurityAuditResult>(value.Trim(), ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed))
        {
            securityOutcome = (int)parsed;
            return true;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)
            && Enum.IsDefined((SecurityAuditResult)integer))
        {
            securityOutcome = integer;
            return true;
        }

        return false;
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1
            FROM sqlite_master
            WHERE type = 'table' AND name = @tableName
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@tableName", tableName);

        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    private static ModbusStatusArchiveRecord CreateModbusStatusRecord(SqliteDataReader reader)
    {
        var detailsJson = reader.GetNullableString(3);
        if (string.IsNullOrWhiteSpace(detailsJson))
        {
            throw new ArchiveQueryMappingException("Modbus status details JSON is missing.");
        }

        using var document = JsonDocument.Parse(detailsJson);
        var root = document.RootElement;

        return new ModbusStatusArchiveRecord(
            reader.GetGuidFromString(0),
            reader.GetNullableString(2) ?? "unknown-device",
            reader.GetUtcFromUnixMilliseconds(1),
            ReadEnum<ModbusConnectionState>(root, "clientState"),
            ReadEnum<ModbusConnectionState>(root, "serverState"),
            ReadString(root, "clientMessage"),
            ReadString(root, "serverMessage"),
            ReadNullableString(root, "lastError"),
            schemaVersion: 1);
    }

    private static T ReadEnum<T>(JsonElement root, string propertyName)
        where T : struct, Enum
    {
        var value = ReadString(root, propertyName);
        if (!Enum.TryParse<T>(value, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
        {
            throw new ArchiveQueryMappingException(
                $"Modbus status details property '{propertyName}' has an unsupported value.");
        }

        return parsed;
    }

    private static string ReadString(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string? ReadNullableString(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private sealed class ArchiveQueryMappingException(string message) : Exception(message);
}
