using System.Reflection;
using System.Text;

namespace Configurator.Infrastructure.Persistence.Sqlite;

public sealed class SqliteMigrationCatalog
{
    private const string FoundationMigrationResourceSuffix = "Migrations.001_archive_foundation.sql";

    public SqliteMigrationCatalog()
    {
        All = new[] { LoadFoundationMigration() };
    }

    public IReadOnlyList<SqliteMigration> All { get; }

    private static SqliteMigration LoadFoundationMigration()
    {
        var assembly = typeof(SqliteMigrationCatalog).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .Single(name => name.EndsWith(FoundationMigrationResourceSuffix, StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded migration resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var sql = reader.ReadToEnd();

        return SqliteMigration.Create(1, "archive_foundation", sql);
    }
}
