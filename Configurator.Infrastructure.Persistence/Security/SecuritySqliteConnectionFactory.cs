using Microsoft.Data.Sqlite;

namespace Configurator.Infrastructure.Persistence.Security;

public sealed class SecuritySqliteConnectionFactory
{
    private readonly SecurityDatabasePathProvider _pathProvider;

    public SecuritySqliteConnectionFactory(SecurityDatabasePathProvider pathProvider)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var databasePath = ResolveDatabasePath();
        var directory = Path.GetDirectoryName(databasePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Security database directory is invalid.");
        }

        Directory.CreateDirectory(directory);

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

            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private string ResolveDatabasePath()
    {
        var databasePath = Path.GetFullPath(_pathProvider.GetDatabasePath());
        if (string.Equals(databasePath.Trim(), ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Security database must be file-backed.");
        }

        if (IsUnderInstallationDirectory(databasePath))
        {
            throw new InvalidOperationException("Security database path must not be under the application installation directory.");
        }

        return databasePath;
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
