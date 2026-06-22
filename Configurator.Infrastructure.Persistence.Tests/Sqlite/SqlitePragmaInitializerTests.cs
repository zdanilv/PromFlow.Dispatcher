using Configurator.Infrastructure.Persistence.Common;
using Configurator.Infrastructure.Persistence.Sqlite;
using Configurator.Infrastructure.Persistence.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Configurator.Infrastructure.Persistence.Tests.Sqlite;

public sealed class SqlitePragmaInitializerTests
{
    [Fact]
    public async Task Pragmas_FileBackedDatabase_EnableWalForeignKeysFullSyncAndBusyTimeout()
    {
        using var database = new TempArchiveDatabase();
        var factory = new SqliteConnectionFactory();
        var connectionResult = await factory.OpenAsync(database.CreateOptions(busyTimeoutMs: 4321), CancellationToken.None);
        Assert.True(connectionResult.Succeeded, FormatFailure(connectionResult.ErrorCode, connectionResult.ErrorMessage, connectionResult.ErrorDetails));
        await using var connection = connectionResult.Value!;
        var initializer = new SqlitePragmaInitializer();

        var result = await initializer.ApplyAsync(connection, 4321, CancellationToken.None);

        Assert.True(result.Succeeded, FormatFailure(result.ErrorCode, result.ErrorMessage, result.ErrorDetails));
        Assert.Equal(1, await ExecuteScalarAsync<long>(connection, "PRAGMA foreign_keys;"));
        Assert.Equal("wal", (await ExecuteScalarAsync<string>(connection, "PRAGMA journal_mode;")).ToLowerInvariant());
        Assert.Equal(2, await ExecuteScalarAsync<long>(connection, "PRAGMA synchronous;"));
        Assert.Equal(4321, await ExecuteScalarAsync<long>(connection, "PRAGMA busy_timeout;"));
    }

    [Fact]
    public async Task ConnectionFactory_InvalidDirectory_ReturnsPersistenceDirectoryInvalid()
    {
        var root = Path.Combine(Path.GetTempPath(), "PromFlow.PersistenceTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var fileAsDirectory = Path.Combine(root, "blocked");
            await File.WriteAllTextAsync(fileAsDirectory, "blocked");
            var options = new ArchiveDatabaseInitializationOptions
            {
                DatabasePath = Path.Combine(fileAsDirectory, "archive.sqlite")
            };
            var factory = new SqliteConnectionFactory();

            var result = await factory.OpenAsync(options, CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal("PersistenceDirectoryInvalid", result.ErrorCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ConnectionFactory_InstallationDirectory_ReturnsPersistencePathInvalid()
    {
        var options = new ArchiveDatabaseInitializationOptions
        {
            DatabasePath = Path.Combine(AppContext.BaseDirectory, "archive.sqlite")
        };
        var factory = new SqliteConnectionFactory();

        var result = await factory.OpenAsync(options, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("PersistencePathInvalid", result.ErrorCode);
    }

    [Fact]
    public async Task ConnectionFactory_InMemoryDatabase_ReturnsPersistencePathInvalid()
    {
        var options = new ArchiveDatabaseInitializationOptions { DatabasePath = ":memory:" };
        var factory = new SqliteConnectionFactory();

        var result = await factory.OpenAsync(options, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("PersistencePathInvalid", result.ErrorCode);
    }

    private static async Task<T> ExecuteScalarAsync<T>(SqliteConnection connection, string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var value = await command.ExecuteScalarAsync();

        return (T)Convert.ChangeType(value!, typeof(T));
    }

    private static string FormatFailure(string? code, string? message, string? details)
        => $"{code}: {message} {details}";
}
