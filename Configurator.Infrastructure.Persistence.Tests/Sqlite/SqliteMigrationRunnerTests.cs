using Configurator.Application.Services.Archiving;
using Configurator.Infrastructure.Persistence.Sqlite;
using Configurator.Infrastructure.Persistence.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Configurator.Infrastructure.Persistence.Tests.Sqlite;

public sealed class SqliteMigrationRunnerTests
{
    [Fact]
    public void SqliteMigrationCatalog_LoadsFoundationMigrationWithStableChecksum()
    {
        var catalog = new SqliteMigrationCatalog();

        var migration = Assert.Single(catalog.All);
        Assert.Equal(1, migration.Version);
        Assert.Equal("archive_foundation", migration.Name);
        Assert.Contains("CREATE TABLE archive_partition_metadata", migration.Sql);
        Assert.Matches("^[0-9a-f]{64}$", migration.Checksum);
        Assert.Equal("f22e3f7e4088b4b84db230aa30219e4c5fa0e496999965720021a8df1095bb51", migration.Checksum);
    }

    [Fact]
    public async Task Initialize_FirstRun_CreatesLedgerMetadataTablesAndIndexes()
    {
        using var database = new TempArchiveDatabase();
        var runner = CreateRunner();

        var result = await runner.InitializeAsync(database.CreateOptions(), CancellationToken.None);

        AssertSucceeded(result);
        using var connection = database.OpenConnection();
        Assert.True(await ObjectExistsAsync(connection, "table", "schema_migration"));
        Assert.True(await ObjectExistsAsync(connection, "table", "archive_partition_metadata"));
        Assert.True(await ObjectExistsAsync(connection, "table", "modbus_snapshot"));
        Assert.True(await ObjectExistsAsync(connection, "table", "runtime_event"));
        Assert.True(await ObjectExistsAsync(connection, "index", "ux_modbus_snapshot_sequence"));
        Assert.True(await ObjectExistsAsync(connection, "index", "ix_modbus_snapshot_time"));
        Assert.True(await ObjectExistsAsync(connection, "index", "ix_runtime_event_time"));
        Assert.Equal(1, await ExecuteScalarAsync<long>(connection, "SELECT COUNT(*) FROM schema_migration WHERE version = 1;"));
        Assert.Equal(1, await ExecuteScalarAsync<long>(connection, "SELECT archive_schema_version FROM archive_partition_metadata WHERE id = 1;"));
        Assert.Equal("device-1", await ExecuteScalarAsync<string>(connection, "SELECT device_id FROM archive_partition_metadata WHERE id = 1;"));
    }

    [Fact]
    public async Task Initialize_RepeatedRun_IsIdempotent()
    {
        using var database = new TempArchiveDatabase();
        var runner = CreateRunner();
        var options = database.CreateOptions();

        AssertSucceeded(await runner.InitializeAsync(options, CancellationToken.None));
        AssertSucceeded(await runner.InitializeAsync(options, CancellationToken.None));

        using var connection = database.OpenConnection();
        Assert.Equal(1, await ExecuteScalarAsync<long>(connection, "SELECT COUNT(*) FROM schema_migration WHERE version = 1;"));
        Assert.Equal(1, await ExecuteScalarAsync<long>(connection, "SELECT COUNT(*) FROM archive_partition_metadata WHERE id = 1;"));
    }

    [Fact]
    public async Task Initialize_ConcurrentCalls_BothSucceedAndLedgerHasSingleVersion()
    {
        using var database = new TempArchiveDatabase();
        var options = database.CreateOptions(busyTimeoutMs: 10000);
        var firstRunner = CreateRunner();
        var secondRunner = CreateRunner();

        var results = await Task.WhenAll(
            firstRunner.InitializeAsync(options, CancellationToken.None),
            secondRunner.InitializeAsync(options, CancellationToken.None));

        Assert.All(results, AssertSucceeded);
        using var connection = database.OpenConnection();
        Assert.Equal(1, await ExecuteScalarAsync<long>(connection, "SELECT COUNT(*) FROM schema_migration WHERE version = 1;"));
    }

    [Fact]
    public async Task Initialize_ChecksumMismatch_ReturnsPersistenceMigrationChecksumMismatch()
    {
        using var database = new TempArchiveDatabase();
        var options = database.CreateOptions();
        AssertSucceeded(await CreateRunner().InitializeAsync(options, CancellationToken.None));
        var original = new SqliteMigrationCatalog().All[0];
        var changedSql = original.Sql + Environment.NewLine + "-- changed";
        var changedMigration = original with
        {
            Sql = changedSql,
            Checksum = SqliteMigration.ComputeChecksum(changedSql)
        };
        var runner = CreateRunner([changedMigration]);

        var result = await runner.InitializeAsync(options, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("PersistenceMigrationChecksumMismatch", result.ErrorCode);
    }

    [Fact]
    public async Task Initialize_FailingMigration_RollsBackTransaction()
    {
        using var database = new TempArchiveDatabase();
        var failingMigration = SqliteMigration.Create(
            1,
            "failing_custom_migration",
            """
            CREATE TABLE partial_custom_table (
                id INTEGER PRIMARY KEY
            );

            INSERT INTO missing_custom_table (id) VALUES (1);
            """);
        var runner = CreateRunner([failingMigration]);

        var result = await runner.InitializeAsync(database.CreateOptions(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("PersistenceMigrationFailed", result.ErrorCode);
        using var connection = database.OpenConnection();
        Assert.False(await ObjectExistsAsync(connection, "table", "partial_custom_table"));
    }

    private static SqliteMigrationRunner CreateRunner(IReadOnlyList<SqliteMigration>? migrations = null)
        => migrations is null
            ? new SqliteMigrationRunner(new SqliteConnectionFactory(), new SqlitePragmaInitializer(), new SqliteMigrationCatalog())
            : new SqliteMigrationRunner(new SqliteConnectionFactory(), new SqlitePragmaInitializer(), migrations);

    private static void AssertSucceeded(ArchiveOperationResult result)
        => Assert.True(result.Succeeded, $"{result.ErrorCode}: {result.ErrorMessage} {result.ErrorDetails}");

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
        var count = await command.ExecuteScalarAsync();

        return Convert.ToInt64(count) == 1;
    }

    private static async Task<T> ExecuteScalarAsync<T>(SqliteConnection connection, string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var value = await command.ExecuteScalarAsync();

        return (T)Convert.ChangeType(value!, typeof(T));
    }
}
