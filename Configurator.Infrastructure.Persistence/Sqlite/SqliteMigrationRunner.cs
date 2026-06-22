using Configurator.Application.Services.Archiving;
using Configurator.Infrastructure.Persistence.Common;
using Microsoft.Data.Sqlite;

namespace Configurator.Infrastructure.Persistence.Sqlite;

public sealed class SqliteMigrationRunner
{
    private const string PersistenceConnectionFailed = nameof(PersistenceConnectionFailed);
    private const string PersistenceMigrationChecksumMismatch = nameof(PersistenceMigrationChecksumMismatch);
    private const string PersistenceMigrationFailed = nameof(PersistenceMigrationFailed);
    private const string PersistenceMetadataMismatch = nameof(PersistenceMetadataMismatch);

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SqlitePragmaInitializer _pragmaInitializer;
    private readonly IReadOnlyList<SqliteMigration> _migrations;

    public SqliteMigrationRunner(
        SqliteConnectionFactory connectionFactory,
        SqlitePragmaInitializer pragmaInitializer,
        SqliteMigrationCatalog migrationCatalog)
        : this(connectionFactory, pragmaInitializer, GetMigrations(migrationCatalog))
    {
    }

    public SqliteMigrationRunner(
        SqliteConnectionFactory connectionFactory,
        SqlitePragmaInitializer pragmaInitializer,
        IReadOnlyList<SqliteMigration> migrations)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _pragmaInitializer = pragmaInitializer ?? throw new ArgumentNullException(nameof(pragmaInitializer));
        _migrations = (migrations ?? throw new ArgumentNullException(nameof(migrations)))
            .OrderBy(migration => migration.Version)
            .ToArray();
    }

    public async Task<ArchiveOperationResult> InitializeAsync(
        ArchiveDatabaseInitializationOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var connectionResult = await _connectionFactory.OpenAsync(options, cancellationToken).ConfigureAwait(false);
        if (!connectionResult.Succeeded || connectionResult.Value is null)
        {
            return ArchiveOperationResult.Failure(
                connectionResult.ErrorCode ?? PersistenceConnectionFailed,
                connectionResult.ErrorMessage ?? "Archive database connection failed.",
                connectionResult.ErrorDetails);
        }

        await using var connection = connectionResult.Value;

        var pragmaResult = await _pragmaInitializer
            .ApplyAsync(connection, options.BusyTimeoutMs, cancellationToken)
            .ConfigureAwait(false);
        if (!pragmaResult.Succeeded)
        {
            return pragmaResult;
        }

        var ledgerResult = await ExecuteLedgerBootstrapAsync(connection, cancellationToken).ConfigureAwait(false);
        if (!ledgerResult.Succeeded)
        {
            return ledgerResult;
        }

        foreach (var migration in _migrations)
        {
            var migrationResult = await ApplyMigrationAsync(connection, migration, cancellationToken).ConfigureAwait(false);
            if (!migrationResult.Succeeded)
            {
                return migrationResult;
            }
        }

        return await EnsureMetadataAsync(connection, options, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ArchiveOperationResult> ExecuteLedgerBootstrapAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS schema_migration (
                version INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                applied_at_utc_ms INTEGER NOT NULL,
                checksum TEXT NOT NULL
            );
            """;

        try
        {
            await ExecuteNonQueryAsync(connection, sql, cancellationToken).ConfigureAwait(false);

            return ArchiveOperationResult.Success();
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return ArchiveOperationResult.Failure(
                PersistenceMigrationFailed,
                "SQLite migration ledger initialization failed.",
                ex.Message);
        }
    }

    private static IReadOnlyList<SqliteMigration> GetMigrations(SqliteMigrationCatalog migrationCatalog)
    {
        ArgumentNullException.ThrowIfNull(migrationCatalog);

        return migrationCatalog.All;
    }

    private static async Task<ArchiveOperationResult> ApplyMigrationAsync(
        SqliteConnection connection,
        SqliteMigration migration,
        CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteNonQueryAsync(connection, "BEGIN IMMEDIATE;", cancellationToken).ConfigureAwait(false);

            var existingChecksum = await GetExistingChecksumAsync(
                connection,
                migration.Version,
                cancellationToken).ConfigureAwait(false);

            if (existingChecksum is not null)
            {
                if (!string.Equals(existingChecksum, migration.Checksum, StringComparison.Ordinal))
                {
                    await RollbackAsync(connection).ConfigureAwait(false);

                    return ArchiveOperationResult.Failure(
                        PersistenceMigrationChecksumMismatch,
                        "SQLite migration checksum mismatch.",
                        $"Version {migration.Version} '{migration.Name}'.");
                }

                await ExecuteNonQueryAsync(connection, "COMMIT;", cancellationToken).ConfigureAwait(false);

                return ArchiveOperationResult.Success();
            }

            await ExecuteNonQueryAsync(connection, migration.Sql, cancellationToken).ConfigureAwait(false);
            await InsertLedgerRowAsync(connection, migration, cancellationToken).ConfigureAwait(false);
            await ExecuteNonQueryAsync(connection, "COMMIT;", cancellationToken).ConfigureAwait(false);

            return ArchiveOperationResult.Success();
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            await RollbackAsync(connection).ConfigureAwait(false);

            return ArchiveOperationResult.Failure(
                PersistenceMigrationFailed,
                "SQLite migration failed.",
                ex.Message);
        }
    }

    private static async Task<ArchiveOperationResult> EnsureMetadataAsync(
        SqliteConnection connection,
        ArchiveDatabaseInitializationOptions options,
        CancellationToken cancellationToken)
    {
        const string insertSql = """
            INSERT INTO archive_partition_metadata
            (id, archive_schema_version, created_at_utc_ms, device_id, application_version)
            VALUES (1, @schemaVersion, @createdAtUtcMs, @deviceId, @applicationVersion)
            ON CONFLICT(id) DO NOTHING;
            """;

        try
        {
            await using (var insert = connection.CreateCommand())
            {
                insert.CommandText = insertSql;
                insert.Parameters.AddWithValue("@schemaVersion", options.ArchiveSchemaVersion);
                insert.Parameters.AddWithValue("@createdAtUtcMs", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                insert.Parameters.AddWithValue("@deviceId", options.DeviceId);
                insert.Parameters.AddWithValue("@applicationVersion", options.ApplicationVersion);
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var select = connection.CreateCommand();
            select.CommandText = """
                SELECT archive_schema_version, device_id
                FROM archive_partition_metadata
                WHERE id = 1;
                """;

            await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return ArchiveOperationResult.Failure(
                    PersistenceMetadataMismatch,
                    "Archive metadata row was not created.");
            }

            var schemaVersion = reader.GetInt32(0);
            var deviceId = reader.GetString(1);

            if (schemaVersion != options.ArchiveSchemaVersion
                || !string.Equals(deviceId, options.DeviceId, StringComparison.Ordinal))
            {
                return ArchiveOperationResult.Failure(
                    PersistenceMetadataMismatch,
                    "Archive metadata does not match initialization options.");
            }

            return ArchiveOperationResult.Success();
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return ArchiveOperationResult.Failure(
                PersistenceMetadataMismatch,
                "Archive metadata verification failed.",
                ex.Message);
        }
    }

    private static async Task<string?> GetExistingChecksumAsync(
        SqliteConnection connection,
        int version,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT checksum FROM schema_migration WHERE version = @version;";
        command.Parameters.AddWithValue("@version", version);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        return value is null or DBNull ? null : Convert.ToString(value);
    }

    private static async Task InsertLedgerRowAsync(
        SqliteConnection connection,
        SqliteMigration migration,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO schema_migration (version, name, applied_at_utc_ms, checksum)
            VALUES (@version, @name, @appliedAtUtcMs, @checksum);
            """;
        command.Parameters.AddWithValue("@version", migration.Version);
        command.Parameters.AddWithValue("@name", migration.Name);
        command.Parameters.AddWithValue("@appliedAtUtcMs", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("@checksum", migration.Checksum);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task RollbackAsync(SqliteConnection connection)
    {
        try
        {
            await ExecuteNonQueryAsync(connection, "ROLLBACK;", CancellationToken.None).ConfigureAwait(false);
        }
        catch (SqliteException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }
}
