using Configurator.Application.Services.Archiving;
using Microsoft.Data.Sqlite;

namespace Configurator.Infrastructure.Persistence.Sqlite;

public sealed class SqlitePragmaInitializer
{
    private const string PersistencePragmaFailed = nameof(PersistencePragmaFailed);

    public async Task<ArchiveOperationResult> ApplyAsync(
        SqliteConnection connection,
        int busyTimeoutMs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (busyTimeoutMs <= 0)
        {
            return ArchiveOperationResult.Failure(
                PersistencePragmaFailed,
                "SQLite busy timeout must be positive.");
        }

        try
        {
            await ExecuteNonQueryAsync(connection, "PRAGMA foreign_keys = ON;", cancellationToken).ConfigureAwait(false);
            await ExecuteNonQueryAsync(connection, $"PRAGMA busy_timeout = {busyTimeoutMs};", cancellationToken).ConfigureAwait(false);

            var journalMode = await ExecuteScalarAsync<string>(
                connection,
                "PRAGMA journal_mode = WAL;",
                cancellationToken).ConfigureAwait(false);

            if (!string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase))
            {
                return ArchiveOperationResult.Failure(
                    PersistencePragmaFailed,
                    "SQLite WAL mode was not enabled.",
                    journalMode);
            }

            await ExecuteNonQueryAsync(connection, "PRAGMA synchronous = FULL;", cancellationToken).ConfigureAwait(false);

            return ArchiveOperationResult.Success();
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return ArchiveOperationResult.Failure(
                PersistencePragmaFailed,
                "SQLite PRAGMA initialization failed.",
                ex.Message);
        }
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

    private static async Task<T?> ExecuteScalarAsync<T>(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        return value is null or DBNull ? default : (T)Convert.ChangeType(value, typeof(T));
    }
}
