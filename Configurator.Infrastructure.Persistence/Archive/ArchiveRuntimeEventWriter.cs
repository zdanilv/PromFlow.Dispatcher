using Configurator.Application.Services.Archiving;
using Configurator.Infrastructure.Persistence.Common;
using Configurator.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace Configurator.Infrastructure.Persistence.Archive;

public sealed class ArchiveRuntimeEventWriter
{
    private readonly ArchivePartitionCatalog _partitionCatalog;
    private readonly SqliteMigrationRunner _migrationRunner;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SqlitePragmaInitializer _pragmaInitializer;

    public ArchiveRuntimeEventWriter(
        ArchivePartitionCatalog partitionCatalog,
        SqliteMigrationRunner migrationRunner,
        SqliteConnectionFactory connectionFactory,
        SqlitePragmaInitializer pragmaInitializer)
    {
        _partitionCatalog = partitionCatalog ?? throw new ArgumentNullException(nameof(partitionCatalog));
        _migrationRunner = migrationRunner ?? throw new ArgumentNullException(nameof(migrationRunner));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _pragmaInitializer = pragmaInitializer ?? throw new ArgumentNullException(nameof(pragmaInitializer));
    }

    public async Task<ArchiveOperationResult> WriteAsync(
        ArchiveOptions options,
        ArchiveRuntimeEventRecord record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(record);

        var partition = _partitionCatalog.GetCurrentWritablePartition(options, record.OccurredAtUtc);
        var initializationOptions = new ArchiveDatabaseInitializationOptions
        {
            DatabasePath = partition.DatabasePath,
            DeviceId = options.DeviceId.Trim(),
            ApplicationVersion = GetApplicationVersion(),
            ArchiveSchemaVersion = 1,
            BusyTimeoutMs = options.BusyTimeoutMs
        };

        var initialize = await _migrationRunner
            .InitializeAsync(initializationOptions, cancellationToken)
            .ConfigureAwait(false);
        if (!initialize.Succeeded)
        {
            return ArchiveOperationResult.Failure(
                initialize.ErrorCode ?? ArchivePersistenceErrorCodes.ArchiveWriteFailed,
                initialize.ErrorMessage ?? "Archive runtime event partition initialization failed.",
                initialize.ErrorDetails);
        }

        var connectionOpen = await _connectionFactory
            .OpenAsync(initializationOptions, cancellationToken)
            .ConfigureAwait(false);
        if (!connectionOpen.Succeeded || connectionOpen.Value is null)
        {
            return ArchiveOperationResult.Failure(
                connectionOpen.ErrorCode ?? ArchivePersistenceErrorCodes.ArchiveWriteFailed,
                connectionOpen.ErrorMessage ?? "Archive runtime event connection failed.",
                connectionOpen.ErrorDetails);
        }

        await using var connection = connectionOpen.Value;
        var pragma = await _pragmaInitializer
            .ApplyAsync(connection, options.BusyTimeoutMs, cancellationToken)
            .ConfigureAwait(false);
        if (!pragma.Succeeded)
        {
            return ArchiveOperationResult.Failure(
                pragma.ErrorCode ?? ArchivePersistenceErrorCodes.ArchiveWriteFailed,
                pragma.ErrorMessage ?? "Archive runtime event PRAGMA failed.",
                pragma.ErrorDetails);
        }

        try
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO runtime_event
                (id, occurred_at_utc_ms, device_id, event_type, severity, message, details_json)
                VALUES
                (@id, @occurredAtUtcMs, @deviceId, @eventType, @severity, @message, @detailsJson);
                """;
            insert.Parameters.AddWithValue("@id", record.Id.ToString("D"));
            insert.Parameters.AddWithValue("@occurredAtUtcMs", record.OccurredAtUtc.ToUnixTimeMilliseconds());
            insert.Parameters.AddWithValue("@deviceId", DbValue(record.DeviceId));
            insert.Parameters.AddWithValue("@eventType", record.EventType);
            insert.Parameters.AddWithValue("@severity", record.Severity);
            insert.Parameters.AddWithValue("@message", record.Message);
            insert.Parameters.AddWithValue("@detailsJson", DbValue(record.DetailsJson));

            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return ArchiveOperationResult.Success();
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return ArchiveOperationResult.Failure(
                ArchivePersistenceErrorCodes.ArchiveWriteFailed,
                "Archive runtime event write failed.",
                ex.Message);
        }
    }

    private static string GetApplicationVersion()
        => typeof(ArchiveRuntimeEventWriter).Assembly.GetName().Version?.ToString() ?? "unknown";

    private static object DbValue<T>(T? value)
        => value is null ? DBNull.Value : value;
}
