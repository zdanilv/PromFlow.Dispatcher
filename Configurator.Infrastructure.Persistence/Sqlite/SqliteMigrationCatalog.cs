using System.Reflection;
using System.Text;

namespace Configurator.Infrastructure.Persistence.Sqlite;

public sealed class SqliteMigrationCatalog
{
    private const string FoundationMigrationResourceSuffix = "Migrations.001_archive_foundation.sql";
    private const string CommandWriteAuditMigrationResourceSuffix = "Migrations.002_command_write_audit.sql";

    public SqliteMigrationCatalog()
    {
        All =
        [
            LoadMigration(1, "archive_foundation", FoundationMigrationResourceSuffix),
            LoadMigration(2, "command_write_audit", CommandWriteAuditMigrationResourceSuffix)
        ];
    }

    public IReadOnlyList<SqliteMigration> All { get; }

    private static SqliteMigration LoadMigration(
        int version,
        string name,
        string resourceSuffix)
    {
        var assembly = typeof(SqliteMigrationCatalog).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .Single(candidate => candidate.EndsWith(resourceSuffix, StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded migration resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var sql = reader.ReadToEnd();

        return SqliteMigration.Create(version, name, sql);
    }
}
