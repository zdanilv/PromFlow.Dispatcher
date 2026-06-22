using Configurator.Application.Services.Archiving;
using Configurator.Infrastructure.Persistence.Common;
using Microsoft.Data.Sqlite;

namespace Configurator.Infrastructure.Persistence.Sqlite;

public sealed class SqliteConnectionFactory
{
    private const string PersistencePathInvalid = nameof(PersistencePathInvalid);
    private const string PersistenceDirectoryInvalid = nameof(PersistenceDirectoryInvalid);
    private const string PersistenceConnectionFailed = nameof(PersistenceConnectionFailed);

    public async Task<ArchiveOperationResult<SqliteConnection>> OpenAsync(
        ArchiveDatabaseInitializationOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.DatabasePath))
        {
            return ArchiveOperationResult<SqliteConnection>.Failure(
                PersistencePathInvalid,
                "Archive database path is required.");
        }

        if (string.Equals(options.DatabasePath.Trim(), ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return ArchiveOperationResult<SqliteConnection>.Failure(
                PersistencePathInvalid,
                "Archive database must be file-backed.");
        }

        string databasePath;
        try
        {
            databasePath = Path.GetFullPath(options.DatabasePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ArchiveOperationResult<SqliteConnection>.Failure(
                PersistencePathInvalid,
                "Archive database path is invalid.",
                ex.Message);
        }

        if (IsUnderInstallationDirectory(databasePath))
        {
            return ArchiveOperationResult<SqliteConnection>.Failure(
                PersistencePathInvalid,
                "Archive database path must not be under the application installation directory.");
        }

        var directory = Path.GetDirectoryName(databasePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return ArchiveOperationResult<SqliteConnection>.Failure(
                PersistenceDirectoryInvalid,
                "Archive database directory is invalid.");
        }

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ArchiveOperationResult<SqliteConnection>.Failure(
                PersistenceDirectoryInvalid,
                "Archive database directory cannot be created.",
                ex.Message);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();

        var connection = new SqliteConnection(connectionString);

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            return ArchiveOperationResult<SqliteConnection>.Success(connection);
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            await connection.DisposeAsync().ConfigureAwait(false);

            return ArchiveOperationResult<SqliteConnection>.Failure(
                PersistenceConnectionFailed,
                "Archive database connection failed.",
                ex.Message);
        }
    }

    private static bool IsUnderInstallationDirectory(string databasePath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var installationDirectory = Path.GetFullPath(AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedPath = Path.GetFullPath(databasePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var installationPrefix = installationDirectory + Path.DirectorySeparatorChar;

        return normalizedPath.Equals(installationDirectory, comparison)
            || normalizedPath.StartsWith(installationPrefix, comparison);
    }
}
