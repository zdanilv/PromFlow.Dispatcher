using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Authorization;
using Configurator.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Configurator.Infrastructure.Persistence.Security;

public sealed class SecuritySqliteMigrationRunner
{
    private readonly SecuritySqliteConnectionFactory _connectionFactory;
    private readonly SqlitePragmaInitializer _pragmaInitializer;
    private readonly IOptions<AuthenticationOptions> _options;
    private readonly IReadOnlyList<SqliteMigration> _migrations;

    public SecuritySqliteMigrationRunner(
        SecuritySqliteConnectionFactory connectionFactory,
        SqlitePragmaInitializer pragmaInitializer,
        SecuritySqliteMigrationCatalog migrationCatalog,
        IOptions<AuthenticationOptions> options)
        : this(connectionFactory, pragmaInitializer, migrationCatalog.All, options)
    {
    }

    public SecuritySqliteMigrationRunner(
        SecuritySqliteConnectionFactory connectionFactory,
        SqlitePragmaInitializer pragmaInitializer,
        IReadOnlyList<SqliteMigration> migrations,
        IOptions<AuthenticationOptions> options)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _pragmaInitializer = pragmaInitializer ?? throw new ArgumentNullException(nameof(pragmaInitializer));
        _migrations = (migrations ?? throw new ArgumentNullException(nameof(migrations)))
            .OrderBy(migration => migration.Version)
            .ToArray();
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<ArchiveOperationResult> InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            var pragmaResult = await _pragmaInitializer
                .ApplyAsync(connection, GetBusyTimeoutMs(), cancellationToken)
                .ConfigureAwait(false);
            if (!pragmaResult.Succeeded)
            {
                return pragmaResult;
            }

            await ExecuteNonQueryAsync(connection, """
                CREATE TABLE IF NOT EXISTS security_schema_migration (
                    version INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    applied_at_utc_ms INTEGER NOT NULL,
                    checksum TEXT NOT NULL
                );
                """, cancellationToken).ConfigureAwait(false);

            foreach (var migration in _migrations)
            {
                var result = await ApplyMigrationAsync(connection, migration, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    return result;
                }
            }

            return ArchiveOperationResult.Success();
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ArchiveOperationResult.Failure(
                SecurityErrorCodes.SecurityMigrationFailed,
                "Security database initialization failed.",
                ex.Message);
        }
    }

    private async Task<ArchiveOperationResult> ApplyMigrationAsync(
        SqliteConnection connection,
        SqliteMigration migration,
        CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteNonQueryAsync(connection, "BEGIN IMMEDIATE;", cancellationToken).ConfigureAwait(false);

            var existingChecksum = await GetExistingChecksumAsync(connection, migration.Version, cancellationToken)
                .ConfigureAwait(false);
            if (existingChecksum is not null)
            {
                if (!string.Equals(existingChecksum, migration.Checksum, StringComparison.Ordinal))
                {
                    await RollbackAsync(connection).ConfigureAwait(false);

                    return ArchiveOperationResult.Failure(
                        SecurityErrorCodes.SecurityMigrationChecksumMismatch,
                        "Security migration checksum mismatch.",
                        $"Version {migration.Version} '{migration.Name}'.");
                }

                await ExecuteNonQueryAsync(connection, "COMMIT;", cancellationToken).ConfigureAwait(false);

                return ArchiveOperationResult.Success();
            }

            await ExecuteNonQueryAsync(connection, migration.Sql, cancellationToken).ConfigureAwait(false);
            await using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO security_schema_migration (version, name, applied_at_utc_ms, checksum)
                VALUES (@version, @name, @appliedAtUtcMs, @checksum);
                """;
            insert.Parameters.AddWithValue("@version", migration.Version);
            insert.Parameters.AddWithValue("@name", migration.Name);
            insert.Parameters.AddWithValue("@appliedAtUtcMs", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            insert.Parameters.AddWithValue("@checksum", migration.Checksum);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteNonQueryAsync(connection, "COMMIT;", cancellationToken).ConfigureAwait(false);

            return ArchiveOperationResult.Success();
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            await RollbackAsync(connection).ConfigureAwait(false);

            return ArchiveOperationResult.Failure(
                SecurityErrorCodes.SecurityMigrationFailed,
                "Security migration failed.",
                ex.Message);
        }
    }

    private int GetBusyTimeoutMs()
        => Math.Max(1000, _options.Value.LockoutMinutes * 100);

    private static async Task<string?> GetExistingChecksumAsync(
        SqliteConnection connection,
        int version,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT checksum FROM security_schema_migration WHERE version = @version;";
        command.Parameters.AddWithValue("@version", version);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        return value is null or DBNull ? null : Convert.ToString(value);
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
