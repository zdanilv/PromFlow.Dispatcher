using Configurator.Infrastructure.Persistence.Common;
using Microsoft.Data.Sqlite;

namespace Configurator.Infrastructure.Persistence.Tests.TestSupport;

public sealed class TempArchiveDatabase : IDisposable
{
    public TempArchiveDatabase()
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), "PromFlow.PersistenceTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        DatabasePath = Path.Combine(DirectoryPath, "archive.sqlite");
    }

    public string DirectoryPath { get; }

    public string DatabasePath { get; }

    public ArchiveDatabaseInitializationOptions CreateOptions(
        string deviceId = "device-1",
        string applicationVersion = "test-1.0.0",
        int archiveSchemaVersion = 1,
        int busyTimeoutMs = 5000)
        => new()
        {
            DatabasePath = DatabasePath,
            DeviceId = deviceId,
            ApplicationVersion = applicationVersion,
            ArchiveSchemaVersion = archiveSchemaVersion,
            BusyTimeoutMs = busyTimeoutMs
        };

    public SqliteConnection OpenConnection()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        connection.Open();

        return connection;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
