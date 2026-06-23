using Configurator.Application.Services.Archiving;
using Configurator.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.Json;

namespace Configurator.Infrastructure.Persistence.Archive;

public sealed class ArchiveMaintenanceService : IArchiveMaintenanceService
{
    private readonly ArchivePartitionCatalog _partitionCatalog;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SqlitePragmaInitializer _pragmaInitializer;
    private readonly ArchiveRuntimeEventWriter _runtimeEventWriter;
    private readonly ArchiveExportPackageWriter _exportPackageWriter;
    private readonly ArchiveBackupPackageWriter _backupPackageWriter;
    private readonly ArchiveOptionsValidator _optionsValidator;
    private readonly IOptions<ArchiveOptions> _options;

    public ArchiveMaintenanceService(
        ArchivePartitionCatalog partitionCatalog,
        SqliteConnectionFactory connectionFactory,
        SqlitePragmaInitializer pragmaInitializer,
        ArchiveRuntimeEventWriter runtimeEventWriter,
        ArchiveExportPackageWriter exportPackageWriter,
        ArchiveBackupPackageWriter backupPackageWriter,
        ArchiveOptionsValidator optionsValidator,
        IOptions<ArchiveOptions> options)
    {
        _partitionCatalog = partitionCatalog ?? throw new ArgumentNullException(nameof(partitionCatalog));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _pragmaInitializer = pragmaInitializer ?? throw new ArgumentNullException(nameof(pragmaInitializer));
        _runtimeEventWriter = runtimeEventWriter ?? throw new ArgumentNullException(nameof(runtimeEventWriter));
        _exportPackageWriter = exportPackageWriter ?? throw new ArgumentNullException(nameof(exportPackageWriter));
        _backupPackageWriter = backupPackageWriter ?? throw new ArgumentNullException(nameof(backupPackageWriter));
        _optionsValidator = optionsValidator ?? throw new ArgumentNullException(nameof(optionsValidator));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<ArchiveOperationResult<ArchiveRetentionResult>> ApplyRetentionAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = _options.Value.Clone();
        var validation = _optionsValidator.Validate(options);
        if (!validation.Succeeded)
        {
            var details = string.Join(
                "; ",
                validation.Errors.Select(error => $"{error.Code}:{error.PropertyName}"));
            return ArchiveOperationResult<ArchiveRetentionResult>.Failure(
                ArchivePersistenceErrorCodes.ArchiveOptionsInvalid,
                "Archive options are invalid.",
                details);
        }

        var appliedAtUtc = nowUtc.ToUniversalTime();
        var highResolutionCutoff = appliedAtUtc.AddHours(-options.HighResolutionRetentionHours).ToUnixTimeMilliseconds();
        var longTermCutoff = appliedAtUtc.AddDays(-options.LongTermRetentionDays).ToUnixTimeMilliseconds();
        var commandCutoff = appliedAtUtc.AddDays(-options.CommandAuditRetentionDays).ToUnixTimeMilliseconds();
        var securityCutoff = appliedAtUtc.AddDays(-options.SecurityAuditRetentionDays).ToUnixTimeMilliseconds();
        var activePartition = _partitionCatalog.GetCurrentWritablePartition(options, appliedAtUtc);
        var partitions = _partitionCatalog.GetExistingPartitions(options);
        var results = new List<ArchiveRetentionPartitionResult>(partitions.Count);

        try
        {
            foreach (var partition in partitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var outcome = await ApplyPartitionRetentionAsync(
                    partition,
                    options,
                    highResolutionCutoff,
                    longTermCutoff,
                    commandCutoff,
                    securityCutoff,
                    IsSamePartition(partition, activePartition),
                    cancellationToken).ConfigureAwait(false);
                if (!outcome.Succeeded || outcome.Value is null)
                {
                    return ArchiveOperationResult<ArchiveRetentionResult>.Failure(
                        outcome.ErrorCode ?? ArchivePersistenceErrorCodes.ArchiveRetentionFailed,
                        outcome.ErrorMessage ?? "Archive retention failed.",
                        outcome.ErrorDetails);
                }

                results.Add(outcome.Value);
            }

            var retention = new ArchiveRetentionResult(appliedAtUtc, results);
            var audit = new ArchiveRuntimeEventRecord(
                Guid.NewGuid(),
                appliedAtUtc,
                options.DeviceId,
                "ArchiveRetention",
                severity: 1,
                "Archive retention applied.",
                JsonSerializer.Serialize(new
                {
                    retention.DeletedHighResolutionSnapshotRows,
                    retention.DeletedLongTermSnapshotRows,
                    retention.DeletedRuntimeEventRows,
                    retention.DeletedCommandRows,
                    retention.DeletedPhysicalWriteRows,
                    retention.DeletedSecurityAuditRows,
                    retention.DeletedDatabaseFiles
                }));
            var auditWrite = await _runtimeEventWriter.WriteAsync(options, audit, cancellationToken).ConfigureAwait(false);
            if (!auditWrite.Succeeded)
            {
                return ArchiveOperationResult<ArchiveRetentionResult>.Failure(
                    auditWrite.ErrorCode ?? ArchivePersistenceErrorCodes.ArchiveRetentionFailed,
                    auditWrite.ErrorMessage ?? "Archive retention audit event could not be written.",
                    auditWrite.ErrorDetails);
            }

            return ArchiveOperationResult<ArchiveRetentionResult>.Success(retention);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return ArchiveOperationResult<ArchiveRetentionResult>.Failure(
                ArchivePersistenceErrorCodes.ArchiveRetentionFailed,
                "Archive retention failed.",
                ex.Message);
        }
    }

    public async Task<ArchiveOperationResult<ArchiveExportResult>> ExportAsync(
        ArchiveExportRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = _options.Value.Clone();
        var validation = _optionsValidator.Validate(options);
        if (!validation.Succeeded)
        {
            return ArchiveOperationResult<ArchiveExportResult>.Failure(
                ArchivePersistenceErrorCodes.ArchiveOptionsInvalid,
                "Archive options are invalid.");
        }

        return await _exportPackageWriter.ExportAsync(request, options, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ArchiveOperationResult<ArchiveBackupResult>> CreateBackupAsync(
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = _options.Value.Clone();
        var validation = _optionsValidator.Validate(options);
        if (!validation.Succeeded)
        {
            return ArchiveOperationResult<ArchiveBackupResult>.Failure(
                ArchivePersistenceErrorCodes.ArchiveOptionsInvalid,
                "Archive options are invalid.");
        }

        return await _backupPackageWriter.CreateBackupAsync(destinationDirectory, options, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ArchiveOperationResult<ArchiveRetentionPartitionResult>> ApplyPartitionRetentionAsync(
        ArchivePartitionInfo partition,
        ArchiveOptions options,
        long highResolutionCutoff,
        long longTermCutoff,
        long commandCutoff,
        long securityCutoff,
        bool isActivePartition,
        CancellationToken cancellationToken)
    {
        var connectionOpen = await _connectionFactory
            .OpenReadWriteExistingAsync(partition.DatabasePath, cancellationToken)
            .ConfigureAwait(false);
        if (!connectionOpen.Succeeded || connectionOpen.Value is null)
        {
            if (connectionOpen.ErrorCode == ArchivePersistenceErrorCodes.ArchivePartitionMissing)
            {
                return ArchiveOperationResult<ArchiveRetentionPartitionResult>.Success(new ArchiveRetentionPartitionResult(
                    partition.DatabasePath,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    databaseFileDeleted: false));
            }

            return ArchiveOperationResult<ArchiveRetentionPartitionResult>.Failure(
                connectionOpen.ErrorCode ?? ArchivePersistenceErrorCodes.ArchiveRetentionFailed,
                connectionOpen.ErrorMessage ?? "Archive partition could not be opened for retention.",
                connectionOpen.ErrorDetails ?? partition.DatabasePath);
        }

        var deletedHighResolutionSnapshots = 0L;
        var deletedLongTermSnapshots = 0L;
        var deletedRuntimeEvents = 0L;
        var deletedPhysicalWrites = 0L;
        var deletedCommands = 0L;
        var deletedSecurityAudits = 0L;
        var shouldDeleteDatabase = false;

        await using (var connection = connectionOpen.Value)
        {
            var pragma = await _pragmaInitializer.ApplyAsync(connection, options.BusyTimeoutMs, cancellationToken).ConfigureAwait(false);
            if (!pragma.Succeeded)
            {
                return ArchiveOperationResult<ArchiveRetentionPartitionResult>.Failure(
                    pragma.ErrorCode ?? ArchivePersistenceErrorCodes.ArchiveRetentionFailed,
                    pragma.ErrorMessage ?? "Archive retention PRAGMA failed.",
                    pragma.ErrorDetails ?? partition.DatabasePath);
            }

            if (await TableExistsAsync(connection, "modbus_snapshot", cancellationToken).ConfigureAwait(false))
            {
                deletedHighResolutionSnapshots = await ExecuteDeleteAsync(
                    connection,
                    """
                    DELETE FROM modbus_snapshot
                    WHERE resolution_class = @resolutionClass
                      AND captured_at_utc_ms < @cutoff;
                    """,
                    ("@resolutionClass", (int)ArchiveResolution.HighResolution),
                    ("@cutoff", highResolutionCutoff),
                    cancellationToken).ConfigureAwait(false);
                deletedLongTermSnapshots = await ExecuteDeleteAsync(
                    connection,
                    """
                    DELETE FROM modbus_snapshot
                    WHERE resolution_class = @resolutionClass
                      AND captured_at_utc_ms < @cutoff;
                    """,
                    ("@resolutionClass", (int)ArchiveResolution.LongTerm),
                    ("@cutoff", longTermCutoff),
                    cancellationToken).ConfigureAwait(false);
            }

            if (await TableExistsAsync(connection, "runtime_event", cancellationToken).ConfigureAwait(false))
            {
                deletedRuntimeEvents = await ExecuteDeleteAsync(
                    connection,
                    """
                    DELETE FROM runtime_event
                    WHERE occurred_at_utc_ms < @cutoff;
                    """,
                    ("@cutoff", longTermCutoff),
                    cancellationToken).ConfigureAwait(false);
            }

            if (await TableExistsAsync(connection, "modbus_write", cancellationToken).ConfigureAwait(false))
            {
                deletedPhysicalWrites = await ExecuteDeleteAsync(
                    connection,
                    """
                    DELETE FROM modbus_write
                    WHERE attempted_at_utc_ms < @cutoff;
                    """,
                    ("@cutoff", commandCutoff),
                    cancellationToken).ConfigureAwait(false);
            }

            if (await TableExistsAsync(connection, "equipment_command", cancellationToken).ConfigureAwait(false))
            {
                deletedCommands = await ExecuteDeleteAsync(
                    connection,
                    """
                    DELETE FROM equipment_command
                    WHERE requested_at_utc_ms < @cutoff;
                    """,
                    ("@cutoff", commandCutoff),
                    cancellationToken).ConfigureAwait(false);
            }

            if (await TableExistsAsync(connection, "security_audit", cancellationToken).ConfigureAwait(false))
            {
                deletedSecurityAudits = await ExecuteDeleteAsync(
                    connection,
                    """
                    DELETE FROM security_audit
                    WHERE occurred_at_utc_ms < @cutoff;
                    """,
                    ("@cutoff", securityCutoff),
                    cancellationToken).ConfigureAwait(false);
            }

            shouldDeleteDatabase = !isActivePartition
                && await IsPartitionEmptyAsync(connection, cancellationToken).ConfigureAwait(false);
            if (shouldDeleteDatabase)
            {
                await ExecuteNonQueryAsync(connection, "PRAGMA wal_checkpoint(TRUNCATE);", cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var databaseFileDeleted = false;
        if (shouldDeleteDatabase)
        {
            DeletePartitionFiles(partition.DatabasePath);
            databaseFileDeleted = true;
        }

        return ArchiveOperationResult<ArchiveRetentionPartitionResult>.Success(new ArchiveRetentionPartitionResult(
            partition.DatabasePath,
            deletedHighResolutionSnapshots,
            deletedLongTermSnapshots,
            deletedRuntimeEvents,
            deletedCommands,
            deletedPhysicalWrites,
            deletedSecurityAudits,
            databaseFileDeleted));
    }

    private static async Task<bool> IsPartitionEmptyAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        foreach (var tableName in new[] { "modbus_snapshot", "runtime_event", "equipment_command", "modbus_write", "security_audit" })
        {
            if (!await TableExistsAsync(connection, tableName, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
            var count = Convert.ToInt64(
                await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture);
            if (count > 0)
            {
                return false;
            }
        }

        return true;
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

    private static async Task<long> ExecuteDeleteAsync(
        SqliteConnection connection,
        string sql,
        (string Name, object Value) firstParameter,
        (string Name, object Value) secondParameter,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue(firstParameter.Name, firstParameter.Value);
        command.Parameters.AddWithValue(secondParameter.Name, secondParameter.Value);

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<long> ExecuteDeleteAsync(
        SqliteConnection connection,
        string sql,
        (string Name, object Value) parameter,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue(parameter.Name, parameter.Value);

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool IsSamePartition(ArchivePartitionInfo first, ArchivePartitionInfo second)
        => first.Year == second.Year
            && first.Month == second.Month
            && string.Equals(first.SanitizedDeviceId, second.SanitizedDeviceId, StringComparison.Ordinal);

    private static void DeletePartitionFiles(string databasePath)
    {
        SqliteConnection.ClearAllPools();
        TryDeleteFile(ArchivePartitionCatalog.GetWalPath(databasePath));
        TryDeleteFile(ArchivePartitionCatalog.GetSharedMemoryPath(databasePath));
        TryDeleteFile(databasePath);
    }

    private static void TryDeleteFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        File.Delete(path);
    }
}
