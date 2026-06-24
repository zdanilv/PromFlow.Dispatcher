using Microsoft.Data.Sqlite;
using Xunit;

namespace Configurator.Infrastructure.Persistence.Tests.Security;

public sealed class SecurityMigrationRunnerTests
{
    [Fact]
    public void SecurityMigrationCatalog_LoadsUserAccountsMigration()
    {
        using var fixture = new SecurityTestFixture();
        var catalog = fixture.MigrationCatalog;

        var migration = Assert.Single(catalog.All);
        Assert.Equal(1, migration.Version);
        Assert.Equal("user_accounts", migration.Name);
        Assert.Contains("CREATE TABLE app_user", migration.Sql);
        Assert.Matches("^[0-9a-f]{64}$", migration.Checksum);
    }

    [Fact]
    public async Task Initialize_FirstRepeatedAndConcurrentRuns_CreateExpectedSchemaOnce()
    {
        using var fixture = new SecurityTestFixture();

        var first = await fixture.MigrationRunner.InitializeAsync(CancellationToken.None);
        var second = await fixture.MigrationRunner.InitializeAsync(CancellationToken.None);
        var concurrent = await Task.WhenAll(
            fixture.MigrationRunner.InitializeAsync(CancellationToken.None),
            fixture.MigrationRunner.InitializeAsync(CancellationToken.None));

        Assert.True(first.Succeeded, first.ErrorMessage);
        Assert.True(second.Succeeded, second.ErrorMessage);
        Assert.All(concurrent, result => Assert.True(result.Succeeded, result.ErrorMessage));

        await using var connection = await fixture.ConnectionFactory.OpenAsync(CancellationToken.None);
        Assert.True(await ObjectExistsAsync(connection, "table", "security_schema_migration"));
        Assert.True(await ObjectExistsAsync(connection, "table", "app_user"));
        Assert.True(await ObjectExistsAsync(connection, "index", "ix_app_user_enabled"));
        Assert.True(await ObjectExistsAsync(connection, "index", "ix_app_user_role"));
        Assert.Equal(1, await ExecuteScalarAsync<long>(connection, "SELECT COUNT(*) FROM security_schema_migration WHERE version = 1;"));
    }

    private static async Task<bool> ObjectExistsAsync(SqliteConnection connection, string type, string name)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = @type AND name = @name;
            """;
        command.Parameters.AddWithValue("@type", type);
        command.Parameters.AddWithValue("@name", name);

        return Convert.ToInt64(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<T> ExecuteScalarAsync<T>(SqliteConnection connection, string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var value = await command.ExecuteScalarAsync();

        return (T)Convert.ChangeType(value!, typeof(T));
    }
}
